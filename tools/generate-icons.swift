// generate-icons.swift — renders SF Symbols to colored-on-transparent PNGs for Keypad key faces.
// Usage: swift generate-icons.swift <output-dir>
import AppKit

// Bright palette that reads well on a dark key. (name, SF Symbol, hex tint)
let G = "22c55e", R = "ef4444", A = "f59e0b", B = "60a5fa", P = "a78bfa", Y = "94a3b8"
let icons: [(String, String, String)] = [
    ("voice", "mic.fill", P),
    ("esc", "hand.raised.fill", R),                 // red stop-hand — "interrupt/stop"; distinct from No's red ✕ (xmark.circle.fill)
    ("clear", "trash.fill", A),
    ("exit", "power", R),
    ("plan", "switch.2", P),                         // "Mode" key (action id still "plan") — Shift+Tab mode cycler
    ("tab", "arrow.right.to.line", Y),               // Tab+Enter — accept autocomplete & submit (distinct from Mode's Shift+Tab)
    ("compact", "arrow.down.right.and.arrow.up.left", Y),
    ("context", "doc.text.fill", Y),
    ("model", "sparkles", P),
    ("deploy", "shippingbox.fill", G),
    ("commit", "checkmark.seal.fill", A),
    ("diff", "plusminus", A),
    ("push", "arrow.up.circle.fill", A),
    ("create_pr", "arrow.triangle.branch", A),
    ("status", "info.circle.fill", A),
    ("log", "list.bullet", A),
    ("fix_bug", "ant.fill", B),
    ("write_tests", "checklist", B),
    ("explore", "binoculars.fill", B),
    ("explain", "text.bubble.fill", B),
    ("refactor", "arrow.triangle.2.circlepath", B),
    ("review", "eye.fill", B),
    ("optimize", "bolt.fill", B),
    ("security", "lock.shield.fill", B),
    ("document", "text.book.closed.fill", B),
    ("cost", "dollarsign.circle.fill", G),
    ("terminal", "terminal.fill", Y),
    ("new_tab", "plus.square.fill", Y),
    ("next_tab", "arrow.right.circle.fill", Y),
    ("prev_tab", "arrow.left.circle.fill", Y),
    ("new_claude", "plus.bubble.fill", Y),
    ("project", "folder.fill", B),
    // Answer keys — respond when Claude prompts a question (basenames match AnswerCommand params).
    ("yes", "checkmark.circle.fill", G),
    ("no", "xmark.circle.fill", R),
    ("up", "arrowtriangle.up.fill", Y),
    ("down", "arrowtriangle.down.fill", Y),
    ("enter", "return", G),
    // Scroll keys — page back/forward through the conversation (basenames match ScrollCommand params).
    // Arrow-to-line reads as "page/jump", clearly distinct from the solid Answer arrowtriangles above.
    ("scroll_up", "arrow.up.to.line", B),
    ("scroll_down", "arrow.down.to.line", B),
    // Live-display face icon (Context usage key) + amber/red variants for the fill warning (#2).
    ("gauge", "gauge.medium", B),
    ("gauge_warn", "gauge.medium", A),   // 75%+  — getting full
    ("gauge_crit", "gauge.medium", R),   // 90%+  — compact soon
    // Model cycle fallback brain — shown before the live model is known.
    ("brain", "brain", P),
    // Activity status key (#3): working / needs-you / idle.
    ("busy", "hourglass", B),
    ("busy0", "hourglass.tophalf.filled", B),     // animated "Working" — sand flips top↔bottom
    ("busy1", "hourglass.bottomhalf.filled", B),
    ("waiting", "bell.badge.fill", R),
    ("done", "circle.fill", G),          // ready/idle status dot — distinct from the Yes checkmark
]

