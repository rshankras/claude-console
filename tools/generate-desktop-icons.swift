// generate-desktop-icons.swift — derives Vizhi Desktop's icon set from the shared glyphs.
//
// The shape stays identical to the Claude/Codex keypad family; only the colour changes. The
// solid #8593F8 is the average saturated blue-violet sampled from the Codex artwork bundled in
// the installed OpenAI desktop app. A solid survives the 90x90 OLED better than its gradient.
//
// Usage: swift tools/generate-desktop-icons.swift
import AppKit

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let source = root.appendingPathComponent("src/Core/Resources/icons")
let output = root.appendingPathComponent("src/Products/VizhiDesktop/Resources/desktop_icons")
let codexBlue = NSColor(
    srgbRed: CGFloat(0x85) / 255,
    green: CGFloat(0x93) / 255,
    blue: CGFloat(0xF8) / 255,
    alpha: 1)

try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

let files = try FileManager.default.contentsOfDirectory(
    at: source,
    includingPropertiesForKeys: nil,
    options: [.skipsHiddenFiles])
    .filter { $0.pathExtension.lowercased() == "png" }
    .sorted { $0.lastPathComponent < $1.lastPathComponent }

var rendered = 0
for file in files {
    guard let image = NSImage(contentsOf: file) else {
        fputs("error: cannot read \(file.path)\n", stderr)
        exit(1)
    }

    let target = NSImage(size: image.size)
    target.lockFocus()
    image.draw(in: NSRect(origin: .zero, size: image.size))
    codexBlue.set()
    NSRect(origin: .zero, size: image.size).fill(using: .sourceAtop)
    target.unlockFocus()

    guard let tiff = target.tiffRepresentation,
          let bitmap = NSBitmapImageRep(data: tiff),
          let png = bitmap.representation(using: .png, properties: [:]) else {
        fputs("error: cannot render \(file.lastPathComponent)\n", stderr)
        exit(1)
    }

    try png.write(to: output.appendingPathComponent(file.lastPathComponent))
    rendered += 1
}

print("desktop icons: \(rendered) rendered in #8593F8")
