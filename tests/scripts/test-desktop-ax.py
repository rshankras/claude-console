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
// Minimal AX tree facade for executing the real scanner without live app access.
struct FakeElement {
    let role: String
    let text: String
    let children: [FakeElement]
}
typealias AXUIElement = FakeElement
let kAXRoleAttribute = "AXRole"
let kAXPressAction = "AXPress"
let MAX_DEPTH = 40
let MAX_NODES = 1500
func str(_ el: FakeElement, _ name: String) -> String? { el.role }
func children(_ el: FakeElement) -> [FakeElement] { el.children }
func displayText(_ el: FakeElement) -> String { el.text }
func actionNames(_ el: FakeElement) -> [String] { ["AXPress"] }
struct Node {
    let el: AXUIElement
    let role: String
    let text: String
    let pressable: Bool
    let depth: Int
}
let original = FakeElement(role: "AXWebArea", text: "original composer", children: [])
let other = FakeElement(role: "AXWebArea", text: "other draft", children: [])
var focusedWindows = [original]
func targetWindows() -> [AXUIElement] { focusedWindows }
'''
fixture += next(line for line in source.splitlines() if line.startswith('let operationWindows =')) + '\n'
fixture += function('scanWindows') + '\n'
fixture += function('conversationMatches') + '\n' + function('conversationState')
fixture += '''
func row(_ text: String, _ depth: Int = 0) -> Node {
    Node(el: original, role: "AXButton", text: text, pressable: true, depth: depth)
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
print("Desktop AX window targeting, matching, and activity regressions passed")
'''
with tempfile.TemporaryDirectory(prefix='vizhi-ax-tests-') as tmp:
    script = Path(tmp) / 'main.swift'
    script.write_text(fixture)
    binary = Path(tmp) / 'tests'
    subprocess.run(['swiftc', str(script), '-o', str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
