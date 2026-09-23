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
//           -> {"ok":true,"method":"value"|"selectedText"|"keyboard"|"existing","sent":bool}
//           Set-value races React's async state: set -> settle -> verify -> fall back to
//           AXFocused+AXSelectedText (the editing pipeline) -> verify again. Proven 2026-08-24.
//   append-target --expect-mode <mode> -> opaque target + draft fingerprint, without draft text.
//   append --expect-target <target> --expect-draft <fingerprint> --text <text> [--accept-existing]
//           Append beneath existing text; verify insertion, preserve clipboard, never send.
//   focus   -> {"ok":true}   the ONE deliberate focus: bring the app forward.
//   shortcut --key-code <macOS ANSI key code> --modifiers <control,shift,option,command>
//           -> {"ok":true,"requested":"shortcut"}; posts only to the already-frontmost app.
//           Does not scan controls, activate an app, or establish whether the app handled it.
//   send    --send-label <label> --stop <label>... --approve <label>...
//           -> {"ok":true,"sent":true}; preserves the current draft, refuses ambiguous targets.
//
// Exit codes: 0 ok · 2 not AX-trusted · 3 app not running · 4 no match / not found · 5 AX error.
// AX trust rides on the RESPONSIBLE process: spawned directly by LogiPluginService this helper
// uses the service's existing Accessibility grant (same attribution as osascript typing today).
// Build: tools/desktop/build.sh

import Cocoa
import ImageIO
import ApplicationServices
import CryptoKit

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
var imagePasteStarted = false
var draftAppendStarted = false
func fail(_ error: String, _ code: Int32) -> Never {
    // Once paste was posted, failure cannot establish that no attachment was created.
    emit(["ok": false, "error": imagePasteStarted ? "attachment-unconfirmed" : draftAppendStarted ? "append-unconfirmed" : error], code: code)
}

// Passive polling can back off in other apps without asking Chromium to build or traverse
// its accessibility tree. Explicit commands still use the normal guarded AX path.
if verb == "frontmost" {
    emit(["frontmost": NSWorkspace.shared.frontmostApplication?.bundleIdentifier == bundleId], code: 0)
}

// Rich editors can expose an empty/trailing paragraph through AXValue. Ignore only outer
// whitespace for eligibility/readback; preserve the original text when actually inserting.
// Never strip placeholder words, internal whitespace, or other visible content.
func comparableDraft(_ value: String) -> String {
    value.trimmingCharacters(in: .whitespacesAndNewlines)
}

func draftFingerprint(_ value: String) -> String {
    SHA256.hash(data: Data(comparableDraft(value).utf8)).map { String(format: "%02x", $0) }.joined()
}

func draftEndPrefixMatches(_ value: String, prefix: String, offset: Int) -> Bool {
    // Chromium's AX ranges omit rendered paragraph breaks that AXValue includes. Require
    // the complete range prefix, with its reported length, to contain all original text.
    // Only line breaks differ between these representations; spaces and words stay exact.
    func withoutBreaks(_ text: String) -> String {
        text.replacingOccurrences(of: "\r", with: "").replacingOccurrences(of: "\n", with: "")
    }
    return prefix.utf16.count == offset && withoutBreaks(prefix) == withoutBreaks(value)
}

func appendedDraftMatches(_ value: String, before: String, addition: String) -> Bool {
    let value = comparableDraft(value), addition = comparableDraft(addition)
    guard !addition.isEmpty, value.hasSuffix(addition) else { return false }
    let prefix = String(value.dropLast(addition.count))
    // AXValue may expose a rendered paragraph gap as just one newline. Require a real
    // separator and the exact original content, rather than a particular blank-line count.
    guard before == draftFingerprint("") || prefix.hasSuffix("\n") || prefix.hasSuffix("\r") else { return false }
    return draftFingerprint(prefix) == before
}

// MARK: - AX plumbing (ported from the spike's probe.swift, proven on the live app)

func attr(_ el: AXUIElement, _ name: String) -> AnyObject? {
    var v: AnyObject?
    return AXUIElementCopyAttributeValue(el, name as CFString, &v) == .success ? v : nil
}
func str(_ el: AXUIElement, _ name: String) -> String? { attr(el, name) as? String }

func draftSelection(_ composer: AXUIElement) -> CFRange? {
    guard let value = attr(composer, kAXSelectedTextRangeAttribute as String),
          CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
    let axValue = value as! AXValue
    var range = CFRange(location: 0, length: 0)
    guard AXValueGetType(axValue) == .cfRange,
          AXValueGetValue(axValue, .cfRange, &range), range.location >= 0, range.length >= 0 else { return nil }
    return range
}

// A description alone is not evidence of emptiness. Require an observed adapter prompt,
// the same description, the cursor at zero with no selection, and independent empty-state evidence.
func matchesDraftPlaceholder(value: String, description: String?, labels: [String]) -> Bool {
    let text = comparableDraft(value)
    return !text.isEmpty && labels.contains(text) && description.map(comparableDraft) == text
}

func isDraftPlaceholder(value: String, description: String?, labels: [String],
                        caret: CFRange?, sendUnavailable: Bool, emptyTextConfirmed: Bool = false) -> Bool {
    matchesDraftPlaceholder(value: value, description: description, labels: labels)
        && caret?.location == 0 && caret?.length == 0 && (sendUnavailable || emptyTextConfirmed)
}

func appendedPlaceholderMatches(_ value: String, before: String, addition: String,
                                description: String?, labels: [String]) -> Bool {
    // A CSS placeholder may be present in the prepared fingerprint but disappear on typing.
    // Explicit retry accepts only the complete instruction, in the same labelled composer.
    // Real placeholder-spelling drafts use the ordinary original-prefix check instead.
    !comparableDraft(addition).isEmpty && draftFingerprint(value) != before
        && comparableDraft(value) == comparableDraft(addition)
        && labels.contains { draftFingerprint($0) == before && description.map(comparableDraft) == $0 }
}

func rangeProvesEmptyDraft(emptyRange: String?, firstCharacter: String?, firstCharacterOutOfBounds: Bool) -> Bool {
    // An unsupported/missing range read is not proof. The empty range must work, and
    // the first UTF-16 unit must be explicitly empty or rejected as out of bounds.
    emptyRange == "" && (firstCharacter == "" || (firstCharacter == nil && firstCharacterOutOfBounds))
}

func emptyDraftRangeConfirmed(_ composer: AXUIElement) -> Bool {
    func read(_ length: Int) -> (AXError, String?) {
        var range = CFRange(location: 0, length: length)
        guard let parameter = AXValueCreate(.cfRange, &range) else { return (.failure, nil) }
        var result: CFTypeRef?
        let error = AXUIElementCopyParameterizedAttributeValue(composer, "AXStringForRange" as CFString, parameter, &result)
        return (error, error == .success ? result as? String : nil)
    }
    let empty = read(0)
    guard empty.0 == .success else { return false }
    let first = read(1)
    return rangeProvesEmptyDraft(emptyRange: empty.1, firstCharacter: first.1,
        firstCharacterOutOfBounds: first.0 == .illegalArgument)
}

func placeholderDraft(_ composer: AXUIElement, value: String, allowRangeEvidence: Bool = false) -> Bool {
    let labels = argValues("--draft-placeholder")
    guard labels.contains(comparableDraft(value)),
          let sendLabel = argValue("--composer-send-label"), !sendLabel.isEmpty else { return false }
    let scan = scanWindows()
    let editors = scan.nodes.filter { $0.role == "AXTextArea" }
    guard scan.webArea, editors.count == 1, CFEqual(editors[0].el, composer) else { return false }
    // Unknown enabled state is not empty-state evidence. Checking the whole pinned window
    // is intentionally conservative if more than one Send control is present.
    let sendUnavailable = exactButtons(matching: [sendLabel], in: scan.nodes).allSatisfy {
        (attr($0.el, kAXEnabledAttribute as String) as? Bool) == false
    }
    let description = str(composer, kAXDescriptionAttribute as String), caret = draftSelection(composer)
    if isDraftPlaceholder(value: value, description: description, labels: labels,
        caret: caret, sendUnavailable: sendUnavailable) { return true }
    // Attachments can enable Send while AXValue still reports the empty editor's
    // placeholder. Append may normalize it only when the editable range proves empty.
    // A literal draft spelling the same words has a real first character and survives.
    guard allowRangeEvidence, isDraftPlaceholder(value: value, description: description,
        labels: labels, caret: caret, sendUnavailable: false, emptyTextConfirmed: true) else { return false }
    return emptyDraftRangeConfirmed(composer)
}

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

// An icon button can expose a short title/value and put its actual action name in its
// description or help attribute. Keep these names together instead of letting displayText's
// first nonempty value hide the only meaningful label. AXValue is not an action name.
func buttonLabels(_ el: AXUIElement) -> [String] {
    [kAXTitleAttribute, kAXDescriptionAttribute, "AXHelp", "AXLabel"].compactMap {
        guard let value = str(el, $0), !value.isEmpty else { return nil }
        return value
    }
}

func normalizedButtonLabel(_ label: String) -> String {
    label.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ").lowercased()
}

// Chromium builds no AX tree until an assistive client asks; re-assert on every invocation.
func forceAccessibility(_ appEl: AXUIElement) {
    AXUIElementSetAttributeValue(appEl, "AXManualAccessibility" as CFString, kCFBooleanTrue)
    AXUIElementSetAttributeValue(appEl, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue)
}

// MARK: - preflight

if verb == "help" || verb == "--help" {
    print("""
    VizhiAxBridge <status|inspect|press|press-exact|voice|shortcut|write|append-target|append|send|focus|search> --app <bundle-id> [verb args]  (see source header)
    """)
    exit(0)
}

func shortcutFlags(_ value: String) -> CGEventFlags? {
    let names = value.split(separator: ",", omittingEmptySubsequences: false).map(String.init)
    let known: [String: CGEventFlags] = ["control": .maskControl, "shift": .maskShift,
                                        "option": .maskAlternate, "command": .maskCommand]
    guard Set(names).count == names.count,
          names.contains("control") || names.contains("command"),
          names.allSatisfy({ known[$0] != nil }) else { return nil }
    return names.reduce(CGEventFlags()) { $0.union(known[$1]!) }
}

