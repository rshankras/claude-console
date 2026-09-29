// generate-plugin-icon.swift — renders a product's plugin icon (256×256 PNG).
// A terminal prompt "❯_" on a dark rounded square, optionally with a sparkle.
//
// Usage: swift generate-plugin-icon.swift <output.png> [accentHex] [sparkleHex|none] [terminal|window]
//   Claude Console : accent f59e0b, sparkle fde68a
//   Vizhi for Codex: accent 4fd1c5, sparkle none
//   Vizhi Desktop  : accent a78bfa, sparkle none, shape window
//
// The sparkle is a deliberate per-product choice, not decoration: it echoes Anthropic's mark, so a
// product driving a different vendor's agent must not carry it. The chevron is what makes the
// family read as one; the accent is what tells them apart on the app strip.
//
// The SHAPE says which surface the product drives, and it is not decoration either: a bare prompt
// means a terminal, a window frame means a desktop app. Putting a TTY prompt on the product that
// drives a GUI would be the icon telling the same lie the key faces are built to avoid.
import AppKit

let outPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Icon256x256.png"
let accentHex = CommandLine.arguments.count > 2 ? CommandLine.arguments[2] : "f59e0b"
let sparkleArg = CommandLine.arguments.count > 3 ? CommandLine.arguments[3] : "fde68a"
let shape = CommandLine.arguments.count > 4 ? CommandLine.arguments[4].lowercased() : "terminal"
let size: CGFloat = 256

func hex(_ h: String, _ a: CGFloat = 1) -> NSColor {
    var v: UInt64 = 0; Scanner(string: h).scanHexInt64(&v)
    return NSColor(srgbRed: CGFloat((v >> 16) & 0xff) / 255,
                   green: CGFloat((v >> 8) & 0xff) / 255,
                   blue: CGFloat(v & 0xff) / 255, alpha: a)
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size),
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
let ctx = NSGraphicsContext.current!.cgContext
ctx.setAllowsAntialiasing(true)
ctx.interpolationQuality = .high

// --- background: dark rounded square with a subtle vertical gradient ---
let inset: CGFloat = 6
let rect = NSRect(x: inset, y: inset, width: size - 2 * inset, height: size - 2 * inset)
let bg = NSBezierPath(roundedRect: rect, xRadius: 54, yRadius: 54)
NSGraphicsContext.saveGraphicsState()
bg.addClip()
NSGradient(colors: [hex("2a2a3d"), hex("13131c")])!.draw(in: rect, angle: -90)
NSGraphicsContext.restoreGraphicsState()
// hairline edge so it reads on light backgrounds too
hex("4a4a5e").setStroke(); bg.lineWidth = 2; bg.stroke()

let amber = hex(accentHex)

// The chevron is the family mark; only its size and placement change with the shape.
func chevron(tipX: CGFloat, midY: CGFloat, arm: CGFloat, width: CGFloat) -> NSBezierPath {
    let p = NSBezierPath()
    p.move(to: NSPoint(x: tipX - arm, y: midY + arm))
    p.line(to: NSPoint(x: tipX, y: midY))
    p.line(to: NSPoint(x: tipX - arm, y: midY - arm))
    p.lineWidth = width
    p.lineCapStyle = .round
    p.lineJoinStyle = .round
    return p
}

if shape == "window" {
    // --- a window frame: this product drives a desktop app, not a TTY ---
    let win = NSRect(x: 50, y: 62, width: 156, height: 132)
    let frame = NSBezierPath(roundedRect: win, xRadius: 18, yRadius: 18)
    amber.setStroke()
    frame.lineWidth = 12
    frame.stroke()

    // title bar: a rule plus two dots, the universal shorthand for a window chrome
    let barY = win.maxY - 34
    let bar = NSBezierPath()
    bar.move(to: NSPoint(x: win.minX + 6, y: barY))
    bar.line(to: NSPoint(x: win.maxX - 6, y: barY))
    bar.lineWidth = 10
    bar.stroke()
    amber.setFill()
    for dx in [CGFloat(26), CGFloat(52)] {
        NSBezierPath(ovalIn: NSRect(x: win.minX + dx - 6, y: barY + 12, width: 12, height: 12)).fill()
    }

    // the chevron, inside the window
    amber.setStroke()
    chevron(tipX: 152, midY: barY - 46, arm: 34, width: 20).stroke()
} else {
    // --- prompt chevron "❯" (thick stroked polyline, vertex pointing right) ---
    amber.setStroke()
    chevron(tipX: 126, midY: 128, arm: 44, width: 26).stroke()

    // --- cursor underscore (rounded bar to the right of the chevron) ---
    let cursor = NSBezierPath(roundedRect: NSRect(x: 142, y: 82, width: 64, height: 20), xRadius: 10, yRadius: 10)
    amber.setFill()
    cursor.fill()
}

// --- sparkle, top-right (4-point concave star) ---
func sparkle(_ c: NSPoint, outer R: CGFloat, inner r: CGFloat) -> NSBezierPath {
    let p = NSBezierPath()
    for i in 0..<8 {
        let ang = Double(i) * .pi / 4
        let rad = (i % 2 == 0) ? R : r
        let pt = NSPoint(x: c.x + CGFloat(cos(ang)) * rad, y: c.y + CGFloat(sin(ang)) * rad)
        i == 0 ? p.move(to: pt) : p.line(to: pt)
    }
    p.close(); return p
}
if sparkleArg.lowercased() != "none" {
    hex(sparkleArg).setFill()
    sparkle(NSPoint(x: 194, y: 190), outer: 26, inner: 7).fill()
}

NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: outPath))
print("wrote \(outPath)")
