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
//   send    --send-label <label> --stop <label>... --approve <label>...
//           -> {"ok":true,"sent":true}; preserves the current draft, refuses ambiguous targets.
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
    VizhiAxBridge <status|inspect|press|press-exact|voice|write|send|focus> --app <bundle-id> [verb args]  (see source header)
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
    let depth: Int      // DFS depth — what lets a flat scan recover subtree boundaries
}

// The app can have ChatGPT and Codex windows open at once. Scope every read and press to the
// focused (or main) window; walking every window lets the first mode label win and can press a
// control in a background window. If AX names neither and there is more than one window, fail
// closed with no surface rather than guessing.
func targetWindows() -> [AXUIElement] {
    if let focused = attr(appEl, kAXFocusedWindowAttribute as String) {
        return [focused as! AXUIElement]
    }
    if let main = attr(appEl, kAXMainWindowAttribute as String) {
        return [main as! AXUIElement]
    }
    let windows = (attr(appEl, kAXWindowsAttribute as String) as? [AXUIElement]) ?? []
    return windows.count == 1 ? windows : []
}

// Capture once per invocation. A re-scan must never retarget another window after a wait.
let operationWindows = targetWindows()

// One DFS over the target window (never the menu bar — thousands of AXMenuItems of pure noise).
// Returns tree order, which the card-text heuristic depends on.
func scanWindows() -> (nodes: [Node], webArea: Bool) {
    var nodes: [Node] = []
    var webArea = false
    var complete = true
    var visited = 0
    func rec(_ el: AXUIElement, _ depth: Int) {
        if depth > MAX_DEPTH || visited >= MAX_NODES { complete = false; return }
        visited += 1
        let role = str(el, kAXRoleAttribute as String) ?? "?"
        if role == "AXWebArea" { webArea = true }
        nodes.append(Node(el: el, role: role, text: displayText(el),
                          pressable: actionNames(el).contains(kAXPressAction as String), depth: depth))
        for c in children(el) { rec(c, depth + 1) }
    }
    for w in operationWindows { rec(w, 0) }
    // A partial tree cannot establish uniqueness or rule out an approval/running task.
    return (nodes, webArea && complete)
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

// Exact button identity is mandatory for native voice and task Stop. In particular, neither
// a chat title containing a label nor a longer button label may be used as a fallback.
func exactButtons(matching labels: [String], in nodes: [Node]) -> [Node] {
    let names = labels.filter { !$0.isEmpty }
    return nodes.indices.compactMap { i in
        let node = nodes[i]
        guard node.role == "AXButton", names.contains(node.text) else { return nil }
        // Sidebar conversation rows can themselves be AXButtons with arbitrary user titles.
        // Their nested pin/archive controls distinguish them from a leaf action button.
        var j = i + 1
        while j < nodes.count && nodes[j].depth > node.depth {
            if nodes[j].pressable { return nil }
            j += 1
        }
        return node
    }
}

func uniqueEnabledButton(matching labels: [String], in nodes: [Node]) -> Node? {
    let matches = exactButtons(matching: labels, in: nodes)
    guard matches.count == 1, matches[0].pressable,
          (attr(matches[0].el, kAXEnabledAttribute as String) as? Bool) == true else { return nil }
    return matches[0]
}

func voiceState(nodes: [Node], start: [String], end: [String]) -> String {
    guard !start.isEmpty, !end.isEmpty else { return "unavailable" }
    let endings = exactButtons(matching: end, in: nodes)
    // Even a disabled End button is evidence of a session. It must block starting another.
    if !endings.isEmpty { return endings.count == 1 ? "active" : "unavailable" }
    return uniqueEnabledButton(matching: start, in: nodes) != nil ? "ready" : "unavailable"
}

func voiceTarget(action: String, nodes: [Node], start: [String], end: [String]) -> Node? {
    guard ["start", "end"].contains(action),
          voiceState(nodes: nodes, start: start, end: end) == (action == "start" ? "ready" : "active") else { return nil }
    return uniqueEnabledButton(matching: action == "start" ? start : end, in: nodes)
}

// Send is never a substring search over the entire window. Require exactly one composer,
// a non-empty draft, and an enabled exact Send button in a shared local container. The
// window/web area are too broad to establish that relationship. Unknown layouts fail closed.
func composerSendTarget(nodes: [Node], label: String, enabled: (Node) -> Bool) -> Node? {
    let composers = nodes.indices.filter { nodes[$0].role == "AXTextArea" }
    guard !label.isEmpty, composers.count == 1 else { return nil }
    let index = composers[0]
    guard !nodes[index].text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
    var depth = nodes[index].depth
    for i in (0..<index).reversed() where nodes[i].depth < depth {
        let ancestor = nodes[i]
        if ["AXWindow", "AXWebArea"].contains(ancestor.role) { return nil }
        depth = ancestor.depth
        let end = nodes[(i + 1)...].firstIndex { $0.depth <= depth } ?? nodes.count
        let matches = nodes[(i + 1)..<end].filter { $0.pressable && $0.text == label }
        if !matches.isEmpty {
            return matches.count == 1 && enabled(matches[0]) ? matches[0] : nil
        }
    }
    return nil
}

func sendTarget(in nodes: [Node]) -> Node? {
    // AXValue, not the text area's placeholder/description, establishes that a draft exists.
    let composers = nodes.filter { $0.role == "AXTextArea" }
    guard composers.count == 1,
          let draft = str(composers[0].el, kAXValueAttribute as String),
          !draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
          exactButtons(matching: argValues("--stop"), in: nodes).isEmpty,
          firstPressable(matching: argValues("--approve"), in: nodes) == nil else { return nil }
    return composerSendTarget(nodes: nodes, label: argValue("--send-label") ?? "") {
        (attr($0.el, kAXEnabledAttribute as String) as? Bool) == true
    }
}

func sameOperationWindow() -> Bool {
    let current = targetWindows()
    return operationWindows.count == 1 && current.count == 1 && CFEqual(operationWindows[0], current[0])
}

// Exact titles only, and only rows with the adapter's sidebar marker in their subtree.
func conversationMatches(title: String, marker: String, nodes: [Node]) -> [Node] {
    guard !title.isEmpty && !marker.isEmpty else { return [] }
    return nodes.indices.compactMap { i in
        let n = nodes[i]
        guard n.pressable && n.text == title else { return nil }
        var j = i + 1
        while j < nodes.count && nodes[j].depth > n.depth {
            if nodes[j].pressable && nodes[j].text == marker { return n }
            j += 1
        }
        return nil
    }
}

// The baseline comes from verified app/mode controls, never from possibly-running peers.
func conversationState(state: String, images: Int, baseline: Int?) -> String {
    guard state == "idle", let baseline = baseline, baseline >= 0 else { return state }
    return images > baseline ? "running" : state
}

// Whitespace-collapse: the card is laid out for a window (newlines, runs of spaces); consumers
// get one clean line. Also what makes card-text comparison stable across reads.
func collapse(_ s: String) -> String {
    s.components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }.joined(separator: " ")
}