// The target PID is fixed for BOTH events, even if foreground focus changes between them.
// Exposed as a pure dispatch seam so tests never post a real keyboard event.
func dispatchShortcut(targetPid: Int32, frontmostPid: Int32?, post: (Int32, Bool) -> Void) -> Bool {
    guard targetPid > 0, frontmostPid == targetPid else { return false }
    post(targetPid, true)
    post(targetPid, false)
    return true
}

// Explicit cross-application actions. This branch never runs on the status poll path.
func contextWindow(_ app: AXUIElement) -> AXUIElement? {
    guard let value = attr(app, kAXFocusedWindowAttribute as String) else { return nil }
    return (value as! AXUIElement)
}
func contextWindowKey(_ window: AXUIElement) -> String {
    let title = str(window, kAXTitleAttribute as String) ?? ""
    return SHA256.hash(data: Data((title + ":" + String(CFHash(window))).utf8)).map { String(format: "%02x", $0) }.joined()
}
func contextSource(_ app: NSRunningApplication, _ window: AXUIElement) -> String {
    let data = try! JSONSerialization.data(withJSONObject: ["bundle": app.bundleIdentifier ?? "", "pid": app.processIdentifier, "window": contextWindowKey(window)])
    return data.base64EncodedString()
}
func contextPasteboard() -> NSPasteboard {
    if let name = argValue("--test-pasteboard") {
        // Test seam is restricted to the disposable fixture; never opens the general clipboard.
        guard argValue("--app") == "com.vizhi.desktop.testfixture", name.hasPrefix("com.vizhi.fixture.") else { fail("invalid-pasteboard", 4) }
        return NSPasteboard(name: NSPasteboard.Name(name))
    }
    return .general
}
func captureDirectory() -> URL {
    if let path = argValue("--test-capture-dir") {
        guard argValue("--app") == "com.vizhi.desktop.testfixture",
              argValue("--test-pasteboard")?.hasPrefix("com.vizhi.fixture.") == true else { fail("invalid-capture-directory", 4) }
        return URL(fileURLWithPath: path, isDirectory: true)
    }
    return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".claude/claude-console/desktop-captures", isDirectory: true)
}
func savedPasteboard(_ board: NSPasteboard) -> [NSPasteboardItem]? {
    var total = 0, result: [NSPasteboardItem] = []
    for old in board.pasteboardItems ?? [] {
        let item = NSPasteboardItem()
        for type in old.types {
            guard let data = old.data(forType: type) else { return nil }
            total += data.count; if total > 16 * 1024 * 1024 { return nil }
            item.setData(data, forType: type)
        }
        result.append(item)
    }
    return result
}
func restorePasteboard(_ board: NSPasteboard, _ items: [NSPasteboardItem], revision: Int) {
    guard board.changeCount == revision else { return } // preserve a newer external copy
    board.clearContents(); if !items.isEmpty { board.writeObjects(items) }
}
func contextKey(_ pid: pid_t, _ code: CGKeyCode, flags: CGEventFlags = .maskCommand) -> Bool {
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
          let source = CGEventSource(stateID: .privateState),
          let down = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: true),
          let up = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: false) else { return false }
    down.flags = flags; up.flags = flags; down.postToPid(pid); up.postToPid(pid)
    withExtendedLifetime((source, down, up)) { RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.15)) }
    return true
}
// One whole-text operation for every composer and legacy reply-field caller. Readback
// establishes success; a partial setter or user edit never receives a second full copy.
func insertWholeText(_ text: String, editor: AXUIElement, pid: pid_t, original: String,
                     caret: CFRange, currentValue: () -> String, confirmed: (String) -> Bool) -> String {
    func unchanged() -> Bool {
        guard currentValue() == original, let now = draftSelection(editor) else { return false }
        return now.location == caret.location && now.length == 0
    }
    guard unchanged() else { fail("composer-selection-changed", 4) }
    draftAppendStarted = true
    AXUIElementSetAttributeValue(editor, kAXSelectedTextAttribute as CFString, text as CFString)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.20))
    if confirmed(currentValue()) { return "selectedText" }
    guard unchanged() else { fail("write-not-applied", 5) }
    let board = contextPasteboard(), revision = contextPasteboard().changeCount
    guard let saved = savedPasteboard(board), board.changeCount == revision else { fail("clipboard-changed", 4) }
    // Clipboard snapshotting can take time. Recheck the editor immediately before paste.
    guard unchanged(), board.changeCount == revision else { fail("clipboard-changed", 4) }
    board.clearContents()
    let writtenOK = board.setString(text, forType: .string), written = board.changeCount
    // No failing checks between writing and restoration: preserve all clipboard formats,
    // and do not restore over a newer copy made by the user or another application.
    let posted = writtenOK && contextKey(pid, 9)
    let deadline = Date().addingTimeInterval(1)
    while posted && !confirmed(str(editor, kAXValueAttribute as String) ?? "") && Date() < deadline {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.03))
    }
    restorePasteboard(board, saved, revision: written)
    guard posted, confirmed(currentValue()) else { fail("write-not-applied", 5) }
    return "paste"
}

func contextFocused(_ app: AXUIElement, _ window: AXUIElement) -> AXUIElement? {
    guard let value = attr(app, kAXFocusedUIElementAttribute as String) else { return nil }
    let element = value as! AXUIElement
    var current = element
    for _ in 0..<45 {
        if CFEqual(current, window) { return element }
        guard let parent = attr(current, kAXParentAttribute as String) else { return nil }
        current = parent as! AXUIElement
    }
    return nil
}

if verb.hasPrefix("context-") && verb != "context-copy" {
    guard AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": false] as CFDictionary) else { fail("not-trusted", 2) }
    let action = String(verb.dropFirst("context-".count))
    guard ["selection", "clipboard", "screenshot", "return", "paste"].contains(action) else { fail("unsupported", 4) }
    if action == "return" || action == "paste" {
        guard let encoded = argValue("--source"), let data = Data(base64Encoded: encoded),
              let source = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let pid = source["pid"] as? Int32, let bundle = source["bundle"] as? String,
              let expectedWindow = source["window"] as? String,
              let sourceApp = NSRunningApplication(processIdentifier: pid), sourceApp.bundleIdentifier == bundle else { fail("source-closed", 4) }
        let sourceAX = AXUIElementCreateApplication(pid)
        forceAccessibility(sourceAX)
        if action == "return" {
            let windows = (attr(sourceAX, kAXWindowsAttribute as String) as? [AXUIElement]) ?? []
            let matches = windows.filter { contextWindowKey($0) == expectedWindow }
            guard matches.count == 1 else { fail("source-closed", 4) }
            guard sourceApp.activate() else { fail("source-closed", 4) }
            AXUIElementPerformAction(matches[0], kAXRaiseAction as CFString)
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.2))
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
                  let window = contextWindow(sourceAX), CFEqual(window, matches[0]) else { fail("source-changed", 4) }
            emit([:], code: 0)
        }
        // Paste is an explicit request into the empty field the user has now selected in the
        // original application. Pin that window/editor throughout; a new Reply window is allowed.
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
              let window = contextWindow(sourceAX), let editor = contextFocused(sourceAX, window),
              ["AXTextArea", "AXTextField"].contains(str(editor, kAXRoleAttribute as String) ?? ""),
              str(editor, kAXSubroleAttribute as String) != "AXSecureTextField",
              let value = str(editor, kAXValueAttribute as String), let text = argValue("--text"), !text.isEmpty else { fail("choose-reply-field", 4) }
        guard value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { fail("draft-exists", 4) }
        let windowKey = contextWindowKey(window)
        func stillHere() -> Bool {
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid,
                  let current = contextWindow(sourceAX), contextWindowKey(current) == windowKey,
                  let focused = contextFocused(sourceAX, current) else { return false }
            return CFEqual(focused, editor)
        }
        guard text.utf16.count <= 50000 else { fail("text-too-long", 4) }
        guard let caret = draftSelection(editor), caret.length == 0 else {
            fail("composer-selection-changed", 4)
        }
        func currentReply() -> String {
            guard stillHere(), let current = str(editor, kAXValueAttribute as String) else { fail("source-changed", 4) }
            return current
        }
        _ = insertWholeText(text, editor: editor, pid: pid, original: value, caret: caret,
            currentValue: currentReply, confirmed: { comparableDraft($0) == comparableDraft(text) })
        emit([:], code: 0)
    }
    guard let front = NSWorkspace.shared.frontmostApplication, let bundle = front.bundleIdentifier,
          (bundleId == "@frontmost" || bundleId == bundle) else { fail("app-not-frontmost", 4) }
    let ownApp = argValue("--chat-app") ?? "com.openai.codex"
    if action == "selection" && bundle == ownApp { fail("choose-source-app", 4) }
    let sourceAX = AXUIElementCreateApplication(front.processIdentifier)
    forceAccessibility(sourceAX)
    guard let window = contextWindow(sourceAX) else { fail("source-changed", 4) }
    let windowKey = contextWindowKey(window)
    func stillSource() -> Bool {
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == front.processIdentifier,
              let current = contextWindow(sourceAX) else { return false }
        return contextWindowKey(current) == windowKey
    }
    var result: [String: Any] = ["appName": front.localizedName ?? "Source app"]
    if bundle != ownApp { result["source"] = contextSource(front, window) }
    if action == "screenshot" {
        guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else { fail("screen-permission", 4) }
        let directory = captureDirectory()
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let image = directory.appendingPathComponent("Vizhi-\(UUID().uuidString).png")
            let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
            process.arguments = ["-i", "-x", image.path]
            process.standardOutput = FileHandle.nullDevice; process.standardError = FileHandle.nullDevice
            try process.run(); process.waitUntilExit()
            guard process.terminationStatus == 0, let size = try? image.resourceValues(forKeys: [.fileSizeKey]).fileSize, size > 0 else { try? FileManager.default.removeItem(at: image); fail("cancelled", 4) }
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: image.path)
            result["image"] = image.path; emit(result, code: 0)
        } catch { fail("capture-failed", 4) }
    }
    let board = contextPasteboard()
    var selected: String?
    if action == "clipboard" {
        guard board.types?.isEmpty == false else { fail("clipboard-empty", 4) }
        let revision = board.changeCount
        if let urls = board.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            guard urls.count <= 8, urls.allSatisfy({ $0.isFileURL }), stillSource(), board.changeCount == revision else { fail("clipboard-changed", 4) }
            result["files"] = urls.map { $0.path }; emit(result, code: 0)
        }
        // File URLs take precedence over a Finder filename string; image data takes
        // precedence over an application's textual clipboard description.
        if let type = [NSPasteboard.PasteboardType.png, .tiff].first(where: { board.types?.contains($0) == true }) {
            guard let data = board.data(forType: type), data.count <= 50 * 1024 * 1024,
                  let source = CGImageSourceCreateWithData(data as CFData, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
                  let width = properties[kCGImagePropertyPixelWidth] as? Int, let height = properties[kCGImagePropertyPixelHeight] as? Int,
                  width > 0, height > 0, width <= 16384, height <= 16384, width * height <= 40_000_000,
                  stillSource(), board.changeCount == revision else { fail("clipboard-image-invalid", 4) }
            let converted = type == .png ? data : NSBitmapImageRep(data: data)?.representation(using: .png, properties: [:])
            guard let png = converted, png.count <= 50 * 1024 * 1024 else { fail("clipboard-image-invalid", 4) }
            let directory = captureDirectory()
            let hash = SHA256.hash(data: png).map { String(format: "%02x", $0) }.joined()
            let path = directory.appendingPathComponent("Clipboard-\(hash).png")
            do {
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
                try png.write(to: path, options: .atomic)
                try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: path.path)
                result["image"] = path.path; emit(result, code: 0)
            } catch { fail("capture-failed", 4) }
        }
        selected = board.string(forType: .string)
        guard selected != nil else { fail("clipboard-not-text", 4) }
    } else {
        if let focused = contextFocused(sourceAX, window) {
            guard str(focused, kAXSubroleAttribute as String) != "AXSecureTextField" else { fail("no-selection", 4) }
            selected = str(focused, kAXSelectedTextAttribute as String)
        }
        if selected?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty != false {
            // Explicit selection capture may use the source application's own Copy action.
            // A revision change proves it copied something now; stale clipboard data is never used.
            let before = board.changeCount
            guard let saved = savedPasteboard(board), board.changeCount == before, stillSource(), contextKey(front.processIdentifier, 8) else { fail("no-selection", 4) }
            let deadline = Date().addingTimeInterval(0.6)
            while board.changeCount == before && Date() < deadline { RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.03)) }
            let after = board.changeCount
            if after != before { selected = board.string(forType: .string); restorePasteboard(board, saved, revision: after) }
        }
    }
    guard stillSource() else { fail("source-changed", 4) }
    guard let text = selected, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { fail("no-selection", 4) }
    guard text.utf16.count <= 50000 else { fail("text-too-long", 4) }
    result["text"] = text; emit(result, code: 0)
}

