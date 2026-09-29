#!/usr/bin/env python3
"""Exercise the production shortcut guard with synthetic PIDs and an inert event sink."""
from pathlib import Path
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[2] / 'tools/desktop/VizhiAxBridge.swift').read_text()

def function(name):
    start = source.index('func ' + name + '(')
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

fixture = 'import CoreGraphics\n' + function('shortcutFlags') + '\n' + function('dispatchShortcut')
fixture += '''
assert(shortcutFlags("control,shift") == [.maskControl, .maskShift])
for invalid in ["", "shift", "option", "control,", "control,control", "control,unknown"] {
    assert(shortcutFlags(invalid) == nil)
}
var events: [(Int32, Bool)] = []
for foreground: Int32? in [nil, 123, 0] {
    assert(!dispatchShortcut(targetPid: 456, frontmostPid: foreground, post: { events.append(($0, $1)) }))
}
assert(!dispatchShortcut(targetPid: 0, frontmostPid: 0, post: { events.append(($0, $1)) }))
assert(events.isEmpty)
var frontmost: Int32? = 456
assert(dispatchShortcut(targetPid: 456, frontmostPid: frontmost, post: { pid, down in
    events.append((pid, down))
    frontmost = 123 // a focus change can never route key-up to another app
}))
assert(events.count == 2)
assert(events[0].0 == 456 && events[0].1)
assert(events[1].0 == 456 && !events[1].1)
print("Shortcut modifier and PID dispatch tests passed; no events posted")
'''
shortcut = source[source.index('if verb == "shortcut" {'):source.index('let appEl = AXUIElementCreateApplication')]
assert '.postToPid(pid)' in shortcut
assert '.post(tap:' not in shortcut and '.activate(' not in shortcut
assert 'scanWindows()' not in shortcut and 'forceAccessibility(' not in shortcut
with tempfile.TemporaryDirectory(prefix='vizhi-shortcut-test-') as temp:
    path = Path(temp) / 'test.swift'
    path.write_text(fixture)
    subprocess.run(['swift', str(path)], check=True)
