// generate-icons.swift — renders the animated listening frames for Keypad key faces.
//
// Every static icon now comes from the designer SVG pipeline (including matching custom SVGs for
// optional actions absent from the delivered pack). Keeping SF Symbols here would let a future
// regeneration silently restore a second visual language. This script owns only wave0...wave3.
//
// Usage: swift generate-icons.swift <output-dir>
import AppKit

let GREEN = "7FC17A" // recording/listening is the design's positive live state

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
for (name, hs) in waveFrames {                    // voice listening equalizer → green
    if renderBars(name, hs, color(GREEN)) { ok.append(name) } else { fail.append(name) }
}
print("OK(\(ok.count)): \(ok.joined(separator: ", "))")
print("FAIL(\(fail.count)): \(fail.joined(separator: ", "))")