guard AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": false] as CFDictionary) else {
    fail("not-trusted", 2)
}
guard let runningApp = NSRunningApplication.runningApplications(withBundleIdentifier: bundleId).first else {
    fail("app-not-running", 3)
}

// A confirmed app shortcut works independently of Chromium's button roles and AX tree.
// Handle it before constructing or enhancing the app's accessibility tree.
if verb == "shortcut" {
    guard let codeText = argValue("--key-code"), let keyCode = CGKeyCode(codeText), keyCode < 128,
          let flags = shortcutFlags(argValue("--modifiers") ?? "") else { fail("invalid-shortcut", 4) }
    guard let source = CGEventSource(stateID: .privateState),
          let down = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true),
          let up = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false) else {
        fail("shortcut-event-unavailable", 5)
    }
    down.flags = flags
    up.flags = flags
    guard dispatchShortcut(targetPid: runningApp.processIdentifier,
                           frontmostPid: NSWorkspace.shared.frontmostApplication?.processIdentifier,
                           post: { pid, isDown in (isDown ? down : up).postToPid(pid) }) else {
        fail("app-not-frontmost", 4)
    }
    // Posting is asynchronous. Exiting this one-shot helper immediately can drop BOTH
    // events before the target receives them (reproduced by the controlled fixture).
    // Keep the source/events alive and service the run loop briefly before teardown.
    // This is bounded well inside the caller's 2500ms timeout and is not an app ack.
    withExtendedLifetime((source, down, up)) {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.2))
    }
    // Event posting returns no app acknowledgement. Never infer active/ended from this result.
    emit(["requested": "shortcut"], code: 0)
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
    var labels: [String] = []
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
                          pressable: actionNames(el).contains(kAXPressAction as String), depth: depth,
                          labels: ["AXButton", "AXPopUpButton", "AXStaticText", "AXImage", "AXProgressIndicator",
                                   "AXBusyIndicator", "AXGroup"].contains(role) ? buttonLabels(el) : []))
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
func exactButtons(matching labels: [String], in nodes: [Node], roles: [String] = ["AXButton"]) -> [Node] {
    let names = Set(labels.map(normalizedButtonLabel).filter { !$0.isEmpty })
    return nodes.indices.compactMap { i in
        let node = nodes[i]
        // Full semantic labels only: case/whitespace are presentation differences; substrings,
        // arbitrary Close/X buttons, and text from descendants are not alternative selectors.
        guard roles.contains(node.role),
              node.labels.contains(where: { names.contains(normalizedButtonLabel($0)) }) else { return nil }
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

// Web accessibility may omit ordinary div wrappers. Match a contiguous run of sibling
// controls as well as a named group. Message/code text, headings, and the composer terminate
// the run; a code Copy cannot borrow a response action across that content boundary.
func replyActionRun(at index: Int, after heading: Int, in nodes: [Node]) -> [Node] {
    guard index > heading,
          let parent = (0..<index).reversed().first(where: { nodes[$0].depth < nodes[index].depth }),
          ["AXGroup", "AXWebArea"].contains(nodes[parent].role) else { return [] }
    var end = parent + 1
    while end < nodes.count && nodes[end].depth > nodes[parent].depth { end += 1 }
    let peers = ((parent + 1)..<end).filter { nodes[$0].depth == nodes[index].depth }
    guard let position = peers.firstIndex(of: index) else { return [] }
    func control(_ i: Int) -> Bool {
        peers[i] > heading && ["AXButton", "AXPopUpButton"].contains(nodes[peers[i]].role)
    }
    var first = position, last = position
    while first > 0 && control(first - 1) { first -= 1 }
    while last + 1 < peers.count && control(last + 1) { last += 1 }
    return Array(nodes[peers[first]..<(last + 1 < peers.count ? peers[last + 1] : end)])
}

func controlFrame(_ el: AXUIElement) -> CGRect? {
    guard let position = attr(el, kAXPositionAttribute as String),
          let size = attr(el, kAXSizeAttribute as String),
          CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
    let p = position as! AXValue, s = size as! AXValue
    guard AXValueGetType(p) == .cgPoint, AXValueGetType(s) == .cgSize else { return nil }
    var origin = CGPoint.zero, dimensions = CGSize.zero
    guard AXValueGetValue(p, .cgPoint, &origin), AXValueGetValue(s, .cgSize, &dimensions) else { return nil }
    let frame = CGRect(origin: origin, size: dimensions)
    guard [frame.minX, frame.minY, frame.width, frame.height].allSatisfy({ $0.isFinite }),
          frame.width > 0, frame.height > 0 else { return nil }
    return frame
}

// A fallback for independently wrapped footer controls. Require THREE distinct native
// icon buttons (Copy plus two response actions) aligned in one compact horizontal row.
// Coordinates establish their relationship only; activation still uses native AXPress.
func sameReplyControlRow(_ frames: [CGRect]) -> Bool {
    guard frames.count == 3,
          frames.allSatisfy({ [ $0.minX, $0.minY, $0.width, $0.height ].allSatisfy { $0.isFinite } &&
              $0.width >= 8 && $0.width <= 96 && $0.height >= 8 && $0.height <= 96 }) else { return false }
    let height = frames.map(\.height).min()!
    guard frames.map(\.height).max()! <= height * 1.5,
          frames.map(\.midY).max()! - frames.map(\.midY).min()! <= height * 0.25 else { return false }
    let ordered = frames.sorted { $0.minX < $1.minX }
    guard ordered[0].maxX <= ordered[1].minX, ordered[1].maxX <= ordered[2].minX,
          ordered[2].maxX - ordered[0].minX <= height * 8 else { return false }
    return true
}

func positionedReplyRow(_ candidate: Node, in nodes: [Node]) -> Bool {
    guard let copyFrame = controlFrame(candidate.el) else { return false }
    let actions = exactButtons(matching: argValues("--response-action"), in: nodes,
        roles: ["AXButton", "AXPopUpButton"]).filter { !CFEqual($0.el, candidate.el) }
    guard actions.count >= 2 else { return false }
    let known = Set(argValues("--response-action").map(normalizedButtonLabel))
    func semantics(_ node: Node) -> Set<String> { Set(node.labels.map(normalizedButtonLabel)).intersection(known) }
    for first in 0..<(actions.count - 1) {
        guard let a = controlFrame(actions[first].el) else { continue }
        for second in (first + 1)..<actions.count where !CFEqual(actions[first].el, actions[second].el) {
            guard semantics(actions[first]).isDisjoint(with: semantics(actions[second])) else { continue }
            if let b = controlFrame(actions[second].el), sameReplyControlRow([copyFrame, a, b]) { return true }
        }
    }
    return false
}

func replySpeakerLabel(at index: Int, in nodes: [Node]) -> String {
    let heading = nodes[index]
    guard heading.role == "AXHeading" else { return "" }
    var text = heading.text
    if text.isEmpty {
        var end = index + 1
        while end < nodes.count && nodes[end].depth > heading.depth { end += 1 }
        text = nodes[(index + 1)..<end].filter { $0.role == "AXStaticText" }.map(\.text).joined(separator: " ")
    }
    return normalizedButtonLabel(text)
}

// A diff/preview can expose an additional AXWebArea in the same window. Identify the
// unique conversation by its speaker headings, not by which area happens to have Copy.
// Nested areas own their own headings/controls. Keep their root as a content boundary
// in the parent so unrelated controls cannot become adjacent after filtering.
func replyConversationNodes(_ nodes: [Node], speakerLabels: Set<String>) -> [Node]? {
    var areas: [[Node]] = []
    var stack: [(area: Int, depth: Int)] = []
    for node in nodes {
        while let parent = stack.last, parent.depth >= node.depth { stack.removeLast() }
        if node.role == "AXWebArea" {
            if let parent = stack.last { areas[parent.area].append(node) }
            areas.append([node]); stack.append((areas.count - 1, node.depth))
        } else if let parent = stack.last {
            areas[parent.area].append(node)
        }
    }
    if areas.count == 1 { return areas[0] }
    let conversations = areas.filter { area in
        area.indices.contains { speakerLabels.contains(replySpeakerLabel(at: $0, in: area)) }
    }
    return conversations.count == 1 ? conversations[0] : nil
}

// Speaker headings and the response-only Copy control are adapter-owned semantics. Never
// use selected text, the last generic Copy button, or scrape message/reasoning text.
func replyTarget(_ windowNodes: [Node], copiedTarget: AXUIElement? = nil) -> (node: Node?, error: String) {
    let assistants = argValues("--assistant-heading").map(normalizedButtonLabel)
    let users = argValues("--user-heading").map(normalizedButtonLabel)
    let copies = argValues("--copy-response")
    guard !assistants.isEmpty, !users.isEmpty, !copies.isEmpty else { return (nil, "unsupported") }
    // Distinguish structural refusals from duplicate Copy candidates. These fixed
    // codes explain the failed guard without exposing any window or message content.
    guard windowNodes.contains(where: { $0.role == "AXWebArea" }) else { return (nil, "reply-web-area-missing") }
    // Copy targets the native response action, never an editor. Unrelated text areas
    // (or a read-only conversation without a composer) do not make that action ambiguous.
    // Keep text areas as action-row boundaries below; write/send retain their own guards.
    if windowNodes.contains(where: { $0.role == "AXSheet" ||
        ["AXApplicationDialog", "AXDialog"].contains(str($0.el, kAXSubroleAttribute as String) ?? "") }) {
        return (nil, "reply-dialog-open")
    }
    if !exactButtons(matching: argValues("--stop") + argValues("--voice-end"), in: windowNodes).isEmpty ||
        firstPressable(matching: argValues("--approve"), in: windowNodes) != nil { return (nil, "answer-not-ready") }
    if let marker = argValue("--conv-marker"), !marker.isEmpty {
        var selected = 0
        for (i, row) in windowNodes.enumerated() where row.pressable && !row.text.isEmpty {
            var end = i + 1
            while end < windowNodes.count && windowNodes[end].depth > row.depth { end += 1 }
            let descendants = windowNodes[(i + 1)..<end]
            guard descendants.contains(where: { $0.pressable && $0.text == marker }), conversationSelected(row.el) else { continue }
            selected += 1
            let state = conversationRowState(title: row.text, descendants: descendants,
                awaiting: argValues("--state-awaiting"), unread: [], running: argValues("--state-running"), baseline: nil)
            if state == "running" || state == "awaiting" { return (nil, "answer-not-ready") }
        }
        if selected > 1 { return (nil, "reply-selection-multiple") }
    }
    guard let nodes = replyConversationNodes(windowNodes, speakerLabels: Set(assistants + users)) else {
        return (nil, "reply-web-area-multiple")
    }
    var lastSpeaker: (index: Int, assistant: Bool)?
    for (i, heading) in nodes.enumerated() where heading.role == "AXHeading" {
        let label = replySpeakerLabel(at: i, in: nodes)
        if assistants.contains(label) { lastSpeaker = (i, true) }
        else if users.contains(label) { lastSpeaker = (i, false) }
    }
    guard let latest = lastSpeaker else { return (nil, "reply-unrecognized") }
    guard latest.assistant else { return (nil, "no-answer") }
    // No fallback to an earlier answer when the newest turn has no completed copy action.
    let tail = Array(nodes[(latest.index + 1)...])
    var matches = exactButtons(matching: copies, in: tail)
    // Some app versions expose the response-specific text only as a tooltip. A generic
    // Copy is accepted only in the same small action row as an adapter-owned sibling action.
    // A code-block Copy would have to climb past the message heading, and is rejected.
    for candidate in exactButtons(matching: argValues("--copy-button"), in: tail) {
        guard !matches.contains(where: { CFEqual($0.el, candidate.el) }),
              let index = nodes.firstIndex(where: { CFEqual($0.el, candidate.el) }) else { continue }
        let run = replyActionRun(at: index, after: latest.index, in: nodes)
        if exactButtons(matching: argValues("--copy-button"), in: run).count == 1 &&
            !exactButtons(matching: argValues("--response-action"), in: run,
                roles: ["AXButton", "AXPopUpButton"]).isEmpty {
            matches.append(candidate); continue
        }
        for parent in stride(from: index - 1, through: 0, by: -1) where nodes[parent].depth < candidate.depth {
            var end = parent + 1
            while end < nodes.count && nodes[end].depth > nodes[parent].depth { end += 1 }
            guard end > index else { continue }
            let scope = Array(nodes[parent..<end])
            if scope.contains(where: { ["AXHeading", "AXTextArea", "AXWebArea"].contains($0.role) }) { break }
            // The heading can be outside the Markdown container. Do not let a code Copy
            // climb through response text to borrow More actions from a different footer.
            let controlLabels = Set((argValues("--copy-button") + argValues("--copy-completed") +
                argValues("--response-action")).map(normalizedButtonLabel))
            if scope.contains(where: { $0.role == "AXStaticText" && !$0.text.isEmpty &&
                !controlLabels.contains(normalizedButtonLabel($0.text)) }) { break }
            guard nodes[parent].role == "AXGroup",
                  exactButtons(matching: argValues("--copy-button"), in: scope).count == 1,
                  !exactButtons(matching: argValues("--response-action"), in: scope,
                    roles: ["AXButton", "AXPopUpButton"]).isEmpty else { continue }
            matches.append(candidate); break
        }
        if !matches.contains(where: { CFEqual($0.el, candidate.el) }), positionedReplyRow(candidate, in: tail) {
            matches.append(candidate)
        }
    }
    // The same pressed button changes its accessible label to Copied for two seconds.
    // This is acknowledgement only, never an alternative target for a fresh request.
    if let copiedTarget, let acknowledged = exactButtons(matching: argValues("--copy-completed"), in: tail)
        .first(where: { CFEqual($0.el, copiedTarget) }), !matches.contains(where: { CFEqual($0.el, copiedTarget) }) {
        matches.append(acknowledged)
    }
    guard matches.count <= 1 else { return (nil, "reply-copy-multiple") }
    guard let target = matches.first else {
        // Report which existing selector condition failed, without emitting labels,
        // conversation text, element identities or an accessibility-tree dump.
        let names = Set((copies + argValues("--copy-button")).map(normalizedButtonLabel))
        func namedCopy(_ node: Node) -> Bool {
            (node.pressable || ["AXButton", "AXPopUpButton"].contains(node.role)) &&
                node.labels.contains { names.contains(normalizedButtonLabel($0)) }
        }
        let named = tail.filter(namedCopy)
        if named.isEmpty {
            return (nil, nodes.contains(where: namedCopy) ? "reply-copy-outside-latest" : "reply-copy-not-found")
        }
        if !named.contains(where: { $0.role == "AXButton" }) { return (nil, "reply-copy-wrong-role") }
        if exactButtons(matching: copies + argValues("--copy-button"), in: tail).isEmpty {
            return (nil, "reply-copy-nested-control")
        }
        if exactButtons(matching: argValues("--response-action"), in: tail,
            roles: ["AXButton", "AXPopUpButton"]).isEmpty { return (nil, "reply-action-not-found") }
        return (nil, "reply-action-row-unrecognized")
    }
    guard target.pressable, (attr(target.el, kAXEnabledAttribute as String) as? Bool) == true else {
        return (nil, "answer-not-ready")
    }
    return (target, "")
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

// Chromium maps aria-current="page" on the app's sidebar button to AXARIACurrent,
// independently of AXSelected. An explicit non-current value must not borrow selection
// from a parent. Legacy AXSelected is only a fallback for apps without aria-current.
func currentConversationFlag(ariaCurrent: String?, legacySelected: Bool) -> Bool {
    guard let value = ariaCurrent else { return legacySelected }
    return ["page", "true"].contains(normalizedButtonLabel(value))
}

func conversationSelected(_ element: AXUIElement) -> Bool {
    if let current = str(element, "AXARIACurrent") {
        return currentConversationFlag(ariaCurrent: current, legacySelected: false)
    }
    var current = element
    for depth in 0...2 {
        if (attr(current, "AXSelected") as? Bool) == true { return true }
        if depth == 2 { break }
        guard let parent = attr(current, kAXParentAttribute as String) else { break }
        current = parent as! AXUIElement
    }
    return false
}

// Read only a verified sidebar row's descendants, never message text or the row's own
// title. Status can be a text badge, a named image, or an accessibility description.
// Exact normalized matches avoid treating a chat titled "Thinking about travel" as busy.
func conversationRowState(title: String, descendants: ArraySlice<Node>, awaiting: [String],
                          unread: [String], running: [String], baseline: Int?) -> String {
    let titleLabel = normalizedButtonLabel(title)
    let statusRoles = ["AXStaticText", "AXImage", "AXProgressIndicator", "AXBusyIndicator", "AXGroup"]
    let labels = Set(descendants.filter { statusRoles.contains($0.role) }.flatMap {
        ([$0.text] + $0.labels).map(normalizedButtonLabel).filter { !$0.isEmpty && $0 != titleLabel }
    })
    func matches(_ candidates: [String]) -> Bool {
        candidates.contains { !normalizedButtonLabel($0).isEmpty && labels.contains(normalizedButtonLabel($0)) }
    }
    if matches(awaiting) { return "awaiting" }
    if matches(unread) { return "unread" }
    if matches(running) { return "running" }
    // Retain the verified legacy unnamed-spinner fallback, scoped to this row and mode.
    let images = descendants.filter { $0.role == "AXImage" && $0.text.isEmpty }.count
    return conversationState(state: "idle", images: images, baseline: baseline)
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

// Search never uses AXTextArea (the message composer), arbitrary pressable rows, or Return.
// Unknown app layouts are deliberately unsupported. All discovery stays inside one dialog.
struct SearchSurface {
    let field: Node
    let nodes: [Node]
    let query: String
    let token: String
}

// Per invocation only. A successful open checks mode before a modal may hide its selector.
// Subsequent calls carry the issued field token (or origin while waiting for the field).
var verifiedSearchOrigin: String?

func searchOrigin() -> String? {
    guard operationWindows.count == 1, sameOperationWindow() else { return nil }
    let window = operationWindows[0]
    guard let title = str(window, kAXTitleAttribute as String), !title.isEmpty else { return nil }
    let allWindows = (attr(appEl, kAXWindowsAttribute as String) as? [AXUIElement]) ?? []
    guard allWindows.filter({ str($0, kAXTitleAttribute as String) == title }).count == 1 else { return nil }
    let identity = [String(runningApp.processIdentifier), title, String(CFHash(window)),
                    argValue("--mode-prefix") ?? "", argValue("--expect-mode") ?? ""]
    guard let data = try? JSONSerialization.data(withJSONObject: identity) else { return nil }
    return SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
}

func failSearch(_ error: String, _ code: Int32 = 4) -> Never {
    // Issue a recovery origin only AFTER mode was verified. An unreadable/wrong mode must
    // never create the evidence a later probe would use to accept a hidden selector.
    let invalidated = ["mode-unavailable", "mode-changed", "search-target-changed", "app-not-frontmost", "no-surface"].contains(error)
    let origin = !invalidated && verifiedSearchOrigin == searchOrigin() ? verifiedSearchOrigin ?? "" : ""
    emit(["ok": false, "error": error, "origin": origin], code: code)
}

func normalizedSearchLabel(_ label: String) -> String {
    normalizedButtonLabel(label).trimmingCharacters(in: CharacterSet(charactersIn: ".… "))
}

func isSearchField(_ node: Node, names: [String]) -> Bool {
    guard ["AXTextField", "AXComboBox", "AXSearchField"].contains(node.role) else { return false }
    if node.role == "AXSearchField" || str(node.el, "AXSubrole") == "AXSearchField" { return true }
    let expected = Set(names.map(normalizedSearchLabel))
    let labels = buttonLabels(node.el) + [str(node.el, "AXPlaceholderValue") ?? ""]
    return labels.contains { !normalizedSearchLabel($0).isEmpty && expected.contains(normalizedSearchLabel($0)) }
}

func searchContainer(_ index: Int, nodes: [Node]) -> Int? {
    var depth = nodes[index].depth
    for i in (0..<index).reversed() where nodes[i].depth < depth {
        depth = nodes[i].depth
        if ["AXDialog", "AXSheet"].contains(nodes[i].role)
            || ["AXDialog", "AXApplicationDialog", "AXLandmarkSearch"].contains(str(nodes[i].el, "AXSubrole") ?? "") { return i }
        // A native sheet can contain a web area. Reaching that web area is not the end of
        // the ancestor walk; the selected window remains the hard boundary.
        if nodes[i].role == "AXWindow" { break }
    }
    return nil
}

func reportedSearchModes(_ nodes: [Node], prefix: String) -> Set<String> {
    guard !prefix.isEmpty else { return [] }
    // The mode label can belong to a popup or its static child. Duplicate descriptions of
    // the same mode are harmless; conflicting modes in one window are not.
    var values = Set<String>()
    for i in nodes.indices {
        let control = ["AXButton", "AXPopUpButton", "AXMenuButton"].contains(nodes[i].role)
        let actionableGroup = nodes[i].role == "AXGroup" && nodes[i].pressable
        guard control || actionableGroup else { continue }
        // Read all semantic attributes, including descriptions on non-button roles. A
        // generic popup title must not hide its mode label. Do not read arbitrary content.
        var labels = [nodes[i].text] + buttonLabels(nodes[i].el)
        var j = i + 1
        while control && j < nodes.count && nodes[j].depth > nodes[i].depth {
            if nodes[j].role == "AXStaticText" { labels.append(nodes[j].text) }
            j += 1
        }
        for label in labels where label.hasPrefix(prefix) { values.insert(String(label.dropFirst(prefix.count))) }
    }
    return values
}

func searchModeError(_ modes: Set<String>, expected: String, pinned: Bool) -> String? {
    guard !expected.isEmpty else { return "mode-unavailable" }
    if modes.isEmpty { return pinned ? nil : "mode-unavailable" }
    // Conflicting reports are a mismatch, never an absent selector that a pin can excuse.
    return modes == Set([expected]) ? nil : "mode-changed"
}

func checkSearchMode(_ nodes: [Node], pinned: Bool) {
    let modes = reportedSearchModes(nodes, prefix: argValue("--mode-prefix") ?? "")
    if let error = searchModeError(modes, expected: argValue("--expect-mode") ?? "", pinned: pinned) { failSearch(error) }
    verifiedSearchOrigin = searchOrigin()
}

func inspectSearch(_ nodes: [Node]) -> (surface: SearchSurface?, error: String) {
    let fields = nodes.indices.filter { isSearchField(nodes[$0], names: argValues("--search-field")) }
    guard !fields.isEmpty else { return (nil, "search-field-missing") }
    guard fields.count == 1, let index = fields.first else { return (nil, "search-field-ambiguous") }
    guard (attr(nodes[index].el, kAXEnabledAttribute as String) as? Bool) == true else { return (nil, "search-field-disabled") }
    guard let query = str(nodes[index].el, kAXValueAttribute as String) else { return (nil, "search-value-unavailable") }
    guard let start = searchContainer(index, nodes: nodes) else { return (nil, "search-container-missing") }
    guard let origin = searchOrigin() else { return (nil, "search-target-changed") }
    let end = nodes[(start + 1)...].firstIndex { $0.depth <= nodes[start].depth } ?? nodes.count
    let identity = origin + ":" + String(CFHash(nodes[index].el))
    let token = SHA256.hash(data: Data(identity.utf8)).map { String(format: "%02x", $0) }.joined()
    return (SearchSurface(field: nodes[index], nodes: Array(nodes[(start + 1)..<end]), query: query, token: token), "")
}

func searchSurface(_ nodes: [Node]) -> SearchSurface? { inspectSearch(nodes).surface }

func searchResultID(_ node: Node) -> String? {
    guard node.role == "AXLink", node.pressable, !node.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
          let raw = attr(node.el, kAXURLAttribute as String),
          let url = (raw as? URL) ?? (raw as? String).flatMap({ URL(string: $0) }),
          ["https", nil].contains(url.scheme),
          (url.host.map { argValues("--result-host").contains($0.lowercased()) } ?? url.absoluteString.hasPrefix("/")),
          url.query == nil, url.fragment == nil,
          argValues("--result-path").contains(where: { prefix in
              url.path.hasPrefix(prefix) && !url.path.dropFirst(prefix.count).isEmpty
                  && !url.path.dropFirst(prefix.count).contains("/")
          }) else { return nil }
    return url.absoluteString
}

func searchResults(_ surface: SearchSurface) -> [(node: Node, id: String)] {
    guard !surface.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return [] }
    let matches = surface.nodes.compactMap { node -> (node: Node, id: String)? in
        searchResultID(node).map { (node, $0) }
    }
    let groups = Dictionary(grouping: matches, by: { $0.id })
    return matches.filter { groups[$0.id]?.count == 1 }.prefix(100).map { $0 }
}

func checkedSearch(queryOverride: String? = nil) -> SearchSurface {
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier else { failSearch("app-not-frontmost") }
    guard sameOperationWindow() else { failSearch("search-target-changed") }
    if let expectedOrigin = argValue("--origin"), searchOrigin() != expectedOrigin { failSearch("search-target-changed") }
    let scan = scanWindows()
    guard scan.webArea else { failSearch("no-surface") }
    let inspection = inspectSearch(scan.nodes)
    // Tokens are emitted only after a mode check and include mode + process + window +
    // field identity. A modal can hide its selector without invalidating that same target.
    // A waiting probe has no field yet and uses only its previously verified origin.
    let pinned = searchOrigin().map { origin in
        verifiedSearchOrigin == origin || (argValue("--action") == "probe" && argValue("--origin") == origin)
            || (inspection.surface.map { !$0.token.isEmpty && argValue("--target") == $0.token } ?? false)
    } ?? false
    checkSearchMode(scan.nodes, pinned: pinned)
    guard let surface = inspection.surface else { failSearch(inspection.error) }
    if let target = argValue("--target"), surface.token != target { failSearch("search-target-changed") }
    if let query = queryOverride ?? argValue("--query"), surface.query != query { failSearch("query-changed") }
    return surface
}

func emitSearch(_ surface: SearchSurface) -> Never {
    emit(["target": surface.token, "origin": searchOrigin() ?? "", "query": surface.query,
          "results": searchResults(surface).map { ["id": $0.id, "title": $0.node.text] }], code: 0)
}

// Include a selected conversation when the app exposes one, so a reused editor cannot
// silently receive a brief after sidebar navigation. No conversation text is logged.
func selectedDraftConversation(_ nodes: [Node]) -> String {
    guard let marker = argValue("--conv-marker"), !marker.isEmpty else { return "" }
    var selected: [String] = []
    for (i, node) in nodes.enumerated() where node.pressable && !node.text.isEmpty {
        var j = i + 1, hasMarker = false
        while j < nodes.count && nodes[j].depth > node.depth {
            if nodes[j].pressable && nodes[j].text == marker { hasMarker = true }
            j += 1
        }
        guard hasMarker else { continue }
        if conversationSelected(node.el) { selected.append(node.text + ":" + String(CFHash(node.el))) }
    }
    if selected.count > 1 { fail("composer-target-changed", 6) }
    return selected.first ?? ""
}

func draftTarget(_ nodes: [Node]) -> String {
    if let error = searchModeError(reportedSearchModes(nodes, prefix: argValue("--mode-prefix") ?? ""),
        expected: argValue("--expect-mode") ?? "", pinned: false) { fail(error, 4) }
    let editors = nodes.filter { $0.role == "AXTextArea" }
    guard editors.count == 1, let origin = searchOrigin() else { fail("composer-target-changed", 6) }
    return SHA256.hash(data: Data((origin + ":" + String(CFHash(editors[0].el)) + ":" + selectedDraftConversation(nodes)).utf8))
        .map { String(format: "%02x", $0) }.joined()
}

func checkPreparedDraft(_ nodes: [Node]) {
    if let target = argValue("--expect-target"), draftTarget(nodes) != target {
        fail("composer-target-changed", 6)
    }
}

func checkAppendReady(_ nodes: [Node]) {
    let blocked = argValues("--stop") + argValues("--approve") + argValues("--voice-end")
    guard !nodes.contains(where: { $0.role == "AXSheet" || $0.role == "AXDialog"
        || str($0.el, kAXSubroleAttribute as String) == "AXDialog" }),
          !exactButtons(matching: blocked, in: nodes).contains(where: {
              (attr($0.el, kAXEnabledAttribute as String) as? Bool) != false
          }) else { fail("composer-unavailable", 4) }
}

// Only the summary row opens the review panel. Counts may follow its exact name.
// Do not accept arbitrary prefix matches (e.g. Changes settings or a file disclosure).
func panelOpeners(_ nodes: [Node], labels: [String], enabledOnly: Bool = true) -> [Node] {
    let names = nodes.flatMap { node in
        node.labels.filter { name in
            labels.contains { label in
                // The app uses locale-formatted integers in separate spans. AX can join
                // those spans without spaces: Changes+1,234-56, or +1,23,456 in en-IN.
                // Accept only signed integer counts after the exact destination name.
                let integer = "[0-9]+(?:[,.٬ ][0-9]{2,3})*"
                let pattern = "^" + NSRegularExpression.escapedPattern(for: label) + "(?: *[+−-] *" + integer + "){0,2}$"
                return collapse(name).range(of: pattern, options: .regularExpression) != nil
            }
        }
    }
    return exactButtons(matching: names, in: nodes).filter {
        !enabledOnly || ($0.pressable && (attr($0.el, kAXEnabledAttribute as String) as? Bool) == true)
    }
}

// A browser preview may expose another web area with arbitrary website controls.
// Review belongs to the unique app web area containing the mode control; child web
// areas own their own controls and cannot advertise Review on behalf of the app.
func panelNodes(_ nodes: [Node], modeLabel: String) -> [Node]? {
    guard !modeLabel.isEmpty else { return nil }
    var areas: [[Node]] = []
    var stack: [(area: Int, depth: Int)] = []
    for node in nodes {
        while let parent = stack.last, parent.depth >= node.depth { stack.removeLast() }
        if node.role == "AXWebArea" {
            if let parent = stack.last { areas[parent.area].append(node) }
            areas.append([node]); stack.append((areas.count - 1, node.depth))
        } else if let parent = stack.last {
            areas[parent.area].append(node)
        }
    }
    let matches = areas.filter { $0.contains { $0.text == modeLabel } }
    return matches.count == 1 ? matches[0] : nil
}

func panelObstructed(_ nodes: [Node]) -> Bool {
    nodes.contains { $0.role == "AXSheet" || $0.role == "AXDialog" || $0.role == "AXMenu" ||
        ["AXApplicationDialog", "AXDialog"].contains(str($0.el, kAXSubroleAttribute as String) ?? "") }
}

func panelRouteAvailable(_ nodes: [Node], modeLabel: String, openLabels: [String], visibleLabels: [String]) -> Bool {
    guard !panelObstructed(nodes), let content = panelNodes(nodes, modeLabel: modeLabel) else { return false }
    let panels = exactButtons(matching: visibleLabels, in: content)
    if panels.count != 0 { return panels.count == 1 }
    return panelOpeners(content, labels: openLabels).count == 1
}

switch verb {
case "open-panel":
    guard let expectedMode = argValue("--expect-mode"), !expectedMode.isEmpty,
          let prefix = argValue("--mode-prefix"), !prefix.isEmpty,
          !argValues("--panel-open").isEmpty, !argValues("--panel-visible").isEmpty else { fail("panel-arguments", 4) }
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier,
          waitForWebContent(seconds: 1), sameOperationWindow() else { fail("panel-unavailable", 4) }
    let windowKey = contextWindowKey(operationWindows[0])
    let initial = scanWindows()
    let conversation = selectedDraftConversation(initial.nodes)
    func checkedPanelNodes() -> [Node] {
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier else { fail("panel-foreground-changed", 6) }
        guard sameOperationWindow(), contextWindowKey(operationWindows[0]) == windowKey else { fail("panel-window-changed", 6) }
        let scan = scanWindows()
        guard scan.webArea else { fail("panel-surface-missing", 6) }
        guard selectedDraftConversation(scan.nodes) == conversation else { fail("panel-conversation-changed", 6) }
        let modes = scan.nodes.filter { $0.text.hasPrefix(prefix) }
        guard modes.count == 1, modes[0].text == prefix + expectedMode else { fail("mode-changed", 6) }
        guard !panelObstructed(scan.nodes) else { fail("panel-obstructed", 4) }
        guard let content = panelNodes(scan.nodes, modeLabel: prefix + expectedMode) else { fail("panel-not-available", 4) }
        return content
    }
    func panelVisible(_ nodes: [Node]) -> Bool {
        let matches = exactButtons(matching: argValues("--panel-visible"), in: nodes)
        if matches.count > 1 { fail("panel-ambiguous", 4) }
        return matches.count == 1
    }
    let nodes = checkedPanelNodes()
    if panelVisible(nodes) { emit(["opened": true, "alreadyOpen": true], code: 0) }
    let candidates = panelOpeners(nodes, labels: argValues("--panel-open"))
    guard candidates.count <= 1 else { fail("panel-opener-multiple", 4) }
    // A Codex conversation may have no Git review capability. Absence is not a clean
    // working tree, and must not trigger a guessed shortcut, toggle, or text entry.
    guard candidates.count == 1 else { fail("panel-not-available", 4) }
    let fresh = checkedPanelNodes()
    if panelVisible(fresh) { emit(["opened": true, "alreadyOpen": true], code: 0) }
    let confirmed = panelOpeners(fresh, labels: argValues("--panel-open"))
    guard confirmed.count == 1, CFEqual(candidates[0].el, confirmed[0].el) else { fail("panel-target-changed", 6) }
    guard AXUIElementPerformAction(confirmed[0].el, kAXPressAction as CFString) == .success else { fail("panel-press-failed", 5) }
    let deadline = Date().addingTimeInterval(1.2)
    repeat {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.05))
        if panelVisible(checkedPanelNodes()) { emit(["opened": true, "alreadyOpen": false, "method": "button"], code: 0) }
    } while Date() < deadline
    fail("panel-unconfirmed", 5)

case "append-target":
    guard waitForWebContent(seconds: 2) else { fail("surface-unavailable", 5) }
    let scan = scanWindows()
    guard scan.webArea else { fail("surface-unavailable", 5) }
    checkAppendReady(scan.nodes)
    let target = draftTarget(scan.nodes)
    guard let composer = scan.nodes.first(where: { $0.role == "AXTextArea" }),
          let raw = str(composer.el, kAXValueAttribute as String) else { fail("composer-value-unavailable", 4) }
    let value = placeholderDraft(composer.el, value: raw, allowRangeEvidence: true) ? "" : raw
    guard value.utf16.count <= 50000 else { fail("text-too-long", 4) }
    let sendable = exactButtons(matching: argValues("--composer-send-label"), in: scan.nodes).contains {
        (attr($0.el, kAXEnabledAttribute as String) as? Bool) == true
    }
    emit(["target": target, "fingerprint": draftFingerprint(value),
          "hasContent": !comparableDraft(value).isEmpty || sendable], code: 0)

case "write", "append":
    let appending = verb == "append"
    guard let supplied = argValue("--text"), !comparableDraft(supplied).isEmpty else { fail("empty-text", 4) }
    let requestedBefore = argValue("--expect-draft")
    if appending {
        guard let before = requestedBefore, before.count == 64, before.allSatisfy({ $0.isHexDigit }),
              argValue("--expect-target") != nil, argValue("--send-label") == nil else { fail("invalid-append", 4) }
    }
    let text = appending ? comparableDraft(supplied) : supplied
    let expectedText = comparableDraft(text)
    guard text.utf16.count <= 50000 else { fail("text-too-long", 4) }
    guard waitForWebContent(seconds: 2) else { fail("surface-unavailable", 5) }
    let initial = scanWindows()
    guard initial.webArea else { fail("surface-unavailable", 5) }
    checkPreparedDraft(initial.nodes); checkAppendReady(initial.nodes)
    let composers = initial.nodes.filter { $0.role == "AXTextArea" }
    guard composers.count == 1, sameOperationWindow() else { fail("no-unique-composer", 4) }
    let composer = composers[0]
    let windowTitle = str(operationWindows[0], kAXTitleAttribute as String)
    let conversation = selectedDraftConversation(initial.nodes)
    let modes = reportedSearchModes(initial.nodes, prefix: argValue("--mode-prefix") ?? "")
    func currentValue(focused: Bool = false) -> String {
        let scan = scanWindows()
        guard scan.webArea, sameOperationWindow(),
              str(operationWindows[0], kAXTitleAttribute as String) == windowTitle,
              selectedDraftConversation(scan.nodes) == conversation,
              reportedSearchModes(scan.nodes, prefix: argValue("--mode-prefix") ?? "") == modes,
              scan.nodes.filter({ $0.role == "AXTextArea" }).count == 1,
              scan.nodes.contains(where: { $0.role == "AXTextArea" && CFEqual($0.el, composer.el) }),
              let raw = str(composer.el, kAXValueAttribute as String) else { fail("composer-target-changed", 6) }
        checkPreparedDraft(scan.nodes); checkAppendReady(scan.nodes)
        if focused {
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier,
                  let active = attr(appEl, kAXFocusedUIElementAttribute as String), CFEqual(active, composer.el)
                else { fail("composer-focus-changed", 6) }
        }
        return placeholderDraft(composer.el, value: raw, allowRangeEvidence: true) ? "" : raw
    }
    let original = currentValue()
    let before = appending ? requestedBefore! : draftFingerprint(original)
    let possiblePlaceholder = argValue("--composer-send-label")?.isEmpty == false &&
        matchesDraftPlaceholder(value: original, description: str(composer.el, kAXDescriptionAttribute as String),
                                labels: argValues("--draft-placeholder"))
    if hasFlag("--accept-existing") && argValue("--send-label") == nil {
        let alreadyInserted = appending
            ? (appendedDraftMatches(original, before: before, addition: text)
                || appendedPlaceholderMatches(original, before: before, addition: text,
                    description: str(composer.el, kAXDescriptionAttribute as String), labels: argValues("--draft-placeholder")))
            : comparableDraft(original) == expectedText
        if alreadyInserted { emit(["method": "existing", "sent": false], code: 0) }
    }
    if !appending && !comparableDraft(original).isEmpty && !possiblePlaceholder { fail("draft-exists", 4) }
    guard draftFingerprint(original) == before else { fail("draft-changed", 4) }
    guard original.utf16.count + text.utf16.count + 2 <= 50000 else { fail("text-too-long", 4) }
    if NSWorkspace.shared.frontmostApplication?.processIdentifier != runningApp.processIdentifier {
        guard runningApp.activate() else { fail("app-not-frontmost", 4) }
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.15))
    }
    guard currentValue() == original else { fail("draft-changed", 4) }
    AXUIElementSetAttributeValue(composer.el, kAXFocusedAttribute as CFString, kCFBooleanTrue)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.10))
    guard currentValue(focused: true) == original else { fail("draft-changed", 4) }
    // Generated CSS hints can appear in AXValue AND AXStringForRange even though none of
    // their characters are editable. With an attachment, Send is enabled too. Navigate to
    // the actual editing boundary instead of assigning an offset into that visual hint.
    // Literal text spelling the same words moves to a nonzero end and is preserved normally.
    var placeholderAtEnd = false
    if possiblePlaceholder {
        guard contextKey(runningApp.processIdentifier, 125), currentValue(focused: true) == original
            else { fail("composer-selection-changed", 4) }
        let caret = draftSelection(composer.el)
        placeholderAtEnd = caret?.location == 0 && caret?.length == 0
    } else {
        // Move only the caret. Never select all or replace the composer's existing value.
        var end = CFRange(location: original.utf16.count, length: 0)
        if let range = AXValueCreate(.cfRange, &end) {
            AXUIElementSetAttributeValue(composer.el, kAXSelectedTextRangeAttribute as CFString, range)
        }
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.05))
    }
    func endCaret() -> CFRange? {
        guard let caret = draftSelection(composer.el), caret.length == 0,
              caret.location <= original.utf16.count else { return nil }
        if placeholderAtEnd && caret.location == 0 { return caret }
        if comparableDraft((original as NSString).substring(from: caret.location)).isEmpty { return caret }
        var prefixRange = CFRange(location: 0, length: caret.location)
        guard let parameter = AXValueCreate(.cfRange, &prefixRange) else { return nil }
        var prefix: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(composer.el, "AXStringForRange" as CFString,
                  parameter, &prefix) == .success, let prefix = prefix as? String,
              draftEndPrefixMatches(original, prefix: prefix, offset: caret.location) else { return nil }
        return caret
    }
    if endCaret() == nil {
        guard currentValue(focused: true) == original, contextKey(runningApp.processIdentifier, 125) else { fail("composer-selection-changed", 4) }
    }
    guard currentValue(focused: true) == original, let caret = endCaret() else { fail("composer-selection-changed", 4) }
    if !appending && !comparableDraft(original).isEmpty && !placeholderAtEnd { fail("draft-exists", 4) }
    let insertion = (placeholderAtEnd || comparableDraft(original).isEmpty ? "" : "\n\n") + text
    // Chromium may add a paragraph newline at the insertion boundary. Permit only that
    // separator variation: the original content fingerprint and complete addition must match.
    func insertionConfirmed(_ value: String) -> Bool {
        appendedDraftMatches(value, before: placeholderAtEnd ? draftFingerprint("") : before, addition: text)
    }
    let method = insertWholeText(insertion, editor: composer.el, pid: runningApp.processIdentifier,
        original: original, caret: caret, currentValue: { currentValue(focused: true) }, confirmed: insertionConfirmed)
    var sent = false
    if argValue("--send-label") != nil {
        guard comparableDraft(currentValue(focused: true)) == expectedText else { fail("write-not-applied", 5) }
        let latest = scanWindows()
        checkPreparedDraft(latest.nodes); checkAppendReady(latest.nodes)
        guard latest.webArea, sameOperationWindow(),
              str(operationWindows[0], kAXTitleAttribute as String) == windowTitle,
              selectedDraftConversation(latest.nodes) == conversation,
              reportedSearchModes(latest.nodes, prefix: argValue("--mode-prefix") ?? "") == modes,
              latest.nodes.contains(where: { $0.role == "AXTextArea" && CFEqual($0.el, composer.el) }),
              str(composer.el, kAXValueAttribute as String).map(comparableDraft) == expectedText,
              let send = sendTarget(in: latest.nodes) else { fail("no-sendable-draft", 4) }
        guard AXUIElementPerformAction(send.el, kAXPressAction as CFString) == .success else { fail("send-press-failed", 5) }
        sent = true
    }
    emit(["method": method, "sent": sent], code: 0)

