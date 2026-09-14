#!/usr/bin/env python3
"""Render the first-page preview thumbnails of the keypad profiles in the current design.

An LP5 carries a static preview strip (metadata/ProfilePreview.json: nine 116x116 PNGs) that
Options+ shows in its profile picker. Options+ renders it when a profile is *created* there and
never again — an imported profile keeps whatever strip it was shipped with. Ours was captured
in the 2.0 era (blue hourglasses, coloured icons, a "Voice" key), so after the 2026-08 restyle
the picker advertised the old design while the keypad rendered the new one.

This script draws the nine thumbnails from the same sources the plugin renders from — the
embedded icon PNGs in src/Core/Resources/icons, the state palette, the session face and the
Yes/No tile geometry in KeyImage.cs — and writes them into all four downloadable profiles, keeping
every other zip member byte for byte. Run it after tools/sync-default-profiles.py and
tools/make-codex-profile.py (which own the bindings and preview order) whenever the design or the
first page changes:

    python3 tools/render-profile-preview.py            # from the repository root
    python3 tools/render-profile-preview.py --sheet out.png   # also write a contact sheet

Requires Pillow (python3 -m pip install pillow).
"""

from __future__ import annotations

import base64
import io
import json
import os
import sys
import tempfile
import zipfile

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS = os.path.join(ROOT, "src", "Core", "Resources", "icons")
CODEX_ICONS = os.path.join(ROOT, "src", "Products", "VizhiCodex", "Resources", "icons_codex")
# Geometry measured from the Options+-rendered strip: a 116x116 canvas, black, with the key
# tile as a 75x75 square at x=20, y=0 and the label centred below it.
CANVAS = 116
TILE_X, TILE_Y, TILE = 20, 0, 75
LABEL_Y = 86

# Palette — KeyImage.cs. Colour is reserved for state; the neutral glyphs are copper PNGs.
BLACK = (0, 0, 0, 255)
EDGE = (42, 42, 46, 255)          # a hairline so a black tile still reads as a key on the strip
WHITE = (255, 255, 255, 255)
LABEL = (165, 165, 165, 255)      # the strip's own label grey, sampled from the old thumbnails
GREEN = (0x4F, 0xA9, 0x75, 255)   # Allow
RED = (0xDA, 0x3D, 0x29, 255)     # Deny
GRAY = (0x5A, 0x5A, 0x60, 255)    # quiet session bar
CODEX_BLUE = (0x81, 0xA8, 0xED, 255)  # selected Vizhi session / Codex identity

# (actionName suffix, strip label, renderer, argument) in first-page order — the order
# tools/sync-default-profiles.py writes; the renderers below mirror KeyImage's three faces.
CLAUDE_FIRST_PAGE = [
    ("SessionSlotCommand___1", "Session 1", "session", "Session 1"),
    ("SessionSlotCommand___2", "Session 2", "session", "Session 2"),
    ("SessionSlotCommand___3", "Session 3", "session", "Session 3"),
    ("ControlCommand___clear", "Clear", "icon", ("clear", 0.70)),
    ("AnswerCommand___no", "No", "tile", (RED, "deny", "No")),
    ("AnswerCommand___yes", "Yes", "tile", (GREEN, "allow", "Yes")),
    ("ControlCommand___esc", "Esc", "icon", ("esc", 0.70)),
    ("ControlCommand___tab", "Tab", "icon", ("tab", 0.70)),
    ("VoiceCommand", "Dictate", "icon", ("voice", 0.82)),
]

