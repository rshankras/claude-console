#!/usr/bin/env python3
"""Execute the helper's pure matching/state functions with synthetic AX tree rows.

Extract production function bodies so these tests need neither an Accessibility grant nor
an installed ChatGPT app. Swift compilation of the complete helper is a separate check.
"""
from pathlib import Path
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[2] / 'tools/desktop/VizhiAxBridge.swift').read_text()

def function(name):
    start = source.index('func ' + name + '(')
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

fixture = '''
import Foundation
import CoreGraphics
import CryptoKit
// Minimal AX tree facade for executing the real scanner without live app access.
struct FakeElement {
    let identity = UUID()
    let role: String
    let text: String
    let children: [FakeElement]
    var value: String? = nil
    var enabled: Bool? = true
    var attributes: [String: String] = [:]
    var frame: CGRect? = nil
}
typealias AXUIElement = FakeElement
let kAXRoleAttribute = "AXRole"
let kAXPressAction = "AXPress"
let MAX_DEPTH = 40
let MAX_NODES = 1500
let kAXValueAttribute = "AXValue"
let kAXEnabledAttribute = "AXEnabled"
let kAXTitleAttribute = "AXTitle"
let kAXDescriptionAttribute = "AXDescription"
let kAXSubroleAttribute = "AXSubrole"
func CFEqual(_ a: FakeElement, _ b: FakeElement) -> Bool { a.identity == b.identity }
func conversationSelected(_ el: FakeElement) -> Bool { el.attributes["selected"] == "true" }
func str(_ el: FakeElement, _ name: String) -> String? {
    if name == kAXValueAttribute { return el.value }
    if name == kAXRoleAttribute { return el.role }
    return el.attributes[name]
}
func attr(_ el: FakeElement, _ name: String) -> Any? { el.enabled }
func controlFrame(_ el: FakeElement) -> CGRect? { el.frame }
var arguments = ["--send-label": ["Send"], "--stop": ["Stop"], "--approve": ["Allow"]]
func argValues(_ name: String) -> [String] { arguments[name] ?? [] }
func argValue(_ name: String) -> String? { argValues(name).first }
func children(_ el: FakeElement) -> [FakeElement] { el.children }
func displayText(_ el: FakeElement) -> String { el.text }
func actionNames(_ el: FakeElement) -> [String] { ["AXPress"] }
struct Node {
    let el: AXUIElement
    let role: String
    let text: String
    let pressable: Bool
    let depth: Int
    var labels: [String] = []
}
let original = FakeElement(role: "AXWebArea", text: "original composer", children: [])
let other = FakeElement(role: "AXWebArea", text: "other draft", children: [])
var focusedWindows = [original]
func targetWindows() -> [AXUIElement] { focusedWindows }
'''
fixture += next(line for line in source.splitlines() if line.startswith('let operationWindows =')) + '\n'
fixture += function('comparableDraft') + '\n'
fixture += function('draftFingerprint') + '\n'
fixture += function('draftEndPrefixMatches') + '\n'
fixture += function('appendedDraftMatches') + '\n'
fixture += r'''
let before = draftFingerprint("Customer email")
assert(draftEndPrefixMatches("Email:\nதமிழ் 😀", prefix: "Email:தமிழ் 😀", offset: "Email:தமிழ் 😀".utf16.count))
assert(!draftEndPrefixMatches("Email:\nதமிழ் 😀", prefix: "Email:தமிழ் ", offset: "Email:தமிழ் ".utf16.count))
assert(!draftEndPrefixMatches("Email:  Friday", prefix: "Email: Friday", offset: "Email: Friday".utf16.count))
assert(!draftEndPrefixMatches("Email:\nFriday", prefix: "Email:Friday", offset: 3))
assert(appendedDraftMatches("Customer email\n\nReply politely.\n", before: before, addition: "Reply politely."))
assert(appendedDraftMatches("Customer email\n\n\nReply politely.\n", before: before, addition: "Reply politely."))
assert(appendedDraftMatches("Customer email\nReply politely.\n", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Customer  email\n\n\nReply politely.", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Customer email\n\nReply  politely.", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Customer emailReply politely.", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Edited email\n\nReply politely.", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Customer email\n\nReply", before: before, addition: "Reply politely."))
assert(!appendedDraftMatches("Customer email\n\nReply politely. extra", before: before, addition: "Reply politely."))
assert(appendedDraftMatches("தமிழ் 😀\n", before: draftFingerprint(""), addition: "தமிழ் 😀"))
assert(!appendedDraftMatches("", before: draftFingerprint(""), addition: " "))
''' + '\n'
fixture += function('matchesDraftPlaceholder') + '\n'
fixture += function('isDraftPlaceholder') + '\n'
fixture += r'''
let hint = "Work with ChatGPT"
func emptyHint(_ value: String = "Work with ChatGPT\n", description: String? = "Work with ChatGPT",
               labels: [String] = ["Work with ChatGPT"], caret: CFRange? = CFRange(location: 0, length: 0),
               sendUnavailable: Bool = true) -> Bool {
    isDraftPlaceholder(value: value, description: description, labels: labels,
                       caret: caret, sendUnavailable: sendUnavailable)
}
assert(emptyHint())
assert(!emptyHint("a real existing draft"))
assert(!emptyHint("Work with ChatGPT plus my words"))
assert(!emptyHint(description: nil))
assert(!emptyHint(description: "Other label"))
assert(!emptyHint(labels: []))
assert(!emptyHint(caret: nil))
assert(!emptyHint(caret: CFRange(location: 17, length: 0)))
assert(!emptyHint(caret: CFRange(location: 0, length: 17)))
assert(!emptyHint(sendUnavailable: false))
assert(isDraftPlaceholder(value: hint, description: hint, labels: [hint],
    caret: CFRange(location: 0, length: 0), sendUnavailable: false, emptyTextConfirmed: true))
assert(!isDraftPlaceholder(value: hint, description: hint, labels: [hint],
    caret: CFRange(location: 0, length: 1), sendUnavailable: false, emptyTextConfirmed: true))
''' + '\n'
fixture += function('appendedPlaceholderMatches') + '\n'
fixture += r'''
let placeholderBefore = draftFingerprint("Work with ChatGPT\n")
func placeholderRetry(_ value: String, before: String = placeholderBefore, addition: String = "Explain the screenshot.",
                      description: String? = "Work with ChatGPT", labels: [String] = ["Work with ChatGPT"]) -> Bool {
    appendedPlaceholderMatches(value, before: before, addition: addition, description: description, labels: labels)
}
assert(placeholderRetry("Explain the screenshot.\n"))
assert(!placeholderRetry("Explain the screenshot. plus my text"))
assert(!placeholderRetry("Explain the screenshot.Work with ChatGPT"))
assert(!placeholderRetry("Work with ChatGPT\n\nExplain the screenshot.")) // ordinary prefix verification handles real text
assert(!placeholderRetry("Explain the screenshot.", before: draftFingerprint("My draft")))
assert(!placeholderRetry("Explain the screenshot.", description: nil))
assert(!placeholderRetry("Explain the screenshot.", description: "Other composer"))
assert(!placeholderRetry("Explain the screenshot.", labels: []))
assert(!placeholderRetry("", addition: ""))
assert(!placeholderRetry("Work with ChatGPT", addition: "Work with ChatGPT")) // unchanged placeholder is never success
''' + '\n'
fixture += function('rangeProvesEmptyDraft') + '\n'
fixture += r'''
assert(rangeProvesEmptyDraft(emptyRange: "", firstCharacter: nil, firstCharacterOutOfBounds: true))
assert(rangeProvesEmptyDraft(emptyRange: "", firstCharacter: "", firstCharacterOutOfBounds: false))
assert(!rangeProvesEmptyDraft(emptyRange: nil, firstCharacter: nil, firstCharacterOutOfBounds: true))
assert(!rangeProvesEmptyDraft(emptyRange: "", firstCharacter: nil, firstCharacterOutOfBounds: false))
assert(!rangeProvesEmptyDraft(emptyRange: "", firstCharacter: "W", firstCharacterOutOfBounds: false))
assert(!rangeProvesEmptyDraft(emptyRange: "", firstCharacter: "W", firstCharacterOutOfBounds: true))
assert(!rangeProvesEmptyDraft(emptyRange: "", firstCharacter: " ", firstCharacterOutOfBounds: false))
''' + '\n'
fixture += r'''assert(comparableDraft("\n\r\n \t\u{00a0}").isEmpty)
assert(comparableDraft("Reply Friday.\n") == comparableDraft("Reply Friday."))
assert(comparableDraft("  Reply Friday.\r\n") == comparableDraft("Reply Friday."))
assert(comparableDraft("Reply\nFriday.") != comparableDraft("Reply Friday."))
assert(comparableDraft("Reply  Friday.") != comparableDraft("Reply Friday."))
assert(comparableDraft("Work with ChatGPT") != comparableDraft(""))
assert(comparableDraft("my earlier draft") != comparableDraft("Reply Friday."))
assert(comparableDraft("Reply") != comparableDraft("Reply Friday."))
''' + '\n'
assert 'keyboardSetUnicodeString' not in source, 'Desktop text insertion must not fall back to chunked Unicode events'
fixture += function('buttonLabels') + '\n' + function('normalizedButtonLabel') + '\n'
fixture += '\n'.join(function(n) for n in ['normalizedSearchLabel', 'isSearchField', 'searchContainer', 'reportedSearchModes', 'searchModeError']) + '\n'
fixture += function('scanWindows') + '\n'
fixture += function('conversationMatches') + '\n' + function('conversationState')
fixture += '\n' + function('conversationRowState')
fixture += '\n' + function('currentConversationFlag')
fixture += '\n' + function('replyActionRun') + '\n' + function('sameReplyControlRow')
fixture += '\n' + function('positionedReplyRow') + '\n' + function('replySpeakerLabel')
fixture += '\n' + function('replyConversationNodes') + '\n' + function('replyTarget')
fixture += '\n' + '\n'.join(function(n) for n in ['firstPressable', 'exactButtons', 'uniqueEnabledButton',
    'voiceState', 'voiceTarget', 'composerSendTarget', 'sendTarget'])
