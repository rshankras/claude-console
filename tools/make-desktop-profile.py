#!/usr/bin/env python3
"""Build Vizhi Desktop's packaged keypad profile from Claude Console's as a structural donor.

Same three laws as make-codex-profile.py, which this follows:
  1. Key bindings name the plugin that owns them ($VizhiDesktop___...); anything else is a key
     that renders and does nothing.
  2. Identity is the GUID, not the display string — and it lives in FOUR places (ProfileInfo
     name + packageName, ApplicationInfo defaultProfileName, metadata yaml name). VizhiCodex's
     package ships with the fourth still carrying the donor GUID (latent dedupe hazard); this
     product ships all four correct.
  3. Keys for things the product does not have are DROPPED, not disabled. The desktop product
     shares no key with the terminal layout, so every donor binding is dropped and one page is
     rebuilt from scratch; donor pages 2-5 are removed outright.

Deliberately inherited from the donor: metadata/ProfilePreview.json (Options+ cosmetic preview
only — carries donor action names, same known-cosmetic state VizhiCodex ships with) and the
generic dial adjustment + its ActionIcons entry (the service-owned mouse-wheel default).

Usage: python3 tools/make-desktop-profile.py
Reads  src/Products/ClaudeConsole/package/profiles/DefaultProfile70.lp5
Writes src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5
"""

import io
import json
import pathlib
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
DONOR = ROOT / "src/Products/ClaudeConsole/package/profiles/DefaultProfile70.lp5"
OUT = ROOT / "src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5"

# Adaptive-profile identity. Rotated once for the mode-aware layout: the updater installs this as
# a new default and RETAINS the earlier profile, so existing user customization is never overwritten.
# Never regenerate for ordinary updates or Options+ will accumulate duplicate profiles.
GUID = "F2C5D1F769AD4BE19B5902EF3309213A"
APP = "@_vizhidesktop"
DISPLAY = "Vizhi Desktop"
PROFILE_DISPLAY = "Vizhi Adaptive"
PLUGIN = "VizhiDesktop"
BUNDLE = "com.openai.codex"
DESCRIPTION = "Codex agent controls for the ChatGPT desktop app."

NS = "Loupedeck.ClaudeConsolePlugin.DesktopActions"


def act(type_name, param=None):
    base = f"${PLUGIN}___{NS}.{type_name}"
    return f"{base}___{param}" if param else base


def folder(type_name):
    """The SDK's reserved binding shape for a PluginDynamicFolder command."""
    full_name = f"{NS}.{type_name}"
    return f"${PLUGIN}___#DynamicFolder___DynamicFolder#{full_name}"


# Page 1 began as the appendix's six-conversation layout. Hardware review reduced it to three
# larger conversation keys and moved overflow into All Chats; the approval row remains fixed so
# the terminal and desktop products keep the same muscle memory.
#
# The default-profile guidance still stands and is unchanged by this: an application profile is
# only active while its app is frontmost, so answering-from-anywhere still means placing
# Activity/Approve/Deny/Show ChatGPT on the user's default profile (README step one). This page
# is the "at the app" home; that placement is the "everywhere else" one.
# Revised to THREE conversations by the product owner after seeing six on hardware
# (2026-08-25): three larger, scannable identities beat six near-identical ones, and the
# freed middle row makes page 1 a complete cockpit. Deviates from the appendix drawing —
# flag in the next Logitech design update.
PAGE_ONE = [
    act("DesktopConversationCommand", "1"),       # 0  ┐ top row: the three most recent
    act("DesktopConversationCommand", "2"),       # 1  │ conversations, stable positions,
    act("DesktopConversationCommand", "3"),       # 2  ┘ state faces, press to jump
    folder("AllChatsDynamicFolder"),              # 3  ┐ overflow: every AX-visible conversation
    act("DesktopControlCommand", "new_chat"),    # 4  │ start work; status already lives in cards
    act("DesktopContextCommand", "primary"),     # 5  ┘ Search in ChatGPT · Files in Codex
    act("DesktopApprovalCommand", "approve"),     # 6  ┐
    act("DesktopApprovalCommand", "deny"),        # 7  │ the bottom row answers
    act("DesktopVoiceCommand"),                   # 8  ┘
]

