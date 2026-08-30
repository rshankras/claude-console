// convert-designer-icons.swift — renders the designer SVG set (assets/designer-icons/Colours,
// variant "B" of the July 2026 icon pack) into the 96px PNGs the plugin embeds.
//
// Owns every static icon listed in `mapping` + `recolors` below. Where the delivered pack has no
// glyph, a small SVG in the same 43-unit copper line language lives beside it in White/. The only
// assets left to tools/generate-icons.swift are the animated listening bars.
//
// Recolors: the pack ships one colour per glyph. Threshold/model variants (gauge_warn/crit,
// brain_haiku/sonnet/opus) are produced by swapping the SVG's fill hex before rendering, so the
// designer's glyph stays the single source of truth.
//
// Usage: swift tools/convert-designer-icons.swift            (from the repo root)
import AppKit

let repo = FileManager.default.currentDirectoryPath
// Neutral action/nav glyphs render from the WHITE set — one monochrome colour, the designer's
// 2026-08 direction (a dedicated White/ variant was delivered alongside Colours/). The colour
// variants (gauge warn/crit and brain tiers) render from Colours/, whose hex fills `recolor`
// can swap; a White SVG (fill="white") is deliberately left untouched by recolor.
let whiteDir = repo + "/assets/designer-icons/White"
let coloursDir = repo + "/assets/designer-icons/Colours"
let outDir = repo + "/src/Core/Resources/icons"   // #39: the embedded-resource path (was src/Resources/icons)

// Designer palette (sampled from the pack itself).
let GREEN = "#7FC17A", RED = "#CE655C", AMBER = "#DFA658", BLUE = "#81A8ED", PURPLE = "#A194EB"
// Claude identity copper — the neutral action/nav glyphs render in this, matching the 2026-08
// design frames (icons copper, colour reserved for state). The White SVGs are tinted to it below.
let COPPER = "#CC7C5E"

// SVG basename -> embedded icon basename (see preview sheet for the intended action).
let mapping: [(String, String)] = [
    ("Brain", "brain"),                    // Model key fallback (brain_* variants below)
    ("BusyBottom", "busy1"),              // Activity animation, frame 2
    ("BusyTop", "busy0"),                 // Activity animation, frame 1
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
    ("Cost", "cost"),
    ("Deploy", "deploy"),
    ("Multi-toggleOff", "plan"),           // the Mode key (action id is still "plan")
    ("NewBrowserTab", "new_tab"),
    ("NewPresentation", "new_claude"),
    ("NextTab(Right)", "next_tab"),
    ("Optimize", "optimize"),
    ("PasteInsert", "write_tests"),
    ("PreviousTab(Left)", "prev_tab"),
    ("ScrollDown", "scroll_down"),
    ("ScrollUp", "scroll_up"),
    ("Security", "security"),
    ("Show", "review"),
    ("Show", "review_core"),              // native review uses the approved eye language too
    ("Screenshot", "screenshot"),
    ("Compact", "compact"),
    ("SmartActions", "done"),              // ready state (Activity key + session faces)
    ("Speed", "gauge"),                    // context gauge, normal fill
    ("Tab", "tab"),
    ("Terminal", "terminal"),
    ("VoiceDictation", "voice"),
    ("Waiting", "waiting"),
    ("WindowAdd", "new_claude_window"),
    ("WindowNext", "next_window"),
    ("WindowPrevious", "prev_window"),
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

// Tint a WHITE glyph (fill="white") to `hex`. The White SVGs are single-fill masks, so this is
// how the neutral set takes the copper identity colour. fill="none" backgrounds are left alone.
func tintWhite(_ svgText: String, to hex: String) -> String {
    return svgText
        .replacingOccurrences(of: "fill=\"white\"", with: "fill=\"" + hex + "\"")
        .replacingOccurrences(of: "stroke=\"white\"", with: "stroke=\"" + hex + "\"")
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
    NSColor(srgbRed: 0xCC / 255.0, green: 0x7C / 255.0, blue: 0x5E / 255.0, alpha: 1).set()  // copper bars, matching the copper mic
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
if let micText = try? String(contentsOfFile: whiteDir + "/VoiceDictation.svg", encoding: .utf8),
   renderVoiceDraft(micSvg: tintWhite(micText, to: COPPER), to: outDir + "/voice_draft.png")
{
    ok.append("voice_draft")
}
else
{
    fail.append("voice_draft")
}
for (svg, name) in mapping {
    let svgPath = whiteDir + "/" + svg + ".svg"
    guard let text = try? String(contentsOfFile: svgPath, encoding: .utf8) else { fail.append(name + "(missing \(svg).svg)"); continue }
    // Neutral glyphs take the copper identity colour (the White mask tinted to COPPER).
    if renderSvg(tintWhite(text, to: COPPER), to: outDir + "/" + name + ".png") { ok.append(name) } else { fail.append(name) }
}
for (svg, name, hex) in recolors {
    let svgPath = coloursDir + "/" + svg + ".svg"
    guard let text = try? String(contentsOfFile: svgPath, encoding: .utf8) else { fail.append(name + "(missing \(svg).svg)"); continue }
    if renderSvg(recolor(text, to: hex), to: outDir + "/" + name + ".png") { ok.append(name) } else { fail.append(name) }
}
print("OK(\(ok.count)): \(ok.joined(separator: ", "))")
print("FAIL(\(fail.count)): \(fail.joined(separator: ", "))")