VIZHI_FIRST_PAGE = [
    # A static profile preview cannot know the live route, so show one representative selected
    # session. On hardware this blue follows whichever Codex session the user actually selects.
    ("SessionSlotCommand___1", "Session 1", "session", ("Session 1", CODEX_BLUE)),
    ("SessionSlotCommand___2", "Session 2", "session", ("Session 2", GRAY)),
    ("SessionSlotCommand___3", "Session 3", "session", ("Session 3", GRAY)),
    ("ScreenshotCommand", "Screenshot", "icon", ("screenshot", 0.70, CODEX_ICONS)),
    ("VoiceCommand", "Dictate", "icon", ("voice", 0.82, CODEX_ICONS)),
    ("VoiceDraftCommand", "Draft", "icon", ("voice_draft", 0.78, CODEX_ICONS)),
    ("ControlCommand___esc", "Esc", "icon", ("esc", 0.70, CODEX_ICONS)),
    ("AnswerCommand___no", "No", "tile", (RED, "deny", "No")),
    ("AnswerCommand___yes", "Yes", "tile", (GREEN, "allow", "Yes")),
]

PROFILE_PAGES = [
    (os.path.join(ROOT, "profiles", "ClaudeConsole-Keypad.lp5"), CLAUDE_FIRST_PAGE),
    (os.path.join(ROOT, "profiles", "ClaudeConsole-Windows.lp5"), CLAUDE_FIRST_PAGE),
    (os.path.join(ROOT, "profiles", "VizhiCodex-Keypad.lp5"), VIZHI_FIRST_PAGE),
    (os.path.join(ROOT, "profiles", "VizhiCodex-Windows.lp5"), VIZHI_FIRST_PAGE),
]