case "attach-image", "attach-files":
    var paths: [String] = []
    var expectedFiles: [(String, Int64, Int64)] = []
    if verb == "attach-image" {
        guard let path = argValue("--image"), path.hasSuffix(".png"), let image = NSImage(contentsOfFile: path), image.isValid else { fail("invalid-image", 4) }
        paths = [path]
    } else {
        guard let raw = argValue("--files"), let data = raw.data(using: .utf8),
              let files = (try? JSONSerialization.jsonObject(with: data)) as? [[String: Any]],
              !files.isEmpty, files.count <= 8 else { fail("invalid-files", 4) }
        for file in files {
            guard let path = file["Path"] as? String, path.hasPrefix("/"),
                  let size = file["Size"] as? Int64, size > 0, size <= 50 * 1024 * 1024,
                  let modified = file["Modified"] as? Int64 else { fail("invalid-files", 4) }
            expectedFiles.append((path, size, modified)); paths.append(path)
        }
        guard expectedFiles.reduce(Int64(0), { $0 + $1.1 }) <= 100 * 1024 * 1024 else { fail("files-too-large", 4) }
    }
    func filesUnchanged() -> Bool {
        expectedFiles.allSatisfy { path, size, modified in
            var info = stat()
            guard lstat(path, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG else { return false }
            return info.st_size == size && Int64(info.st_mtimespec.tv_sec) * 1000 + Int64(info.st_mtimespec.tv_nsec / 1_000_000) == modified
        }
    }
    guard argValue("--expect-target") != nil, filesUnchanged() else { fail("files-changed", 4) }
    guard waitForWebContent(seconds: 2) else { fail("surface-unavailable", 5) }
    let initial = scanWindows()
    guard initial.webArea else { fail("surface-unavailable", 5) }
    checkPreparedDraft(initial.nodes); checkAppendReady(initial.nodes)
    let names = paths.map { URL(fileURLWithPath: $0).lastPathComponent }
    guard Set(names.map { $0.lowercased() }).count == names.count else { fail("attachment-name-exists", 4) }
    func visible(_ name: String, _ nodes: [Node]) -> Bool {
        nodes.contains { ["AXImage", "AXStaticText", "AXButton"].contains($0.role) && $0.text == name }
    }
    func attachmentVisible(_ nodes: [Node]) -> Bool {
        names.allSatisfy { visible($0, nodes) }
    }
    if verb == "attach-image" && attachmentVisible(initial.nodes) { emit(["attached": true], code: 0) }
    if verb == "attach-files" && names.contains(where: { visible($0, initial.nodes) }) { fail("attachment-name-exists", 4) }
    guard let editor = initial.nodes.first(where: { $0.role == "AXTextArea" }) else { fail("no-unique-composer", 4) }
    func currentImageDraft() -> String {
        let scan = scanWindows()
        guard scan.webArea, sameOperationWindow(),
              scan.nodes.contains(where: { $0.role == "AXTextArea" && CFEqual($0.el, editor.el) }),
              let raw = str(editor.el, kAXValueAttribute as String) else { fail("composer-target-changed", 6) }
        checkPreparedDraft(scan.nodes); checkAppendReady(scan.nodes)
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier
            else { fail("app-not-frontmost", 4) }
        return raw
    }
    let value = currentImageDraft()
    AXUIElementSetAttributeValue(editor.el, kAXFocusedAttribute as CFString, kCFBooleanTrue)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.10))
    guard let focused = contextFocused(appEl, operationWindows[0]), CFEqual(focused, editor.el) else { fail("composer-target-changed", 4) }
    // Preserve existing text, including selected text. Paste the file at a verified empty
    // caret at the end; never select all or replace the composer's value.
    let placeholderSpelling = argValues("--draft-placeholder").contains(comparableDraft(value))
        && str(editor.el, kAXDescriptionAttribute as String).map(comparableDraft) == comparableDraft(value)
    // File attachment need not replace any text, including a literal string equal to
    // the placeholder. Preserve the raw value and use an empty caret at zero in this
    // ambiguous case; do not infer emptiness from an attachment-enabled Send button.
    var end = CFRange(location: placeholderSpelling ? 0 : value.utf16.count, length: 0)
    if let range = AXValueCreate(.cfRange, &end) {
        AXUIElementSetAttributeValue(editor.el, kAXSelectedTextRangeAttribute as CFString, range)
    }
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.05))
    func imageEndCaret() -> Bool {
        guard let caret = draftSelection(editor.el), caret.length == 0,
              caret.location <= value.utf16.count else { return false }
        if placeholderSpelling && caret.location == 0 { return true }
        if comparableDraft((value as NSString).substring(from: caret.location)).isEmpty { return true }
        var prefixRange = CFRange(location: 0, length: caret.location)
        guard let parameter = AXValueCreate(.cfRange, &prefixRange) else { return false }
        var prefix: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(editor.el, "AXStringForRange" as CFString,
                  parameter, &prefix) == .success, let prefix = prefix as? String else { return false }
        return draftEndPrefixMatches(value, prefix: prefix, offset: caret.location)
    }
    if !imageEndCaret() {
        guard currentImageDraft() == value, contextKey(runningApp.processIdentifier, 125) else { fail("composer-selection-changed", 4) }
    }
    guard currentImageDraft() == value, imageEndCaret(),
          let stillFocused = contextFocused(appEl, operationWindows[0]), CFEqual(stillFocused, editor.el)
        else { fail("composer-selection-changed", 4) }
    let board = contextPasteboard(), before = board.changeCount
    guard let saved = savedPasteboard(board), board.changeCount == before else { fail("clipboard-busy", 4) }
    guard filesUnchanged() else { fail("files-changed", 4) }
    board.clearContents(); board.writeObjects(paths.map { URL(fileURLWithPath: $0) as NSURL }); let written = board.changeCount
    guard contextKey(runningApp.processIdentifier, 9) else { restorePasteboard(board, saved, revision: written); fail("app-not-frontmost", 4) }
    imagePasteStarted = true
    let deadline = Date().addingTimeInterval(2)
    var attached = false
    repeat {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.08))
        let scan = scanWindows()
        // Restore before a guard can exit; do not overwrite a copy made by another app.
        attached = attachmentVisible(scan.nodes)
        if attached { break }
    } while Date() < deadline
    restorePasteboard(board, saved, revision: written)
    guard attached, filesUnchanged(), currentImageDraft() == value else { fail("attachment-unconfirmed", 4) }
    emit(["attached": true], code: 0)
