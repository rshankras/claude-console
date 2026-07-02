// generate-plugin-icon.swift — renders the Claude Console plugin icon (256×256 PNG).
// A warm-amber terminal prompt "❯_" with a sparkle, on a dark rounded square.
// Usage: swift generate-plugin-icon.swift <output.png>
import AppKit

let outPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Icon256x256.png"
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

let amber = hex("f59e0b")

// --- prompt chevron "❯" (thick stroked polyline, vertex pointing right) ---
let chev = NSBezierPath()
chev.move(to: NSPoint(x: 74, y: 172))
chev.line(to: NSPoint(x: 126, y: 128))
chev.line(to: NSPoint(x: 74, y: 84))
chev.lineWidth = 26
chev.lineCapStyle = .round
chev.lineJoinStyle = .round
amber.setStroke()
chev.stroke()

// --- cursor underscore (rounded bar to the right of the chevron) ---
let cursor = NSBezierPath(roundedRect: NSRect(x: 142, y: 82, width: 64, height: 20), xRadius: 10, yRadius: 10)
amber.setFill()
cursor.fill()

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
hex("fde68a").setFill()
sparkle(NSPoint(x: 194, y: 190), outer: 26, inner: 7).fill()

NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: outPath))
print("wrote \(outPath)")