// Coloured model brains — rendered IN COLOUR (not white) so each tier is distinguishable at a
// glance. Basenames brain_<id> are used by the Model key's live display (ModelCycleCommand) to
// tint the current-model brain (opus / sonnet / haiku).
let coloredIcons: [(String, String, String)] = [
    ("brain_haiku",  "brain", G),  // fast      → green
    ("brain_sonnet", "brain", B),  // balanced  → blue
    ("brain_opus",   "brain", P),  // top tier  → purple
]

// Voice "listening" animation: equalizer frames VoiceCommand cycles while recording. Hand-drawn
// bars (not an SF Symbol) so cycling them reads as live, bouncing audio. Heights are fractions of
// the bar area; rows are arranged so consecutive frames look like levels jumping around.
let waveFrames: [(String, [CGFloat])] = [
    ("wave0", [0.25, 0.55, 1.00, 0.55, 0.25]),
    ("wave1", [0.60, 1.00, 0.40, 0.85, 0.50]),
    ("wave2", [1.00, 0.40, 0.70, 0.30, 0.90]),
    ("wave3", [0.45, 0.85, 0.30, 1.00, 0.60]),
]

func color(_ hex: String) -> NSColor {
    var v: UInt64 = 0
    Scanner(string: hex).scanHexInt64(&v)
    return NSColor(srgbRed: CGFloat((v >> 16) & 0xff) / 255, green: CGFloat((v >> 8) & 0xff) / 255, blue: CGFloat(v & 0xff) / 255, alpha: 1)
}

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let size: CGFloat = 96
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

func render(_ name: String, _ symbol: String, _ tint: NSColor) -> Bool {
    let cfg = NSImage.SymbolConfiguration(pointSize: 78, weight: .semibold)
    guard let base = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?.withSymbolConfiguration(cfg) else {
        return false
    }
    let target = NSImage(size: NSSize(width: size, height: size))
    target.lockFocus()
    let bs = base.size
    base.draw(in: NSRect(x: (size - bs.width) / 2, y: (size - bs.height) / 2, width: bs.width, height: bs.height))
    tint.set()
    NSRect(x: 0, y: 0, width: size, height: size).fill(using: .sourceAtop)
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else { return false }
    try? png.write(to: URL(fileURLWithPath: "\(outDir)/\(name).png"))
    return true
}

// Draw a row of rounded vertical bars (an equalizer frame) at the given height fractions.
func renderBars(_ name: String, _ heights: [CGFloat], _ tint: NSColor) -> Bool {
    let target = NSImage(size: NSSize(width: size, height: size))
    target.lockFocus()
    let n = CGFloat(heights.count)
    let gap = size * 0.05
    let barW = (size * 0.66 - gap * (n - 1)) / n
    let startX = (size - (barW * n + gap * (n - 1))) / 2
    let maxH = size * 0.70
    tint.set()
    for (i, frac) in heights.enumerated() {
        let h = max(barW, maxH * frac)                 // floor at bar width → rounded "dot" when quiet
        let x = startX + CGFloat(i) * (barW + gap)
        let rect = NSRect(x: x, y: (size - h) / 2, width: barW, height: h)
        NSBezierPath(roundedRect: rect, xRadius: barW / 2, yRadius: barW / 2).fill()
    }
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else { return false }
    try? png.write(to: URL(fileURLWithPath: "\(outDir)/\(name).png"))
    return true
}

var ok: [String] = [], fail: [String] = []
for (name, symbol, hex) in icons {               // category icons → semantic colour (grouped by family)
    if render(name, symbol, color(hex)) { ok.append(name) } else { fail.append("\(name)(\(symbol))") }
}
for (name, symbol, hex) in coloredIcons {        // model brains → coloured
    if render(name, symbol, color(hex)) { ok.append(name) } else { fail.append("\(name)(\(symbol))") }
}
for (name, hs) in waveFrames {                    // voice listening equalizer → green
    if renderBars(name, hs, color(G)) { ok.append(name) } else { fail.append(name) }
}
print("OK(\(ok.count)): \(ok.joined(separator: ", "))")
print("FAIL(\(fail.count)): \(fail.joined(separator: ", "))")