case "draft-target":
    guard waitForWebContent(seconds: 2) else { fail("surface-unavailable", 5) }
    let scan = scanWindows()
    guard scan.webArea else { fail("surface-unavailable", 5) }
    let target = draftTarget(scan.nodes)
    guard let composer = scan.nodes.first(where: { $0.role == "AXTextArea" }),
          let value = str(composer.el, kAXValueAttribute as String) else { fail("composer-value-unavailable", 4) }
    if !hasFlag("--allow-existing") && !comparableDraft(value).isEmpty && !placeholderDraft(composer.el, value: value) {
        fail("draft-exists", 4)
    }
    emit(["target": target], code: 0)
case "search":
    let action = argValue("--action") ?? ""
    guard ["open", "probe", "read", "focus", "write", "select"].contains(action) else { failSearch("invalid-search-action") }
    // Find Chat is an explicit navigation request. Bring its already-selected app window
    // forward in this same operation. Passive polling and delayed audio may never do this.
    if action == "open" && NSWorkspace.shared.frontmostApplication?.processIdentifier != runningApp.processIdentifier {
        guard operationWindows.count == 1, runningApp.activate() else { failSearch("app-not-frontmost") }
        let deadline = Date().addingTimeInterval(0.6)
        // AppKit refreshes running-application/workspace state on the run loop. Sleeping here
        // kept the old foreground app cached and falsely reported an activation failure.
        while NSWorkspace.shared.frontmostApplication?.processIdentifier != runningApp.processIdentifier && Date() < deadline {
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.05))
        }
        guard sameOperationWindow() else { failSearch("search-target-changed") }
    }
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier else { failSearch("app-not-frontmost") }
    if action == "open" {
        let scan = scanWindows()
        guard scan.webArea, sameOperationWindow() else { failSearch("no-surface") }
        checkSearchMode(scan.nodes, pinned: false)
        if searchSurface(scan.nodes) == nil {
            // An existing field with an unsupported container must not make Retry toggle
            // an already-open search panel. Only press the opener when no field is present.
            let inspection = inspectSearch(scan.nodes)
            guard inspection.error == "search-field-missing" else { failSearch(inspection.error) }
            guard let button = uniqueEnabledButton(matching: argValues("--search"), in: scan.nodes) else { failSearch("search-button-missing") }
            guard AXUIElementPerformAction(button.el, kAXPressAction as CFString) == .success else { failSearch("search-open-failed", 5) }
            let deadline = Date().addingTimeInterval(1)
            repeat {
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.1))
                if searchSurface(scanWindows().nodes) != nil { break }
            } while Date() < deadline && sameOperationWindow()
        }
    } else if action == "probe" {
        guard argValue("--origin")?.isEmpty == false else { failSearch("missing-search-origin") }
    } else if argValue("--target")?.isEmpty != false { failSearch("missing-search-target") }
    if ["focus", "write", "select"].contains(action) && argValue("--query") == nil { failSearch("missing-search-query") }
    let surface = checkedSearch()
    if action == "open" || action == "focus" {
        // Some fields are already focused but do not implement the focus setter.
        if (attr(surface.field.el, kAXFocusedAttribute as String) as? Bool) != true {
            guard AXUIElementSetAttributeValue(surface.field.el, kAXFocusedAttribute as CFString, kCFBooleanTrue) == .success else { failSearch("search-focus-failed", 5) }
        }
        emitSearch(checkedSearch())
    }
    if action == "write" {
        guard let text = argValue("--value"), !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.count <= 500 else { failSearch("invalid-query") }
        guard AXUIElementSetAttributeValue(surface.field.el, kAXValueAttribute as CFString, text as CFString) == .success else { failSearch("search-write-failed", 5) }
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.2))
        let updated = checkedSearch(queryOverride: text)
        guard updated.token == surface.token else { failSearch("search-write-unconfirmed", 5) }
        emitSearch(updated)
    }
    if action == "select" {
        let matches = searchResults(surface).filter { $0.id == argValue("--value") && $0.node.text == argValue("--title") }
        guard matches.count == 1 else { failSearch("search-result-changed") }
        let latest = checkedSearch()
        guard let match = searchResults(latest).first(where: { $0.id == matches[0].id && $0.node.text == matches[0].node.text }),
              CFEqual(match.node.el, matches[0].node.el) else { failSearch("search-result-changed") }
        guard AXUIElementPerformAction(match.node.el, kAXPressAction as CFString) == .success else { failSearch("search-select-failed", 5) }
        emit(["target": surface.token, "origin": searchOrigin() ?? "", "query": surface.query, "results": []], code: 0)
    }
    emitSearch(surface)

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
    // app-agnostic). DFS order is the sidebar's own order, i.e. recency. Prefer explicit
    // status labels across AX roles/attributes; older builds expose an unnamed spinner.
    let baselineImages = argValues("--idle-images").compactMap { entry -> Int? in
        let parts = entry.split(separator: "=", maxSplits: 1)
        return parts.count == 2 && String(parts[0]) == mode ? Int(parts[1]) : nil
    }.first
    var readings: [(title: String, state: String, selected: Bool)] = []
    if let convMarker = argValue("--conv-marker"), !convMarker.isEmpty {
        let awaiting = argValues("--state-awaiting")
        let unread = argValues("--state-unread")
        let running = argValues("--state-running")
        var i = 0
        while i < nodes.count && readings.count < 8 {
            let n = nodes[i]
            guard n.pressable && !n.text.isEmpty else { i += 1; continue }

            var j = i + 1
            var hasMarker = false
            while j < nodes.count && nodes[j].depth > n.depth {
                let m = nodes[j]
                if m.pressable && m.text == convMarker { hasMarker = true }
                j += 1
            }

            if hasMarker {
                let selected = conversationSelected(n.el)
                let state = conversationRowState(title: n.text, descendants: nodes[(i + 1)..<j],
                    awaiting: awaiting, unread: unread, running: running, baseline: baselineImages)
                readings.append((n.text, state, selected))
                i = j          // skip the subtree so row controls never read as items
            } else {
                i += 1
            }
        }
    }

    let conversations: [[String: String]] = readings.map { reading in
        return ["title": reading.title, "state": reading.state,
                "selected": reading.selected ? "true" : "false"]
    }
    let copyInspection = replyTarget(nodes)
    emit([
        "surface": true,
        "attention": attention,
        "approvalPresent": approve != nil,
        "denyPresent": deny != nil,
        "stopPresent": stop != nil,
        "voiceChat": voiceState(nodes: nodes, start: argValues("--voice-start"), end: argValues("--voice-end")),
        "canSend": sendTarget(in: nodes) != nil,
        "canCopyAnswer": copyInspection.node != nil,
        "copyAnswerError": copyInspection.error,
        "searchPresent": present("--search"),
        "changesPresent": !mode.isEmpty && mode == argValue("--panel-mode") && panelRouteAvailable(nodes,
            modeLabel: (argValue("--mode-prefix") ?? "") + mode,
            openLabels: argValues("--changes"), visibleLabels: argValues("--panel-visible")),
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

case "copy-reply":
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier else { fail("app-not-frontmost", 4) }
    guard waitForWebContent(seconds: 2), operationWindows.count == 1, sameOperationWindow() else { fail("surface-unavailable", 4) }
    let initial = scanWindows()
    guard initial.webArea else { fail("surface-unavailable", 4) }
    let inspection = replyTarget(initial.nodes)
    guard let target = inspection.node else { fail(inspection.error, 4) }
    let windowKey = contextWindowKey(operationWindows[0])
    let conversation = selectedDraftConversation(initial.nodes)
    func confirmedReply(afterCopy: Bool = false) -> Bool {
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == runningApp.processIdentifier,
              sameOperationWindow(), contextWindowKey(operationWindows[0]) == windowKey else { return false }
        let fresh = scanWindows()
        guard fresh.webArea, selectedDraftConversation(fresh.nodes) == conversation,
              let current = replyTarget(fresh.nodes, copiedTarget: afterCopy ? target.el : nil).node else { return false }
        return CFEqual(current.el, target.el)
    }
    let board = contextPasteboard()
    let before = board.changeCount
    guard confirmedReply() else { fail("answer-changed", 4) }
    guard AXUIElementPerformAction(target.el, kAXPressAction as CFString) == .success else { fail("copy-unconfirmed", 5) }
    let deadline = Date().addingTimeInterval(1.2)
    let completedLabels = Set(argValues("--copy-completed").map(normalizedButtonLabel))
    func acknowledged() -> Bool {
        !completedLabels.isEmpty && buttonLabels(target.el).contains { completedLabels.contains(normalizedButtonLabel($0)) }
    }
    while (board.changeCount == before || !acknowledged()) && Date() < deadline {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.025))
    }
    guard confirmedReply(afterCopy: true) else { fail("answer-changed", 4) }
    let revision = board.changeCount
    guard acknowledged(), revision != before, let text = board.string(forType: .string),
          !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
          text.utf16.count <= 50000, board.changeCount == revision else { fail("copy-unconfirmed", 5) }
    // Leave the app's text/html payload intact. Only the plain text is retained for Paste Reply.
    emit(["text": text], code: 0)

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
    checkPreparedDraft(initial)
    guard let target = sendTarget(in: initial),
          let composer = initial.first(where: { $0.role == "AXTextArea" }),
          let draft = str(composer.el, kAXValueAttribute as String) else { fail("no-sendable-draft", 4) }
    if let expected = argValue("--expect-text") {
        guard !comparableDraft(expected).isEmpty, comparableDraft(draft) == comparableDraft(expected) else {
            fail("draft-changed", 6)
        }
    }
    let windowTitle = str(operationWindows[0], kAXTitleAttribute as String)
    let latestScan = scanWindows()
    let latest = latestScan.nodes
    checkPreparedDraft(latest)
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

case "focus":
    runningApp.activate()
    emit([:], code: 0)

default:
    fail("unknown-verb \(verb)", 4)
}
