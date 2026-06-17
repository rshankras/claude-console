// generate-icons.swift — renders SF Symbols to colored-on-transparent PNGs for Keypad key faces.
// Usage: swift generate-icons.swift <output-dir>
import AppKit

// Bright palette that reads well on a dark key. (name, SF Symbol, hex tint)
let G = "22c55e", R = "ef4444", A = "f59e0b", B = "60a5fa", P = "a78bfa", Y = "94a3b8"
let icons: [(String, String, String)] = [
    ("voice", "mic.fill", P),
    ("accept", "checkmark", G),
    ("reject", "xmark", R),
    ("esc", "xmark.octagon.fill", R),
    ("clear", "trash.fill", A),
    ("exit", "power", R),
    ("plan", "checklist", P),
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
]

func color(_ hex: String) -> NSColor {
    var v: UInt64 = 0
    Scanner(string: hex).scanHexInt64(&v)
    return NSColor(srgbRed: CGFloat((v >> 16) & 0xff) / 255, green: CGFloat((v >> 8) & 0xff) / 255, blue: CGFloat(v & 0xff) / 255, alpha: 1)
}

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."
let size: CGFloat = 96
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

var ok: [String] = [], fail: [String] = []
for (name, symbol, hex) in icons {
    _ = hex // monochrome for now; swap NSColor.white below to color(hex) to re-enable per-category colour
    let cfg = NSImage.SymbolConfiguration(pointSize: 78, weight: .semibold)
    guard let base = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?.withSymbolConfiguration(cfg) else {
        fail.append("\(name)(\(symbol))"); continue
    }
    let target = NSImage(size: NSSize(width: size, height: size))
    target.lockFocus()
    let bs = base.size
    base.draw(in: NSRect(x: (size - bs.width) / 2, y: (size - bs.height) / 2, width: bs.width, height: bs.height))
    NSColor.white.set()
    NSRect(x: 0, y: 0, width: size, height: size).fill(using: .sourceAtop)
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else { fail.append(name); continue }
    try? png.write(to: URL(fileURLWithPath: "\(outDir)/\(name).png"))
    ok.append(name)
}
print("OK(\(ok.count)): \(ok.joined(separator: ", "))")
print("FAIL(\(fail.count)): \(fail.joined(separator: ", "))")