# Page 2 · the session controls — the appendix's "Actions" page.
PAGE_TWO = [
    act("DesktopControlCommand", "mode"),         # 0
    act("DesktopControlCommand", "stop"),         # 1
    act("DesktopVoiceDraftCommand"),              # 2  transcribe, review, send yourself
    act("DesktopContextCommand", "secondary_1"), # 3  Projects · Permissions
    act("DesktopContextCommand", "secondary_2"), # 4  Plugins · Attach Files
    act("DesktopContextCommand", "secondary_3"), # 5  Scheduled · Pull Requests
    act("DesktopContextCommand", "secondary_4"), # 6  Explore · Quick Chat
    None,                                         # 7
    None,                                         # 8
]

# Page 3 · adaptive workflows. The physical slots stay fixed; DesktopWorkflowCommand resolves
# each slot against the focused window's ChatGPT/Codex mode at render time and again at press time.
PAGE_THREE = [act("DesktopWorkflowCommand", f"slot_{slot}") for slot in range(1, 10)]


def main() -> None:
    donor = zipfile.ZipFile(DONOR)
    entries = {i.filename: donor.read(i.filename) for i in donor.infolist() if not i.is_dir()}

    # --- ProfileInfo.json: identity + a single rebuilt page ---
    profile = json.loads(entries["ProfileInfo.json"])
    profile["name"] = GUID
    profile["packageName"] = GUID          # self-owning: installs refresh instead of skipping
    profile["displayName"] = PROFILE_DISPLAY
    profile["description"] = DESCRIPTION
    profile["applicationName"] = APP
    profile["nativePluginName"] = PLUGIN

    for mode in profile["layout"]["layoutModes"]:
        for ws in mode["workspaces"]:
            pages = ws["pressPages"]
            layout_pages = [("Conversations", PAGE_ONE), ("Actions", PAGE_TWO), ("Workflows", PAGE_THREE)]
            if len(pages) < len(layout_pages):
                sys.exit(f"donor has fewer than {len(layout_pages)} press pages — wrong donor?")
            kept = []
            for page, (name, bindings) in zip(pages, layout_pages):
                page["displayName"] = name
                if len(page["controls"]) != len(bindings):
                    sys.exit(f"donor page has {len(page['controls'])} controls, expected {len(bindings)}")
                for control, binding in zip(page["controls"], bindings):
                    control["pressAction"] = binding
                kept.append(page)
            ws["pressPages"] = kept        # remaining donor pages: dropped, not blanked

    entries["ProfileInfo.json"] = json.dumps(profile, indent=2).encode()

    # --- ApplicationInfo.json: the registration document ---
    app_info = json.loads(entries["ApplicationInfo.json"])
    app_info["name"] = APP
    app_info["displayName"] = DISPLAY
    app_info["description"] = DESCRIPTION
    app_info["nativePluginName"] = PLUGIN
    app_info["processOrBundleName"] = BUNDLE
    app_info["defaultProfileName"] = GUID
    entries["ApplicationInfo.json"] = json.dumps(app_info, indent=2).encode()

    # --- metadata/LoupedeckPackage.yaml: the fourth GUID location, shipped CORRECT here ---
    yaml_text = entries["metadata/LoupedeckPackage.yaml"].decode()
    fixed = []
    for line in yaml_text.splitlines():
        if line.startswith("name:"):
            fixed.append(f"name: {GUID}")
        elif line.startswith("displayName:"):
            fixed.append(f"displayName: {PROFILE_DISPLAY}")
        else:
            fixed.append(line)
    entries["metadata/LoupedeckPackage.yaml"] = ("\n".join(fixed) + "\n").encode()

    # --- metadata/AdvancedInfo.json: name OUR plugin, not the donor's ---
    entries["metadata/AdvancedInfo.json"] = json.dumps(
        {"additionalPluginNames": [PLUGIN]}).encode()

    # --- write (store, like the originals) ---
    OUT.parent.mkdir(parents=True, exist_ok=True)
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED) as z:
        for name, data in entries.items():
            z.writestr(name, data)
    OUT.write_bytes(buf.getvalue())

    bound = sum(1 for b in PAGE_ONE + PAGE_TWO + PAGE_THREE if b)
    print(f"wrote {OUT.relative_to(ROOT)}: 3 pages, {bound} bound keys, GUID {GUID}")


if __name__ == "__main__":
    main()
