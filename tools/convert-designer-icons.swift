// convert-designer-icons.swift — renders the designer SVG set (assets/designer-icons/Colours,
// variant "B" of the July 2026 icon pack) into the 96px PNGs the plugin embeds.
//
// Owns the icons listed in `mapping` + `recolors` below; tools/generate-icons.swift owns the
// REST (state faces, wave frames, and icons the pack doesn't cover). The two sets are disjoint
// on purpose — run either script without stepping on the other's output.
//
// Recolors: the pack ships one colour per glyph. Threshold/model variants (gauge_warn/crit,
// brain_haiku/sonnet/opus) are produced by swapping the SVG's fill hex before rendering, so the
// designer's glyph stays the single source of truth.
//
// Usage: swift tools/convert-designer-icons.swift            (from the repo root)
import AppKit

let repo = FileManager.default.currentDirectoryPath
let srcDir = repo + "/assets/designer-icons/Colours"
let outDir = repo + "/src/Resources/icons"

// Designer palette (sampled from the pack itself).
let GREEN = "#7FC17A", RED = "#CE655C", AMBER = "#DFA658", BLUE = "#81A8ED", PURPLE = "#A194EB"

// SVG basename -> embedded icon basename (see preview sheet for the intended action).
let mapping: [(String, String)] = [
    ("Brain", "brain"),                    // Model key fallback (brain_* variants below)
    ("Branch", "create_pr"),
    ("Bug", "fix_bug"),
    ("Build project", "refactor"),
    ("Chevron-Small-Down", "down"),
    ("Chevron-Small-Up", "up"),
    ("Delete", "clear"),
    ("Enter", "enter"),
    ("Escape", "esc"),
    ("Exit", "exit"),
    ("Explain", "explain"),
    ("Explore", "explore"),
    ("ExposureLayer", "diff"),
    ("File", "document"),
    ("GitCommit", "commit"),
    ("GitPush", "push"),
    ("GoToFolder", "project"),
    ("Info", "status"),
    ("ListTool", "log"),
    ("Money", "cost"),
    ("Multi-toggleOff", "plan"),           // the Mode key (action id is still "plan")
    ("NewBrowserTab", "new_tab"),
    ("NewPresentation", "new_claude"),
    ("NextTab(Right)", "next_tab"),
    ("Optimize", "optimize"),
    ("PasteInsert", "write_tests"),
    ("PreviousTab(Left)", "prev_tab"),
    ("Radiobutton-Check", "yes"),
    ("Remove", "no"),
    ("ScrollDown", "scroll_down"),
    ("ScrollUp", "scroll_up"),
    ("Security", "security"),
    ("Show", "review"),
    ("Shrink Selection", "compact"),
    ("SmartActions", "done"),              // ready state (Activity key + session faces)
    ("Speed", "gauge"),                    // context gauge, normal fill
    ("Tab", "tab"),
    ("VoiceDictation", "voice"),
]

// (source SVG, output name, fill hex) — colour variants of a designer glyph.
let recolors: [(String, String, String)] = [
    ("Speed", "gauge_warn", AMBER),        // context 75%+
    ("Speed", "gauge_crit", RED),          // context 90%+
    ("Brain", "brain_haiku", GREEN),       // fast
    ("Brain", "brain_sonnet", BLUE),       // balanced
    ("Brain", "brain_opus", PURPLE),       // top tier
]

let size: CGFloat = 96

func renderSvg(_ svgText: String, to path: String) -> Bool {
    guard let data = svgText.data(using: .utf8), let img = NSImage(data: data) else { return false }
    let target = NSImage(size: NSSize(width: size, height: size))
    target.lockFocus()
    img.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else { return false }
    try? png.write(to: URL(fileURLWithPath: path))
    return true
}

// Replace every fill hex in the SVG with `hex` (keeps fill="none"/"white" markers intact).
func recolor(_ svgText: String, to hex: String) -> String {
    var out = ""
    var rest = Substring(svgText)
    while let r = rest.range(of: "fill=\"#") {
        out += rest[..<r.lowerBound] + "fill=\"" + hex + "\""
        let afterHash = rest[r.upperBound...]
        rest = afterHash.drop(while: { $0 != "\"" }).dropFirst()
    }
    return out + rest
}

// Compose voice_draft IN the designer's language: their VoiceDictation mic (scaled, right) plus
// three rounded wave bars whose width matches the pack's stroke weight (~3.2 units on a 43 grid
// ≈ 7px at 96). The pack predates the Voice Draft key, so this is the one icon built from
// designer parts rather than shipped whole.
func renderVoiceDraft(micSvg: String, to path: String) -> Bool {
    guard let data = micSvg.data(using: .utf8), let mic = NSImage(data: data) else { return false }
    let target = NSImage(size: NSSize(width: size, height: size))
    target.lockFocus()
    mic.draw(in: NSRect(x: 34, y: 6, width: 66, height: 66))   // right-of-centre, slightly low
    var v: UInt64 = 0
    Scanner(string: String(PURPLE.dropFirst())).scanHexInt64(&v)
    NSColor(srgbRed: CGFloat((v >> 16) & 0xff) / 255, green: CGFloat((v >> 8) & 0xff) / 255,
            blue: CGFloat(v & 0xff) / 255, alpha: 1).set()
    let barW: CGFloat = 7
    for (x, h) in [(CGFloat(10), CGFloat(30)), (23, 52), (36, 38)] {
        NSBezierPath(roundedRect: NSRect(x: x, y: (size - h) / 2, width: barW, height: h),
                     xRadius: barW / 2, yRadius: barW / 2).fill()
    }
    target.unlockFocus()
    guard let tiff = target.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else { return false }
    try? png.write(to: URL(fileURLWithPath: path))
    return true
}

var ok: [String] = [], fail: [String] = []
if let micText = try? String(contentsOfFile: srcDir + "/VoiceDictation.svg", encoding: .utf8),
   renderVoiceDraft(micSvg: micText, to: outDir + "/voice_draft.png")
{
    ok.append("voice_draft")
}
else
{
    fail.append("voice_draft")
}
for (svg, name) in mapping {
    let svgPath = srcDir + "/" + svg + ".svg"
    guard let text = try? String(contentsOfFile: svgPath, encoding: .utf8) else { fail.append(name + "(missing \(svg).svg)"); continue }
    if renderSvg(text, to: outDir + "/" + name + ".png") { ok.append(name) } else { fail.append(name) }
}
for (svg, name, hex) in recolors {
    let svgPath = srcDir + "/" + svg + ".svg"
    guard let text = try? String(contentsOfFile: svgPath, encoding: .utf8) else { fail.append(name + "(missing \(svg).svg)"); continue }
    if renderSvg(recolor(text, to: hex), to: outDir + "/" + name + ".png") { ok.append(name) } else { fail.append(name) }
}
print("OK(\(ok.count)): \(ok.joined(separator: ", "))")
print("FAIL(\(fail.count)): \(fail.joined(separator: ", "))")
