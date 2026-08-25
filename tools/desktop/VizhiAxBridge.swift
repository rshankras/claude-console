// VizhiAxBridge — one-shot AX automation verbs for desktop agent apps (macOS).
//
// The plugin's hands and eyes on a GUI agent (the OpenAI desktop app first; any Electron
// agent later). Every invocation does ONE thing and exits with ONE line of JSON on stdout —
// deliberately not a daemon: the plugin spawns it through BoundedProcess (timeout + kill-tree),
// which is the repo's only sanctioned child-process shape after the 4096-thread SIGABRT
// (see src/Core/Platform/BoundedProcess.cs).
//
// App-agnostic BY CONTRACT: this binary knows no control labels and no app names. Which app to
// drive (--app) and which labels mean approve/deny/stop/attention (--approve/--deny/...) arrive
// as arguments from the product's IDesktopAppAdapter. Keep it that way — a second desktop app
// must cost an adapter, not a helper fork.
//
// Verbs (each takes --app <bundle-id>):
//   status  --approve <label>... --deny <label> --stop <label> --attention <substr>
//           --mode-prefix <prefix>
//           -> {"ok":true,"surface":bool,"attention":bool,"approvalPresent":bool,
//               "denyPresent":bool,"stopPresent":bool,"cardText":"...","mode":"Codex"}
//           surface=false means the web AX tree is gone (screen locked / window hidden):
//           report it, never guess. A missing web area is NOT "card resolved".
//   press   --label <text>... [--dry]
//           -> {"ok":true,"matched":"...","frontBefore":"...","frontAfter":"..."}
//   write   --text <text> [--send-label <label>]
//           -> {"ok":true,"method":"value"|"selectedText","sent":bool}
//           Set-value races React's async state: set -> settle -> verify -> fall back to
//           AXFocused+AXSelectedText (the editing pipeline) -> verify again. Proven 2026-08-24.
//   focus   -> {"ok":true}   the ONE deliberate focus: bring the app forward.
//
// Exit codes: 0 ok · 2 not AX-trusted · 3 app not running · 4 no match / not found · 5 AX error.
// AX trust rides on the RESPONSIBLE process: spawned directly by LogiPluginService this helper
// uses the service's existing Accessibility grant (same attribution as osascript typing today).
// Build: tools/desktop/build.sh

import Cocoa
import ApplicationServices

// MARK: - arguments

let argv = Array(CommandLine.arguments.dropFirst())
let verb = argv.first ?? "help"

func argValues(_ name: String) -> [String] {
    var out: [String] = []
    var i = 0
    while i < argv.count - 1 {
        if argv[i] == name { out.append(argv[i + 1]); i += 2 } else { i += 1 }
    }
    return out
}
func argValue(_ name: String) -> String? { argValues(name).first }
func hasFlag(_ name: String) -> Bool { argv.contains(name) }

let bundleId = argValue("--app") ?? "com.openai.codex"

// MARK: - JSON out

func emit(_ obj: [String: Any], code: Int32) -> Never {
    var payload = obj
    if payload["ok"] == nil { payload["ok"] = code == 0 }
    let data = (try? JSONSerialization.data(withJSONObject: payload)) ?? Data("{}".utf8)
    print(String(data: data, encoding: .utf8) ?? "{}")
    exit(code)
}
func fail(_ error: String, _ code: Int32) -> Never { emit(["ok": false, "error": error], code: code) }

// MARK: - AX plumbing (ported from the spike's probe.swift, proven on the live app)

func attr(_ el: AXUIElement, _ name: String) -> AnyObject? {
    var v: AnyObject?
    return AXUIElementCopyAttributeValue(el, name as CFString, &v) == .success ? v : nil
}
func str(_ el: AXUIElement, _ name: String) -> String? { attr(el, name) as? String }
func children(_ el: AXUIElement) -> [AXUIElement] {
    (attr(el, kAXChildrenAttribute as String) as? [AXUIElement]) ?? []
}
func actionNames(_ el: AXUIElement) -> [String] {
    var names: CFArray?
    return AXUIElementCopyActionNames(el, &names) == .success ? (names as? [String] ?? []) : []
}
func displayText(_ el: AXUIElement) -> String {
    for key in [kAXTitleAttribute, kAXValueAttribute, kAXDescriptionAttribute,
                "AXHelp", "AXLabel"] as [String] {
        if let s = str(el, key), !s.isEmpty { return s }
    }
    return ""
}