fixture += '''
func row(_ text: String, _ depth: Int = 0) -> Node {
    Node(el: original, role: "AXButton", text: text, pressable: true, depth: depth, labels: [text])
}
// Simulate the focus change during the write's settle interval. Re-scanning for Send
// must still return the original tree, never the other window's draft.
assert(scanWindows().nodes.first?.text == "original composer")
focusedWindows = [other]
assert(scanWindows().nodes.first?.text == "original composer")
let nodes = [row("Deployment Plan"), row("Pin chat", 1), row("Plan"), row("Pin chat", 1)]
assert(conversationMatches(title: "Plan", marker: "Pin chat", nodes: nodes).count == 1)
assert(conversationMatches(title: "Plan", marker: "Pin chat", nodes: Array(nodes.prefix(2))).isEmpty)
assert(conversationMatches(title: "Plan", marker: "Pin chat", nodes: [row("Plan"), row("Other"), row("Pin chat", 1)]).isEmpty)
assert(conversationMatches(title: "Plan", marker: "Pin chat", nodes: [row("Plan"), row("Pin chat", 1), row("Plan"), row("Pin chat", 1)]).count == 2)
assert(conversationMatches(title: "plan", marker: "Pin chat", nodes: nodes).isEmpty)
// Each row is independent: one running row and an all-running sidebar both remain running.
for baseline in [1, 2] {
    assert(conversationState(state: "idle", images: baseline, baseline: baseline) == "idle")
    for _ in 0..<3 {
        assert(conversationState(state: "idle", images: baseline + 1, baseline: baseline) == "running")
    }
    assert(conversationState(state: "awaiting", images: baseline + 1, baseline: baseline) == "awaiting")
    assert(conversationState(state: "unread", images: baseline + 1, baseline: baseline) == "unread")
}
assert(conversationState(state: "idle", images: 3, baseline: nil) == "idle")
assert(currentConversationFlag(ariaCurrent: "page", legacySelected: false))
assert(currentConversationFlag(ariaCurrent: "true", legacySelected: false))
assert(currentConversationFlag(ariaCurrent: " PAGE ", legacySelected: false))
assert(currentConversationFlag(ariaCurrent: nil, legacySelected: true))
assert(!currentConversationFlag(ariaCurrent: nil, legacySelected: false))
for value in ["false", "", "step", "location", "date", "time", "unknown"] {
    assert(!currentConversationFlag(ariaCurrent: value, legacySelected: true))
}
func node(_ role: String, _ text: String, _ depth: Int, enabled: Bool? = true, value: String? = nil,
          labels: [String]? = nil, frame: CGRect? = nil) -> Node {
    Node(el: FakeElement(role: role, text: text, children: [], value: value, enabled: enabled, frame: frame),
         role: role, text: text, pressable: role == "AXButton", depth: depth, labels: labels ?? [text])
}
let web = node("AXWebArea", "", 0)
func rowState(_ children: [Node], title: String = "Text Voice draft", baseline: Int? = nil) -> String {
    conversationRowState(title: title, descendants: children[...], awaiting: ["Awaiting approval"],
        unread: ["Unread", "Complete"], running: ["Thinking", "Working"], baseline: baseline)
}
// Reproduce the reported lifecycle without assuming the older unnamed-image count.
for role in ["AXStaticText", "AXImage", "AXGroup", "AXProgressIndicator", "AXBusyIndicator"] {
    assert(rowState([node(role, "Thinking", 1)]) == "running")
    assert(rowState([node(role, "Complete", 1)]) == "unread")
    assert(rowState([node(role, "Awaiting approval", 1)]) == "awaiting")
    // A title/value must not hide the status in the description/help/label attribute.
    assert(rowState([node(role, "activity", 1, labels: ["activity", "Thinking"])]) == "running")
    assert(rowState([node(role, "activity", 1, labels: ["Complete"])]) == "unread")
}
assert(rowState([node("AXStaticText", "  THINKING ", 1)]) == "running")
// Installed app: taskRow.working is a role=status container named Working. It is
// not the transcript's Thinking text, and its spinner need not be an AXImage.
assert(rowState([node("AXGroup", "Working", 1)]) == "running")
assert(rowState([node("AXStaticText", "Unread", 1)]) == "unread")
assert(rowState([]) == "idle")
assert(rowState([node("AXStaticText", "Thinking about travel", 1)]) == "idle")
assert(rowState([node("AXStaticText", "Thinking", 1)], title: "Thinking") == "idle")
assert(rowState([node("AXStaticText", "Complete", 1)], title: "Complete") == "idle")
assert(rowState([node("AXButton", "Complete", 1)]) == "idle")
assert(rowState([node("AXTextArea", "Thinking", 1)]) == "idle")
assert(rowState([node("AXStaticText", "Thinking", 1), node("AXImage", "Unread", 1)]) == "unread")
assert(rowState([node("AXImage", "Complete", 1), node("AXStaticText", "Awaiting approval", 1)]) == "awaiting")
assert(rowState([node("AXImage", "", 1), node("AXImage", "", 1)], baseline: 1) == "running")
// Only the row's slice contributes: a neighbouring conversation cannot light this key.
let adjacent = [row("First"), row("Pin chat", 1), row("Second"), row("Pin chat", 1), node("AXStaticText", "Thinking", 1)]
assert(conversationRowState(title: "First", descendants: adjacent[1..<2], awaiting: [], unread: ["Complete"], running: ["Thinking"], baseline: nil) == "idle")
let group = node("AXGroup", "", 1)
let composer = node("AXTextArea", "reviewed draft", 2, value: "reviewed draft")
let send = node("AXButton", "Send", 2)
let eligible = [web, group, composer, send]
assert(sendTarget(in: eligible)?.text == "Send")
// A disabled/unknown-enabled button, wrong label, duplicate composer, or draftless placeholder
// must never submit. An unrelated Send button elsewhere in the window is not a fallback.
assert(sendTarget(in: [web, group, composer, node("AXButton", "Send", 2, enabled: false)]) == nil)
assert(sendTarget(in: [web, group, composer, node("AXButton", "Send", 2, enabled: nil)]) == nil)
assert(sendTarget(in: [web, group, composer, node("AXButton", "Send feedback", 2)]) == nil)
assert(sendTarget(in: eligible + [composer]) == nil)
assert(sendTarget(in: eligible + [send]) == nil)
assert(sendTarget(in: [web, group, node("AXTextArea", "Ask anything", 2, value: ""), send]) == nil)
assert(sendTarget(in: [web, group, node("AXTextArea", " ", 2, value: " "), send]) == nil)
assert(sendTarget(in: [web, group, composer, node("AXGroup", "other", 1), send]) == nil)
assert(sendTarget(in: eligible + [node("AXButton", "Stop", 1)]) == nil)
assert(sendTarget(in: eligible + [node("AXButton", "Allow", 1)]) == nil)
// Task Stop must not match the native voice stop control, regardless of DFS order.
let startLabels = ["Start voice chat", "Start new voice chat"]
let endLabels = ["Stop voice chat"]
let startVoice = node("AXButton", "Start voice chat", 1)
let endVoice = node("AXButton", "Stop voice chat", 1)
let taskStop = node("AXButton", "Stop", 1)
assert(uniqueEnabledButton(matching: ["Stop"], in: [endVoice, taskStop])?.text == "Stop")
assert(uniqueEnabledButton(matching: ["Stop"], in: [endVoice]) == nil)
assert(uniqueEnabledButton(matching: ["Stop"], in: [taskStop, taskStop]) == nil)
assert(sendTarget(in: eligible + [endVoice]) != nil)
func state(_ nodes: [Node]) -> String { voiceState(nodes: nodes, start: startLabels, end: endLabels) }
func target(_ action: String, _ nodes: [Node]) -> Node? {
    voiceTarget(action: action, nodes: nodes, start: startLabels, end: endLabels)
}
assert(state([startVoice]) == "ready")
assert(state([node("AXButton", "Start new voice chat", 1)]) == "ready")
assert(state([endVoice]) == "active")
assert(state([startVoice, endVoice]) == "active")
assert(target("start", [startVoice])?.text == "Start voice chat")
assert(target("end", [endVoice])?.text == "Stop voice chat")
// Stale expected states cannot turn End into Start or Start into End.
assert(target("start", [endVoice]) == nil)
assert(target("start", [startVoice, endVoice]) == nil)
assert(target("end", [startVoice]) == nil)
assert(target("toggle", [startVoice]) == nil)
// Icon/title/value must not shadow the action name in description/help. This executes the
// production attribute reader as well as the matcher; fixture names do not claim live labels.
for attribute in [kAXTitleAttribute, kAXDescriptionAttribute, "AXHelp", "AXLabel"] {
    let icon = FakeElement(role: "AXButton", text: "×", children: [], value: "0",
        attributes: [attribute: "Stop voice chat"])
    let labels = buttonLabels(icon)
    assert(labels == ["Stop voice chat"])
    assert(state([node("AXButton", "×", 1, labels: labels)]) == "active")
    assert(target("end", [node("AXButton", "0", 1, labels: labels)]) != nil)
}
assert(state([node("AXButton", "icon", 1, labels: ["START  VOICE\\nCHAT"])]) == "ready")
// Exact tooltip supplied by the user for this installed ChatGPT build.
assert(state([node("AXButton", "Start Voice Chat", 1)]) == "ready")
assert(state([node("AXButton", "icon", 1, labels: ["Start Voice Chat"])]) == "ready")
assert(target("start", [node("AXButton", "icon", 1, labels: ["Start Voice Chat"])]) != nil)
assert(state([node("AXButton", "Stop voice chat", 1, labels: [])]) == "unavailable")
assert(state([node("AXButton", "×", 1, labels: ["Close"])]) == "unavailable")
assert(state([node("AXButton", "×", 1, labels: ["Learn how to Stop voice chat"])]) == "unavailable")
let valueOnly = FakeElement(role: "AXButton", text: "", children: [], value: "Start voice chat")
assert(buttonLabels(valueOnly).isEmpty)
let repeatedLabel = node("AXButton", "icon", 1, labels: ["Stop voice chat", "Stop voice chat"])
assert(target("end", [repeatedLabel]) != nil) // one control with two matching attributes is unique
// A conversation named exactly like a control still has nested sidebar actions; ignore it.
let pin = node("AXButton", "Pin chat", 2)
assert(state([startVoice, pin]) == "unavailable")
assert(state([endVoice, pin]) == "unavailable")
assert(uniqueEnabledButton(matching: ["Stop"], in: [taskStop, pin]) == nil)
assert(target("start", [startVoice, pin, startVoice])?.text == "Start voice chat")
// No control, duplicates, text content, near matches, and unknown availability fail closed.
for candidates in [[], [startVoice, startVoice], [endVoice, endVoice],
    [node("AXButton", "How to Start voice chat", 1)],
    [node("AXStaticText", "Start voice chat", 1)],
    [node("AXButton", "Start voice chat", 1, enabled: false)],
    [node("AXButton", "Start voice chat", 1, enabled: nil)]] as [[Node]] {
    assert(state(candidates) == "unavailable")
    assert(target("start", candidates) == nil)
    assert(target("end", candidates) == nil)
}
let disabledEnd = node("AXButton", "Stop voice chat", 1, enabled: false)
assert(state([startVoice, disabledEnd]) == "active")
assert(target("start", [startVoice, disabledEnd]) == nil)
assert(target("end", [disabledEnd]) == nil)
assert(voiceState(nodes: [startVoice], start: [], end: endLabels) == "unavailable")
// Nested composer wrappers remain supported within the same local group.
assert(sendTarget(in: [web, group, node("AXGroup", "editor", 2),
    node("AXTextArea", "draft", 3, value: "draft"), send]) != nil)
let placeholderField = Node(el: FakeElement(role: "AXTextField", text: "", children: [],
    attributes: ["AXPlaceholderValue": "Search…"]), role: "AXTextField", text: "", pressable: false, depth: 3)
assert(isSearchField(placeholderField, names: ["Search"]))
assert(!isSearchField(placeholderField, names: ["Search conversations"]))
let semanticField = Node(el: FakeElement(role: "AXTextField", text: "", children: [],
    attributes: ["AXSubrole": "AXSearchField"]), role: "AXTextField", text: "", pressable: false, depth: 3)
assert(isSearchField(semanticField, names: []))
let fakeComposerSearch = Node(el: placeholderField.el, role: "AXTextArea", text: "Search", pressable: false, depth: 3)
assert(!isSearchField(fakeComposerSearch, names: ["Search"]))
let nestedWeb = [node("AXWindow", "window", 0), node("AXSheet", "search sheet", 1), node("AXWebArea", "content", 2), placeholderField]
assert(searchContainer(3, nodes: nestedWeb) == 1)
assert(searchContainer(2, nodes: [node("AXWindow", "window", 0), node("AXWebArea", "content", 1), placeholderField]) == nil)
let popupMode = node("AXPopUpButton", "Mode: ChatGPT", 1)
assert(reportedSearchModes([popupMode, node("AXStaticText", "Mode: ChatGPT", 2)], prefix: "Mode: ") == ["ChatGPT"])
assert(reportedSearchModes([popupMode, node("AXStaticText", "Mode: Codex", 2)], prefix: "Mode: ") == ["ChatGPT", "Codex"])
assert(reportedSearchModes([node("AXStaticText", "Mode: ChatGPT", 1)], prefix: "Mode: ").isEmpty)
assert(reportedSearchModes([], prefix: "Mode: ").isEmpty)
let describedPopup = Node(el: FakeElement(role: "AXPopUpButton", text: "Switch mode", children: [],
    attributes: ["AXDescription": "Mode: ChatGPT"]), role: "AXPopUpButton", text: "Switch mode", pressable: true, depth: 1)
assert(reportedSearchModes([describedPopup], prefix: "Mode: ") == ["ChatGPT"])
let groupMode = Node(el: describedPopup.el, role: "AXGroup", text: "Mode: ChatGPT", pressable: true, depth: 1)
assert(reportedSearchModes([groupMode], prefix: "Mode: ") == ["ChatGPT"])
assert(reportedSearchModes([node("AXGroup", "Mode: ChatGPT", 1)], prefix: "Mode: ").isEmpty)
assert(reportedSearchModes([node("AXLink", "Mode: ChatGPT", 1)], prefix: "Mode: ").isEmpty)
assert(searchModeError([], expected: "ChatGPT", pinned: false) == "mode-unavailable")
assert(searchModeError([], expected: "ChatGPT", pinned: true) == nil)
assert(searchModeError(["ChatGPT"], expected: "ChatGPT", pinned: false) == nil)
assert(searchModeError(["Codex"], expected: "ChatGPT", pinned: true) == "mode-changed")
assert(searchModeError(["ChatGPT", "Codex"], expected: "ChatGPT", pinned: true) == "mode-changed")
assert(searchModeError([], expected: "", pinned: true) == "mode-unavailable")
// Execute the exact production reply selector over synthetic message/action hierarchies.
arguments["--assistant-heading"] = ["ChatGPT said:"]
arguments["--user-heading"] = ["You said:"]
arguments["--copy-response"] = ["Copy response"]
arguments["--copy-button"] = ["Copy"]
arguments["--copy-completed"] = ["Copied"]
arguments["--response-action"] = ["Fork chat from here", "Continue in new chat", "More actions", "Rate response"]
let replyBase = [web, node("AXTextArea", "unselected composer", 1)]
func message(_ assistant: Bool, footer: Bool = true, explicit: Bool = false) -> [Node] {
    var result = [node("AXGroup", "", 1), node("AXHeading", "", 2),
        node("AXStaticText", assistant ? "ChatGPT said:" : "You said:", 3),
        node("AXGroup", "markdown", 2), node("AXGroup", "code block", 3),
        node("AXButton", "Copy", 4), node("AXStaticText", "Thinking and tool details", 3)]
    if footer { result += [node("AXGroup", "actions", 2),
        node("AXButton", explicit ? "Copy response" : "Copy", 3),
        node("AXButton", "Fork chat from here", 3)] }
    return result
}
let oldReply = message(true)
let userMessage = message(false)
let newest = message(true)
let transcript = replyBase + oldReply + userMessage + newest
assert(CFEqual(replyTarget(transcript).node!.el, newest[newest.count - 2].el))
assert(replyTarget(replyBase + oldReply + userMessage).error == "no-answer")
assert(replyTarget(replyBase + oldReply + userMessage + message(true, footer: false)).error == "reply-action-not-found")
assert(replyTarget(replyBase).error == "reply-unrecognized")
assert(replyTarget(replyBase + message(true, explicit: true)).node != nil)
assert(replyTarget(transcript + [node("AXButton", "Stop", 1)]).error == "answer-not-ready")
assert(replyTarget(transcript + [node("AXButton", "Allow", 1)]).error == "answer-not-ready")
let extraEditors = [node("AXTextArea", "sidebar editor", 1)] + transcript + [node("AXTextArea", "secondary editor", 1)]
assert(CFEqual(replyTarget(extraEditors).node!.el, newest[newest.count - 2].el))
assert(CFEqual(replyTarget(transcript.filter { $0.role != "AXTextArea" }).node!.el, newest[newest.count - 2].el))
assert(replyTarget(replyBase + oldReply + userMessage + [node("AXTextArea", "secondary editor", 1)]).error == "no-answer")
assert(CFEqual(replyTarget(transcript + [node("AXWebArea", "another web area", 1)]).node!.el, newest[newest.count - 2].el))
// A sibling or nested diff preview must not disable the one identified conversation,
// and even an explicit Copy response in that preview is never a conversation target.
func deeper(_ nodes: [Node], by offset: Int) -> [Node] {
    nodes.map { Node(el: $0.el, role: $0.role, text: $0.text, pressable: $0.pressable,
                    depth: $0.depth + offset, labels: $0.labels) }
}
let preview = [node("AXWebArea", "Preview", 0), node("AXHeading", "Diff", 1),
    node("AXButton", "Copy response", 1), node("AXButton", "Fork chat from here", 1)]
for extra in [preview, deeper(preview, by: 1)] {
    let withPreview = transcript + extra
    assert(CFEqual(replyTarget(withPreview).node!.el, newest[newest.count - 2].el))
    assert(replyTarget(replyBase + oldReply + userMessage + extra).error == "no-answer")
    assert(replyTarget(replyBase + message(true, footer: false) + extra).node == nil)
    assert(replyTarget(withPreview + [node("AXButton", "Stop", 2)]).error == "answer-not-ready")
    assert(replyTarget(withPreview + [node("AXSheet", "", 2)]).error == "reply-dialog-open")
}
let innerChat = preview + deeper(transcript, by: 2)
assert(CFEqual(replyTarget(innerChat).node!.el, newest[newest.count - 2].el))
// Never choose an older answer simply because a second conversation has only a user turn.
for speaker in [true, false] {
    let second = [node("AXWebArea", "Other conversation", 1)] + deeper(message(speaker), by: 1)
    assert(replyTarget(transcript + second).error == "reply-web-area-multiple")
}
assert(replyTarget(replyBase + preview).error == "reply-web-area-multiple")
let interruptedFooter = [node("AXHeading", "ChatGPT said:", 1), node("AXGroup", "body", 1),
    node("AXButton", "Copy", 2), node("AXWebArea", "Preview", 2), node("AXStaticText", "Diff content", 3),
    node("AXButton", "Fork chat from here", 2)]
assert(replyTarget(replyBase + interruptedFooter).node == nil)
assert(replyTarget(transcript.filter { $0.role != "AXWebArea" }).error == "reply-web-area-missing")
assert(replyTarget(transcript + [node("AXSheet", "", 1)]).error == "reply-dialog-open")
for subrole in ["AXApplicationDialog", "AXDialog"] {
    let dialog = Node(el: FakeElement(role: "AXGroup", text: "", children: [], attributes: ["AXSubrole": subrole]),
        role: "AXGroup", text: "", pressable: false, depth: 1)
    assert(replyTarget(transcript + [dialog]).error == "reply-dialog-open")
}
arguments["--conv-marker"] = ["Pin chat"]
let selectedRow = Node(el: FakeElement(role: "AXButton", text: "Conversation", children: [], attributes: ["selected": "true"]),
    role: "AXButton", text: "Conversation", pressable: true, depth: 1)
let selectedSidebar = [selectedRow, node("AXButton", "Pin chat", 2)]
assert(replyTarget(transcript + selectedSidebar).node != nil)
assert(replyTarget(transcript + selectedSidebar + selectedSidebar).error == "reply-selection-multiple")
arguments.removeValue(forKey: "--conv-marker")
let duplicated = transcript + [node("AXGroup", "other actions", 2), node("AXButton", "Copy", 3), node("AXButton", "Fork chat from here", 3)]
assert(replyTarget(duplicated).error == "reply-copy-multiple")
assert(replyTarget(duplicated + [node("AXTextArea", "secondary editor", 1)]).error == "reply-copy-multiple")
// Each missing-control outcome identifies an existing refusal, never a new target.
let onlyHeading = [node("AXHeading", "ChatGPT said:", 1)]
assert(replyTarget(replyBase + onlyHeading).error == "reply-copy-not-found")
assert(replyTarget(replyBase + oldReply + onlyHeading).error == "reply-copy-outside-latest")
assert(replyTarget(replyBase + onlyHeading + [node("AXPopUpButton", "Copy", 1)]).error == "reply-copy-wrong-role")
assert(replyTarget(replyBase + onlyHeading + [node("AXButton", "Copy", 1), node("AXButton", "Nested action", 2)]).error == "reply-copy-nested-control")
let noFooter = message(true, footer: false)
assert(replyTarget(replyBase + noFooter + [node("AXStaticText", "Copy response", 2)]).error == "reply-action-not-found")
let disabled = replyBase + noFooter + [node("AXGroup", "actions", 2), node("AXButton", "Copy response", 3, enabled: false)]
assert(replyTarget(disabled).error == "answer-not-ready")
let copied = Node(el: newest[newest.count - 2].el, role: "AXButton", text: "Copied", pressable: true, depth: 3, labels: ["Copied"])
let acknowledged = replyBase + noFooter + [node("AXGroup", "actions", 2), copied, node("AXButton", "Fork chat from here", 3)]
assert(replyTarget(acknowledged).error == "reply-action-row-unrecognized")
assert(replyTarget(acknowledged, copiedTarget: copied.el).node != nil)
assert(replyTarget(acknowledged, copiedTarget: oldReply[oldReply.count - 2].el).error == "reply-action-row-unrecognized")
// The ChatGPT viewer moves Branch in new chat into a More actions popup. The
// generic Copy belongs to that same footer; a hidden branch menu item is not a sibling.
for role in ["AXButton", "AXPopUpButton"] {
    let chatFooter = [node("AXGroup", "actions", 2), node("AXButton", "Copy", 3),
        node(role, "More actions", 3)]
    let currentChat = replyBase + oldReply + userMessage + noFooter + chatFooter
    assert(CFEqual(replyTarget(currentChat).node!.el, chatFooter[1].el))
    // No response Copy: a code block cannot borrow the footer's More actions.
    let withoutCopy = replyBase + noFooter + [chatFooter[0], chatFooter[2]]
    assert(replyTarget(withoutCopy).node == nil)
    assert(replyTarget(currentChat + message(false)).node == nil)
}
let branchFooter = [node("AXGroup", "actions", 2), node("AXButton", "Copy", 3),
    node("AXButton", "Continue in new chat", 3)]
assert(replyTarget(replyBase + noFooter + branchFooter).node != nil)
// The viewer can place its role heading outside the body+footer container.
// With no full-answer Copy, do not mistake its code block's Copy for the reply.
let splitHeading = [node("AXHeading", "ChatGPT said:", 1), node("AXGroup", "body and footer", 1),
    node("AXGroup", "markdown", 2), node("AXStaticText", "Full answer text", 3),
    node("AXGroup", "code", 3), node("AXButton", "Copy", 4),
    node("AXGroup", "footer", 2), node("AXPopUpButton", "More actions", 3)]
assert(replyTarget(replyBase + splitHeading).node == nil)
let splitWithCopy = splitHeading + [node("AXButton", "Copy", 3)]
assert(CFEqual(replyTarget(replyBase + splitWithCopy).node!.el, splitWithCopy.last!.el))
// Actual WKWebView reproduction: presentation divs are ignored. Text, headings,
// code Copy and the response's action buttons become peers under the web body.
let flatHead = [node("AXGroup", "body", 1), node("AXHeading", "ChatGPT said:", 2),
    node("AXStaticText", "Response paragraph", 2), node("AXButton", "Copy", 2),
    node("AXStaticText", "print('code fragment')", 2)]
let flatActions = [node("AXButton", "Copy", 2), node("AXButton", "Response feedback", 2),
    node("AXPopUpButton", "More actions", 2)]
let flat = replyBase + flatHead + flatActions + [node("AXStaticText", "timestamp", 2)]
assert(CFEqual(replyTarget(flat).node!.el, flatActions[0].el))
assert(replyTarget(replyBase + flatHead + Array(flatActions.dropFirst())).node == nil)
assert(replyTarget(replyBase + flatHead + [node("AXButton", "Copy", 2),
    node("AXStaticText", "more code content", 2), flatActions[2]]).node == nil)
assert(replyTarget(replyBase + flatHead + [node("AXButton", "Copy", 2)] + flatActions).node == nil)
assert(replyTarget(flat + [node("AXHeading", "You said:", 2)]).error == "no-answer")
assert(replyTarget(flat + [node("AXButton", "Stop", 2)]).error == "answer-not-ready")
// Exact roles/labels plus native geometry can associate separately wrapped controls.
func box(_ x: Double, _ y: Double = 100, _ w: Double = 24, _ h: Double = 24) -> CGRect {
    CGRect(x: x, y: y, width: w, height: h)
}
assert(sameReplyControlRow([box(0), box(70), box(102)]))
assert(sameReplyControlRow([box(102), box(0), box(70)]))
assert(!sameReplyControlRow([box(0), box(70)]))
assert(!sameReplyControlRow([box(0), box(70, 125), box(102)]))
assert(!sameReplyControlRow([box(0), box(70), box(350)]))
assert(!sameReplyControlRow([box(0), box(12), box(70)]))
assert(!sameReplyControlRow([box(0), box(70, 100, 24, 60), box(102)]))
assert(!sameReplyControlRow([box(0), box(70, 100, 0), box(102)]))
assert(!sameReplyControlRow([box(0), box(70, 100, 200), box(302)]))
assert(!sameReplyControlRow([box(0), box(Double.nan), box(102)]))
assert(!sameReplyControlRow([box(0), box(Double.infinity), box(102)]))
func wrappedFooter(copy: CGRect? = box(0), rate: CGRect? = box(70), branch: CGRect? = box(102),
                   branchLabel: String = "Fork chat from here") -> [Node] {
    [node("AXGroup", "toolbar", 1), node("AXGroup", "copy wrapper", 2),
     node("AXButton", "Copy", 3, frame: copy), node("AXStaticText", "Now", 2),
     node("AXGroup", "rating wrapper", 2), node("AXButton", "Rate response", 3, frame: rate),
     node("AXGroup", "branch wrapper", 2), node("AXButton", branchLabel, 3, frame: branch)]
}
let wrapped = wrappedFooter()
let header = [node("AXHeading", "ChatGPT said:", 1)]
assert(CFEqual(replyTarget(replyBase + header + wrapped).node!.el, wrapped[2].el))
for rejected in [wrappedFooter(copy: nil), wrappedFooter(branch: nil), wrappedFooter(branch: box(102, 140)),
                 wrappedFooter(copy: box(0, 70)), wrappedFooter(branch: box(350)),
                 wrappedFooter(branchLabel: "Rate response")] {
    assert(replyTarget(replyBase + header + rejected).error == "reply-action-row-unrecognized")
}
let duplicateCopy = [node("AXButton", "Copy", 2, frame: box(35))]
assert(replyTarget(replyBase + header + wrapped + duplicateCopy).error == "reply-copy-multiple")
assert(replyTarget(replyBase + header + wrapped + [node("AXHeading", "You said:", 1)]).error == "no-answer")
print("Desktop AX targeting, draft eligibility, matching, activity, search and latest-reply regressions passed")
'''
fixture += '\n' + function('collapse') + '\n' + function('panelOpeners') + '\n'
fixture += '\n'.join(function(name) for name in ['panelNodes', 'panelObstructed', 'panelRouteAvailable']) + '\n'
fixture += r'''
func panelButton(_ name: String, depth: Int = 1, enabled: Bool = true) -> Node {
 let el = FakeElement(role: "AXButton", text: name, children: [], enabled: enabled)
 return Node(el: el, role: "AXButton", text: name, pressable: true, depth: depth, labels: [name])
}
let openerLabels = ["Changes", "This branch"]
for name in ["Changes", "Changes +7 -2", "Changes -12", "This branch +0 -1", "Changes +1,234 -56", "Changes+1,234-56",
 "Changes +1,23,456 -7,890", "Changes + 1,234 − 56", "Changes +1.234 -56", "Changes +1 234 -56"] {
 assert(panelOpeners([panelButton(name)], labels: openerLabels).count == 1)
}
for name in ["Changes settings", "Changes +oops", "Toggle file diff", "Show files", "Changes +1 -2 extra", "Changes +1,234 settings", "Changes +1 -2 +3",
 "Changes +oops-56", "Changes +1,2,3", "Changes -1,234/56", "Changes file.txt"] {
 assert(panelOpeners([panelButton(name)], labels: openerLabels).isEmpty)
}
assert(panelOpeners([panelButton("Changes", enabled: false)], labels: openerLabels).isEmpty)
assert(panelOpeners([panelButton("Changes", enabled: false)], labels: openerLabels, enabledOnly: false).count == 1)
assert(panelOpeners([panelButton("Changes"), panelButton("Pin chat", depth: 2)], labels: openerLabels).isEmpty)
assert(panelOpeners([panelButton("Changes"), panelButton("Changes +7 -2")], labels: openerLabels).count == 2)
let rootArea = Node(el: FakeElement(role: "AXWebArea", text: "", children: []), role: "AXWebArea", text: "", pressable: false, depth: 0)
let appMode = panelButton("Mode: Codex")
let visibleLabels = ["Show files", "Hide files"]
func reviewAvailable(_ nodes: [Node]) -> Bool { panelRouteAvailable(nodes, modeLabel: "Mode: Codex", openLabels: openerLabels, visibleLabels: visibleLabels) }
assert(!reviewAvailable([rootArea, appMode])) // Codex mode alone is insufficient.
assert(reviewAvailable([rootArea, appMode, panelButton("Changes+0-0")])) // Zero changes is still a valid route.
assert(reviewAvailable([rootArea, appMode, panelButton("Show files")])) // An already-open panel remains available.
assert(!reviewAvailable([rootArea, appMode, panelButton("Changes", enabled: false)]))
assert(!reviewAvailable([rootArea, appMode, panelButton("Changes"), panelButton("Changes +1-0")]))
let previewArea = Node(el: FakeElement(role: "AXWebArea", text: "", children: []), role: "AXWebArea", text: "", pressable: false, depth: 1)
let browserControls = [previewArea, panelButton("Changes", depth: 2), panelButton("Show files", depth: 2)]
assert(!reviewAvailable([rootArea, appMode] + browserControls))
assert(reviewAvailable([rootArea, appMode, panelButton("Changes")] + browserControls))
let modal = Node(el: FakeElement(role: "AXGroup", text: "", children: [], attributes: ["AXSubrole": "AXApplicationDialog"]), role: "AXGroup", text: "", pressable: false, depth: 1)
assert(!reviewAvailable([rootArea, appMode, panelButton("Changes"), modal]))

'''
with tempfile.TemporaryDirectory(prefix='vizhi-ax-tests-') as tmp:
    script = Path(tmp) / 'main.swift'
    script.write_text(fixture)
    binary = Path(tmp) / 'tests'
    subprocess.run(['swiftc', str(script), '-o', str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