def font(size: int) -> ImageFont.FreeTypeFont:
    for candidate in (
        "/Library/Fonts/SF-Compact.ttf",
        "/System/Library/Fonts/SFNS.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ):
        if os.path.exists(candidate):
            try:
                return ImageFont.truetype(candidate, size)
            except OSError:
                continue
    return ImageFont.load_default()


def centred_text(draw: ImageDraw.ImageDraw, text: str, cx: float, cy: float, size: int, fill) -> None:
    f = font(size)
    left, top, right, bottom = draw.textbbox((0, 0), text, font=f)
    draw.text((cx - (right - left) / 2 - left, cy - (bottom - top) / 2 - top), text, font=f, fill=fill)


def icon(name: str, px: int, directory: str = ICONS) -> Image.Image:
    return Image.open(os.path.join(directory, name + ".png")).convert("RGBA").resize((px, px), Image.LANCZOS)


def tile_canvas() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    im = Image.new("RGBA", (CANVAS, CANVAS), BLACK)
    return im, ImageDraw.Draw(im)


def render_icon(label: str, arg) -> Image.Image:
    """KeyImage.Render: black face, the copper glyph large and centred, no in-tile label."""
    name, share, *directory = arg
    im, draw = tile_canvas()
    draw.rectangle((TILE_X, TILE_Y, TILE_X + TILE - 1, TILE_Y + TILE - 1), fill=BLACK, outline=EDGE)
    px = int(TILE * share)
    glyph = icon(name, px, directory[0] if directory else ICONS)
    im.alpha_composite(glyph, (TILE_X + (TILE - px) // 2, TILE_Y + (TILE - px) // 2))
    centred_text(draw, label, CANVAS / 2, LABEL_Y + 6, 13, LABEL)
    return im


def render_tile(label: str, arg) -> Image.Image:
    """KeyImage.RenderDecisionTile: solid state colour, white circled glyph at half the tile
    width centred a little above the middle, the label inside the tile."""
    colour, name, word = arg
    im, draw = tile_canvas()
    draw.rectangle((TILE_X, TILE_Y, TILE_X + TILE - 1, TILE_Y + TILE - 1), fill=colour)
    px = int(TILE * 0.50)
    glyph = icon(name, px)
    im.alpha_composite(glyph, (TILE_X + (TILE - px) // 2, TILE_Y + int(TILE * 0.42) - px // 2))
    centred_text(draw, word, TILE_X + TILE / 2, TILE_Y + TILE * 0.80, 11, WHITE)
    centred_text(draw, label, CANVAS / 2, LABEL_Y + 6, 13, LABEL)
    return im


def render_session(label: str, arg) -> Image.Image:
    """KeyImage.RenderSessionSlot: the name centred in the top 75%, a state bar flush along the
    bottom 25%. Session specs may provide a selected-bar colour; otherwise the bar is quiet grey."""
    if isinstance(arg, tuple):
        name, bar_colour = arg
    else:
        name, bar_colour = arg, GRAY
    im, draw = tile_canvas()
    draw.rectangle((TILE_X, TILE_Y, TILE_X + TILE - 1, TILE_Y + TILE - 1), fill=BLACK, outline=EDGE)
    title_h = int(TILE * 0.75)
    centred_text(draw, name, TILE_X + TILE / 2, TILE_Y + title_h / 2, 12, WHITE)
    draw.rectangle((TILE_X, TILE_Y + title_h, TILE_X + TILE - 1, TILE_Y + TILE - 1), fill=bar_colour)
    centred_text(draw, "Complete", TILE_X + TILE / 2, TILE_Y + title_h + (TILE - title_h) / 2, 10, WHITE)
    centred_text(draw, label, CANVAS / 2, LABEL_Y + 6, 13, LABEL)
    return im


RENDERERS = {"icon": render_icon, "tile": render_tile, "session": render_session}


def png_base64(im: Image.Image) -> str:
    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return base64.b64encode(buf.getvalue()).decode("ascii")


def read_preview(path: str) -> dict:
    with zipfile.ZipFile(path) as archive:
        return json.loads(archive.read("metadata/ProfilePreview.json"))


def write_preview(path: str, preview: dict) -> None:
    """Replace one JSON entry while preserving every other LP5 member and its metadata."""
    payload = json.dumps(preview, indent=2).encode("utf-8")
    directory = os.path.dirname(path)
    with tempfile.NamedTemporaryFile(dir=directory, suffix=".lp5", delete=False) as temporary:
        temporary_path = temporary.name
    try:
        with zipfile.ZipFile(path) as source, zipfile.ZipFile(
            temporary_path, "w", zipfile.ZIP_DEFLATED
        ) as target:
            for item in source.infolist():
                data = payload if item.filename == "metadata/ProfilePreview.json" else source.read(item.filename)
                target.writestr(item, data)
        os.replace(temporary_path, path)
    finally:
        if os.path.exists(temporary_path):
            os.unlink(temporary_path)


def main() -> None:
    sheet_path = None
    if "--sheet" in sys.argv:
        sheet_path = sys.argv[sys.argv.index("--sheet") + 1]

    sheets = []
    for path, page_spec in PROFILE_PAGES:
        thumbnails = [
            (action, label, RENDERERS[kind](label, arg))
            for action, label, kind, arg in page_spec
        ]
        preview = read_preview(path)
        by_action = {
            item["actionName"].split("Actions.", 1)[-1]: item for item in preview["buttonPages"]
        }
        missing = [a for a, _, _ in thumbnails if a not in by_action]
        if missing:
            raise SystemExit(f"{os.path.relpath(path, ROOT)}: preview is missing {', '.join(missing)} — run tools/sync-default-profiles.py first")
        for action, label, im in thumbnails:
            entry = by_action[action]
            entry["image"] = png_base64(im)
            entry["displayName"] = label
        write_preview(path, preview)
        print(f"rendered {len(thumbnails)} thumbnails into {os.path.relpath(path, ROOT)}")
        if not sheets:
            sheets = [im for _, _, im in thumbnails]

    if sheet_path:
        ims = sheets
        sheet = Image.new("RGBA", (sum(i.width for i in ims) + 8 * len(ims), CANVAS), (30, 30, 30, 255))
        x = 0
        for im in ims:
            sheet.paste(im, (x, 0), im)
            x += im.width + 8
        sheet.resize((sheet.width * 2, sheet.height * 2), Image.LANCZOS).save(sheet_path)
        print(f"sheet -> {sheet_path}")


if __name__ == "__main__":
    main()