// Chromium builds no AX tree until an assistive client asks; re-assert on every invocation.
func forceAccessibility(_ appEl: AXUIElement) {
    AXUIElementSetAttributeValue(appEl, "AXManualAccessibility" as CFString, kCFBooleanTrue)
    AXUIElementSetAttributeValue(appEl, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue)
}

// MARK: - preflight

if verb == "help" || verb == "--help" {
    print("""
    VizhiAxBridge <status|press|write|focus> --app <bundle-id> [verb args]  (see source header)
    """)
    exit(0)
}

guard AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": false] as CFDictionary) else {
    fail("not-trusted", 2)
}
guard let runningApp = NSRunningApplication.runningApplications(withBundleIdentifier: bundleId).first else {
    fail("app-not-running", 3)
}
let appEl = AXUIElementCreateApplication(runningApp.processIdentifier)
forceAccessibility(appEl)

// MARK: - scan

let MAX_DEPTH = 40
let MAX_NODES = 1500        // windows-only walk measured ~350 nodes on the live app; headroom, not budget
let CARD_TEXT_CAP = 400

struct Node {
    let el: AXUIElement
    let role: String
    let text: String
    let pressable: Bool
}

// One DFS over the app's WINDOWS (never the menu bar — thousands of AXMenuItems of pure noise).
// Returns tree order, which the card-text heuristic depends on.
func scanWindows() -> (nodes: [Node], webArea: Bool) {
    var nodes: [Node] = []
    var webArea = false
    var visited = 0
    func rec(_ el: AXUIElement, _ depth: Int) {
        if depth > MAX_DEPTH || visited > MAX_NODES { return }
        visited += 1
        let role = str(el, kAXRoleAttribute as String) ?? "?"
        if role == "AXWebArea" { webArea = true }
        nodes.append(Node(el: el, role: role, text: displayText(el),
                          pressable: actionNames(el).contains(kAXPressAction as String)))
        for c in children(el) { rec(c, depth + 1) }
    }
    for w in (attr(appEl, kAXWindowsAttribute as String) as? [AXUIElement]) ?? [] { rec(w, 0) }
    return (nodes, webArea)
}

// Bounded wait for the web content tree — used by press/write (which must not act on a half
// tree), NOT by status (a status poll reports surface=false immediately and cheaply).
func waitForWebContent(seconds: Double) -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    repeat {
        forceAccessibility(appEl)
        if scanWindows().webArea { return true }
        Thread.sleep(forTimeInterval: 0.2)
    } while Date() < deadline
    return false
}

func frontmostName() -> String { NSWorkspace.shared.frontmostApplication?.localizedName ?? "?" }

func firstPressable(matching labels: [String], in nodes: [Node]) -> Node? {
    let needles = labels.map { $0.lowercased() }
    return nodes.first { n in
        n.pressable && !n.text.isEmpty && needles.contains { n.text.lowercased().contains($0) }
    }
}

// MARK: - verbs