// The card's description: AXStaticText nodes directly BEFORE an anchor button in tree order
// (verified live). One definition, used by status (to report) and press (to verify) — the
// expected-card guard is only sound if both sides compute the same string.
func cardText(before anchor: Node, in nodes: [Node]) -> String {
    guard let idx = nodes.firstIndex(where: { $0.el === anchor.el }) else { return "" }
    let windowStart = max(0, idx - 20)
    let statics = nodes[windowStart..<idx].filter { $0.role == "AXStaticText" && !$0.text.isEmpty }
    var text = collapse(statics.suffix(3).map { $0.text }.joined(separator: " "))
    if text.count > CARD_TEXT_CAP { text = String(text.prefix(CARD_TEXT_CAP)) }
    return text
}

// MARK: - verbs

switch verb {
case "inspect":
    // Recon only: report controls and activity-shaped nodes, not message/static-text content.
    // This keeps UI-label drift diagnosable without dumping the user's conversation into logs.
    let (nodes, webArea) = scanWindows()
    let interesting = nodes.filter {
        $0.pressable || $0.role == "AXImage" || $0.role == "AXProgressIndicator" ||
            $0.role == "AXBusyIndicator"
    }.map {
        ["role": $0.role, "text": $0.text, "pressable": $0.pressable ? "true" : "false",
         "depth": String($0.depth)]
    }
    emit(["surface": webArea, "nodes": interesting], code: 0)

case "status":
    let (nodes, webArea) = scanWindows()
    if !webArea {
        // Screen locked / window hidden: the tree evaporates. Say so — the monitor must treat
        // this as SURFACE UNAVAILABLE, never as "everything resolved".
        emit(["surface": false], code: 0)
    }

    let approve = firstPressable(matching: argValues("--approve"), in: nodes)
    let deny = firstPressable(matching: argValues("--deny"), in: nodes)
    let stop = exactButtons(matching: argValues("--stop"), in: nodes).first

    func present(_ argument: String) -> Bool {
        firstPressable(matching: argValues(argument), in: nodes) != nil
    }

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

    // The card's description — empty when the layout surprises us; an honest "" beats a guess.
    let card = approve.map { cardText(before: $0, in: nodes) } ?? ""

    // Sidebar conversations — a pressable button whose SUBTREE contains the item marker (the
    // app-specific per-row control, e.g. a pin button, passed as --conv-marker so this stays
    // app-agnostic). DFS order is the sidebar's own order, i.e. recency. State, verified live
    // 2026-08-25: "awaiting"/"unread" are literal static texts on the row; "running" has NO text,
    // only an extra activity image. The idle baseline differs by mode (ChatGPT: pin; Codex:
    // pin + archive), so the adapter supplies the verified idle count for each mode.
    var readings: [(title: String, state: String, selected: Bool, images: Int)] = []
    if let convMarker = argValue("--conv-marker"), !convMarker.isEmpty {
        let awaiting = argValue("--state-awaiting") ?? ""
        let unread = argValue("--state-unread") ?? ""
        var i = 0
        while i < nodes.count && readings.count < 8 {
            let n = nodes[i]
            guard n.pressable && !n.text.isEmpty else { i += 1; continue }

            var j = i + 1
            var hasMarker = false
            var state = "idle"
            var images = 0
            while j < nodes.count && nodes[j].depth > n.depth {
                let m = nodes[j]
                if m.pressable && m.text == convMarker { hasMarker = true }
                if m.role == "AXStaticText" {
                    if !awaiting.isEmpty && m.text.contains(awaiting) { state = "awaiting" }
                    else if !unread.isEmpty && m.text == unread && state == "idle" { state = "unread" }
                }
                if m.role == "AXImage" && m.text.isEmpty { images += 1 }
                j += 1
            }

            if hasMarker {
                // Which conversation is OPEN matters to approval identity: the card only ever
                // belongs to the open one. AXSelected is the app's own answer — carried by the
                // row's container, not the button (checked live), so climb a couple of parents.
                var selected = (attr(n.el, "AXSelected" as String) as? Bool) ?? false
                var cur: AXUIElement = n.el
                for _ in 0..<2 where !selected {
                    guard let p = attr(cur, kAXParentAttribute as String) else { break }
                    cur = p as! AXUIElement
                    selected = (attr(cur, "AXSelected" as String) as? Bool) ?? false
                }
                readings.append((n.text, state, selected, images))
                i = j          // skip the subtree so row controls never read as items
            } else {
                i += 1
            }
        }
    }

    let baselineImages = argValues("--idle-images").compactMap { entry -> Int? in
        let parts = entry.split(separator: "=", maxSplits: 1)
        return parts.count == 2 && String(parts[0]) == mode ? Int(parts[1]) : nil
    }.first
    let conversations: [[String: String]] = readings.map { reading in
        let state = conversationState(state: reading.state, images: reading.images, baseline: baselineImages)
        return ["title": reading.title, "state": state,
                "selected": reading.selected ? "true" : "false"]
    }

    emit([
        "surface": true,
        "attention": attention,
        "approvalPresent": approve != nil,
        "denyPresent": deny != nil,
        "stopPresent": stop != nil,
        "voiceChat": voiceState(nodes: nodes, start: argValues("--voice-start"), end: argValues("--voice-end")),
        "canSend": sendTarget(in: nodes) != nil,
        // No verified assistant-message ownership selector in this adapter yet. In particular,
        // the last arbitrary "Copy message" button can belong to the user or an older answer.
        "canCopyAnswer": false,
        "searchPresent": present("--search"),
        "changesPresent": present("--changes"),
        "projectsPresent": present("--projects"),
        "pluginsPresent": present("--plugins"),
        "attachFilesPresent": present("--attach-files"),
        "permissionsPresent": present("--permissions"),
        "scheduledPresent": present("--scheduled"),
        "pullRequestsPresent": present("--pull-requests"),
        "explorePresent": present("--explore"),
        "quickChatPresent": present("--quick-chat"),
        "cardText": card,
        "mode": mode,
        "conversations": conversations,
    ], code: 0)

case "press", "press-exact":
    let labels = argValues("--label")
    if labels.isEmpty { fail("no --label given", 4) }
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    let before = frontmostName()
    let currentScan = scanWindows()
    if !currentScan.webArea { fail("surface-unavailable", 5) }
    let pressScan = currentScan.nodes
    if let expectedMode = argValue("--expect-mode") {
        let prefix = argValue("--mode-prefix") ?? ""
        let modes = pressScan.filter { !prefix.isEmpty && $0.text.hasPrefix(prefix) }
        guard modes.count == 1, modes[0].text == prefix + expectedMode else { fail("mode-changed", 6) }
    }
    let candidate: Node?
    if let marker = argValue("--conversation") {
        let matches = conversationMatches(title: labels[0], marker: marker, nodes: pressScan)
        if matches.count > 1 { fail("ambiguous-conversation", 4) }
        candidate = matches.first
    } else if verb == "press-exact" {
        candidate = uniqueEnabledButton(matching: labels, in: pressScan)
    } else {
        candidate = firstPressable(matching: labels, in: pressScan)
    }
    guard let target = candidate else {
        fail("no-match", 4)
    }

    // The expected-card guard: the caller names the card text it SAW; if what is beside the
    // button now is a different card (the old one resolved, a new one appeared between the
    // keypad's render and the thumb), refuse — "card-changed" makes the user look, which is
    // the entire point. Containment either way absorbs the report-side length cap.
    if let expect = argValue("--expect-near").map(collapse), !expect.isEmpty {
        let seen = cardText(before: target, in: pressScan)
        if seen.isEmpty || !(seen.contains(expect) || expect.contains(seen)) {
            fail("card-changed", 6)
        }
    }

    if hasFlag("--dry") {
        emit(["matched": target.text, "dry": true], code: 0)
    }
    guard sameOperationWindow() else { fail("window-changed", 6) }
    let rc = AXUIElementPerformAction(target.el, kAXPressAction as CFString)
    if rc != .success { fail("press-failed rc=\(rc.rawValue)", 5) }
    emit(["matched": target.text, "frontBefore": before, "frontAfter": frontmostName()], code: 0)

case "voice":
    let action = argValue("--action") ?? ""
    guard ["start", "end"].contains(action) else { fail("invalid-voice-action", 4) }
    let starts = argValues("--voice-start")
    let ends = argValues("--voice-end")
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    let initial = scanWindows()
    guard initial.webArea, sameOperationWindow(),
          let target = voiceTarget(action: action, nodes: initial.nodes, start: starts, end: ends) else {
        fail("voice-state-changed", 6)
    }
    let title = str(operationWindows[0], kAXTitleAttribute as String)
    let latest = scanWindows()
    guard latest.webArea, sameOperationWindow(),
          str(operationWindows[0], kAXTitleAttribute as String) == title,
          let confirmed = voiceTarget(action: action, nodes: latest.nodes, start: starts, end: ends),
          CFEqual(target.el, confirmed.el) else { fail("voice-state-changed", 6) }
    guard AXUIElementPerformAction(confirmed.el, kAXPressAction as CFString) == .success else {
        fail("voice-press-failed", 5)
    }
    // Request accepted is all we know. Setup dialogs, connection failure, or user cancellation
    // can follow; only subsequent status observations may render an active session.
    emit(["requested": action], code: 0)

case "send":
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    let initialScan = scanWindows()
    if !initialScan.webArea { fail("surface-unavailable", 5) }
    let initial = initialScan.nodes
    guard let target = sendTarget(in: initial),
          let composer = initial.first(where: { $0.role == "AXTextArea" }),
          let draft = str(composer.el, kAXValueAttribute as String) else { fail("no-sendable-draft", 4) }
    let windowTitle = str(operationWindows[0], kAXTitleAttribute as String)
    let latestScan = scanWindows()
    let latest = latestScan.nodes
    guard latestScan.webArea, sameOperationWindow(),
          str(operationWindows[0], kAXTitleAttribute as String) == windowTitle,
          latest.contains(where: { $0.role == "AXTextArea" && CFEqual($0.el, composer.el) }),
          str(composer.el, kAXValueAttribute as String) == draft,
          let confirmed = sendTarget(in: latest), CFEqual(confirmed.el, target.el) else {
        fail("composer-target-changed", 6)
    }
    if AXUIElementPerformAction(target.el, kAXPressAction as CFString) != .success {
        fail("send-press-failed", 5)
    }
    emit(["sent": true], code: 0)

case "write":
    guard let text = argValue("--text"), !text.isEmpty else { fail("no --text given", 4) }
    if !waitForWebContent(seconds: 2) { fail("surface-unavailable", 5) }
    let writeScan = scanWindows()
    if !writeScan.webArea { fail("surface-unavailable", 5) }
    let composers = writeScan.nodes.filter { $0.role == "AXTextArea" }
    guard composers.count == 1, sameOperationWindow() else { fail("no-unique-composer", 4) }
    let composer = composers[0]
    guard let existing = str(composer.el, kAXValueAttribute as String) else {
        fail("composer-value-unavailable", 4)
    }
    if !existing.isEmpty { fail("draft-exists", 4) }

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
    if argValue("--send-label") != nil {
        // Abort if the user changed windows or the composer was replaced during the write.
        // Even this final scan stays scoped to the original window.
        let currentWindows = targetWindows()
        let sendScan = scanWindows()
        let sendNodes = sendScan.nodes
        guard sendScan.webArea, operationWindows.count == 1 && currentWindows.count == 1,
              CFEqual(operationWindows[0], currentWindows[0]),
              sendNodes.contains(where: { CFEqual($0.el, composer.el) }),
              str(composer.el, kAXValueAttribute as String) == text else {
            fail("composer-target-changed", 6)
        }
        guard let send = sendTarget(in: sendNodes) else {
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
