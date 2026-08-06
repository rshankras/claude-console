// generate-icons.swift — renders SF Symbols to colored-on-transparent PNGs for Keypad key faces.
//
// Owns ONLY the icons the designer pack doesn't cover — everything else is rendered by
// tools/convert-designer-icons.swift from assets/designer-icons/. The two sets are disjoint:
// re-running this script must never overwrite a designer icon.
//
// Usage: swift generate-icons.swift <output-dir>
import AppKit

// The DESIGNER's palette (sampled from the pack), so these SF-generated stragglers sit in the
// same colour family as the designer icons around them.
let G = "7FC17A", R = "CE655C", A = "DFA658", B = "81A8ED", P = "A194EB", Y = "9DA5B7"
let icons: [(String, String, String)] = [
    // (voice_draft is composed from designer parts — see convert-designer-icons.swift)
    ("context", "doc.text.fill", Y),                 // legacy basename, kept for old bindings
    ("model", "sparkles", P),                        // legacy basename, kept for old bindings
    ("deploy", "shippingbox.fill", G),               // 10th prompt key — absent from the pack
    ("terminal", "terminal.fill", Y),
    // Window nav — solid TRIANGLES in squares. Differs from the tab keys' line-arrows-in-circles on
    // BOTH the inner glyph (▶ vs →) and the outer shape (square vs circle), so window ≠ tab even on a
    // tiny dark key (a plain arrow.*.square just re-drew the tab arrow in a near-invisible square).
    ("new_claude_window", "macwindow.badge.plus", Y),
    ("next_window", "arrowtriangle.right.square.fill", Y),
    ("prev_window", "arrowtriangle.left.square.fill", Y),
    // Activity status key: working / needs-you (the ready state is the designer "done" icon).
    ("busy", "hourglass", B),
    ("busy0", "hourglass.tophalf.filled", B),     // animated "Working" — sand flips top↔bottom
    ("busy1", "hourglass.bottomhalf.filled", B),
    ("waiting", "bell.badge.fill", R),
]

// No coloured brains here any more — brain and brain_haiku/sonnet/opus are designer recolors
// (see convert-designer-icons.swift).
let coloredIcons: [(String, String, String)] = []

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