switch verb {
case "status":
    let (nodes, webArea) = scanWindows()
    if !webArea {
        // Screen locked / window hidden: the tree evaporates. Say so — the monitor must treat
        // this as SURFACE UNAVAILABLE, never as "everything resolved".
        emit(["surface": false], code: 0)
    }

    let approve = firstPressable(matching: argValues("--approve"), in: nodes)
    let deny = firstPressable(matching: argValues("--deny"), in: nodes)
    let stop = firstPressable(matching: argValues("--stop"), in: nodes)

    var attention = false
    if let marker = argValue("--attention")?.lowercased(), !marker.isEmpty {
        attention = nodes.contains { $0.text.lowercased().contains(marker) }
    }

    var mode = ""
    if let prefix = argValue("--mode-prefix"), !prefix.isEmpty {
        if let m = nodes.first(where: { $0.text.hasPrefix(prefix) }) {
            mode = String(m.text.dropFirst(prefix.count))
        }
    }

    // The card's description renders as AXStaticText nodes directly BEFORE the approve button
    // in tree order (verified in the spike dump). Positional heuristic, capped; empty when the
    // layout surprises us — an honest "" beats a guessed string.
    var cardText = ""
    if let approve = approve, let idx = nodes.firstIndex(where: { $0.el === approve.el }) {
        let windowStart = max(0, idx - 20)
        let statics = nodes[windowStart..<idx].filter { $0.role == "AXStaticText" && !$0.text.isEmpty }
        cardText = statics.suffix(3).map { $0.text }.joined(separator: " ")
        if cardText.count > CARD_TEXT_CAP { cardText = String(cardText.prefix(CARD_TEXT_CAP)) }
    }

    emit([
        "surface": true,
        "attention": attention,
        "approvalPresent": approve != nil,
        "denyPresent": deny != nil,
        "stopPresent": stop != nil,
        "cardText": cardText,
        "mode": mode,
    ], code: 0)

case "press":
    let labels = argValues("--label")
    if labels.isEmpty { fail("no --label given", 4) }
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    let before = frontmostName()
    guard let target = firstPressable(matching: labels, in: scanWindows().nodes) else {
        fail("no-match", 4)
    }
    if hasFlag("--dry") {
        emit(["matched": target.text, "dry": true], code: 0)
    }
    let rc = AXUIElementPerformAction(target.el, kAXPressAction as CFString)
    if rc != .success { fail("press-failed rc=\(rc.rawValue)", 5) }
    emit(["matched": target.text, "frontBefore": before, "frontAfter": frontmostName()], code: 0)

case "write":
    guard let text = argValue("--text"), !text.isEmpty else { fail("no --text given", 4) }
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    guard let composer = scanWindows().nodes.first(where: { $0.role == "AXTextArea" }) else {
        fail("no-composer", 4)
    }

    // Plan A: raw value set. Readback races React's async state application, so settle first;
    // a stale readback does NOT mean failure (first sitting proved the set landed anyway) —
    // but only a VERIFIED readback lets us report method:"value" honestly.
    var method = "value"
    AXUIElementSetAttributeValue(composer.el, kAXValueAttribute as CFString, text as CFString)
    Thread.sleep(forTimeInterval: 0.20)
    var back = str(composer.el, kAXValueAttribute as String) ?? ""
    if !back.contains(text) {
        // Plan B: in-app keyboard focus (no app activation) + AXSelectedText — the editing
        // pipeline, which frameworks that ignore raw sets do honor.
        method = "selectedText"
        AXUIElementSetAttributeValue(composer.el, "AXFocused" as CFString, kCFBooleanTrue)
        Thread.sleep(forTimeInterval: 0.15)
        AXUIElementSetAttributeValue(composer.el, "AXSelectedText" as CFString, text as CFString)
        Thread.sleep(forTimeInterval: 0.20)
        back = str(composer.el, kAXValueAttribute as String) ?? ""
        if !back.contains(text) { fail("write-not-applied", 5) }
    }

    var sent = false
    if let sendLabel = argValue("--send-label") {
        // Re-scan: the send control may only exist once the composer is non-empty.
        guard let send = firstPressable(matching: [sendLabel], in: scanWindows().nodes) else {
            emit(["method": method, "sent": false, "error": "send-not-found"], code: 4)
        }
        if AXUIElementPerformAction(send.el, kAXPressAction as CFString) != .success {
            emit(["method": method, "sent": false, "error": "send-press-failed"], code: 5)
        }
        sent = true
    }
    emit(["method": method, "sent": sent], code: 0)

case "focus":
    runningApp.activate()
    emit([:], code: 0)

default:
    fail("unknown-verb \(verb)", 4)
}
