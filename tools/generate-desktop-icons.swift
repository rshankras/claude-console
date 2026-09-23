// generate-desktop-icons.swift — derives Vizhi Desktop's icon set from the shared glyphs.
//
// Shared glyphs retain the Claude/Codex family shape; desktop controls get semantic overrides. The
// solid #8593F8 is the average saturated blue-violet sampled from the Codex artwork bundled in
// the installed OpenAI desktop app. A solid survives the 90x90 OLED better than its gradient.
//
// Usage: swift tools/generate-desktop-icons.swift [--ux-only] [--only=send]
import AppKit

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let source = root.appendingPathComponent("src/Core/Resources/icons")
let output = root.appendingPathComponent("src/Products/VizhiDesktop/Resources/desktop_icons")
let onlyIcon = CommandLine.arguments.first { $0.hasPrefix("--only=") }.map { String($0.dropFirst(7)) }
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
    .filter { $0.pathExtension.lowercased() == "png" && !CommandLine.arguments.contains("--ux-only") }
    .filter { onlyIcon == nil || $0.deletingPathExtension().lastPathComponent == onlyIcon }
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

// Desktop-only symbols share the designer pack's 43-unit geometry. --ux-only avoids touching
// the established PNG set while iterating these additions.
for (svg, name) in [("DesktopStop", "stop"), ("DesktopSwitchMode", "switch_mode"),
                    ("DesktopWriting", "writing"), ("DesktopCopy", "copy"),
                    ("DesktopVoiceChat", "voice_chat"), ("VoiceDictation", "voice_draft"),
                    ("DesktopAllChats", "all_chats"),
                    ("DesktopQuickChat", "quick_chat"), ("DesktopNewChat", "new_chat"),
                    ("DesktopSearch", "search"), ("DesktopScheduled", "scheduled"),
                    ("DesktopAttach", "attach"), ("DesktopRewrite", "rewrite"),
                    ("DesktopPlan", "plan"), ("DesktopSend", "send"),
                    ("DesktopContinue", "continue"), ("DesktopMore", "more")] {
    if let onlyIcon, name != onlyIcon { continue }
    let path = root.appendingPathComponent("assets/designer-icons/White/\(svg).svg")
    let text = try String(contentsOf: path, encoding: .utf8).replacingOccurrences(of: "white", with: "#8593F8")
    guard let data = text.data(using: .utf8), let image = NSImage(data: data) else { fatalError("Invalid SVG: \(svg)") }
    let target = NSImage(size: NSSize(width: 96, height: 96))
    target.lockFocus()
    image.draw(in: NSRect(x: 0, y: 0, width: 96, height: 96))
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
          let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("Cannot render: \(svg)") }
    try png.write(to: output.appendingPathComponent("\(name).png"))
    rendered += 1
}

// Pending approvals keep their identity glyph and risk badge; idle glyphs are visibly grey.
for name in ["yes", "no", "search", "diff", "project", "security", "model", "attach",
             "scheduled", "create_pr", "explore", "quick_chat", "send", "copy", "status", "voice"] {
    if let onlyIcon, name != onlyIcon { continue }
    guard let image = NSImage(contentsOf: output.appendingPathComponent("\(name).png")) else {
        fatalError("Missing approval glyph: \(name)")
    }
    let target = NSImage(size: image.size)
    target.lockFocus()
    image.draw(in: NSRect(origin: .zero, size: image.size))
    NSColor(srgbRed: 0.35, green: 0.35, blue: 0.38, alpha: 1).set()
    NSRect(origin: .zero, size: image.size).fill(using: .sourceAtop)
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
          let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("Cannot render idle: \(name)") }
    try png.write(to: output.appendingPathComponent("\(name)_idle.png"))
    rendered += 1
}

print("desktop icons: \(rendered) rendered (identity #8593F8; unavailable controls grey)")
