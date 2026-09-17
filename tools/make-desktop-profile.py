#!/usr/bin/env python3
"""Rebuild Vizhi Desktop's two 3x3 profiles from its tracked profile structure.

Each binding names VizhiDesktop; identity agrees in all four package documents. The default
retains approvals, and Everyday is an explicit import outside the auto-import profiles folder.
The existing profile supplies only hardware geometry and service-owned dial bindings. Preview
metadata is rebuilt with desktop labels and glyphs. Outputs have deterministic ZIP timestamps.

Usage: python3 tools/make-desktop-profile.py
"""

import base64
import io
import json
import pathlib
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5"
DONOR = OUT  # tracked desktop geometry; never depend on generated terminal product packages

# Adaptive-profile identity. Rotated once for the mode-aware layout: the updater installs this as
# an additional profile and RETAINS the user-selected default and earlier profile, so existing user customization is never overwritten.
# Never regenerate for ordinary updates or Options+ will accumulate duplicate profiles.
GUID = "6CA374714642475581835B05D0E6F7AC"
EVERYDAY_GUID = "A4B07D949FB2461B941D80BEB3D77A28"
EVERYDAY_OUT = OUT.parent.parent / "optional-profiles/VizhiDesktop-Everyday.lp5"
APP = "@_vizhidesktop"
DISPLAY = "Vizhi Desktop"
PROFILE_DISPLAY = "Vizhi Adaptive 2"
PLUGIN = "VizhiDesktop"
BUNDLE = "com.openai.codex"
DESCRIPTION = "Conversations, app controls, and nine adaptive workflow favorites for ChatGPT and Codex."

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
    act("DesktopContextCommand", "primary"),     # 5  ┘ Search in ChatGPT · Changes in Codex
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
    act("DesktopComposerCommand", "send"),        # 7  submit the existing draft
    act("DesktopComposerCommand", "output"),      # 8  Copy Answer (when supported) · Changes
]

# Page 3 · adaptive workflows. The physical slots stay fixed; DesktopWorkflowCommand resolves
# each slot against the focused window's ChatGPT/Codex mode at render time and again at press time.
PAGE_THREE = [act("DesktopWorkflowCommand", f"slot_{slot}") for slot in range(1, 10)]


EVERYDAY_HOME = PAGE_ONE[:6] + [
    act("DesktopVoiceDraftCommand"),
    act("DesktopComposerCommand", "send"),
    act("DesktopControlCommand", "stop"),
]


def build_profile(out, guid, display_name, home) -> None:
    with zipfile.ZipFile(DONOR) as donor:
        entries = {i.filename: donor.read(i.filename) for i in donor.infolist() if not i.is_dir()}

    # --- ProfileInfo.json: identity + a single rebuilt page ---
    profile = json.loads(entries["ProfileInfo.json"])
    profile["name"] = guid
    profile["packageName"] = guid          # self-owning: installs refresh instead of skipping
    profile["displayName"] = display_name
    profile["description"] = DESCRIPTION
    profile["applicationName"] = APP
    profile["nativePluginName"] = PLUGIN

    for mode in profile["layout"]["layoutModes"]:
        for ws in mode["workspaces"]:
            pages = ws["pressPages"]
            layout_pages = [("Conversations", home), ("Controls", PAGE_TWO), ("Workflows", PAGE_THREE)]
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
    app_info["defaultProfileName"] = guid
    entries["ApplicationInfo.json"] = json.dumps(app_info, indent=2).encode()

    # --- metadata/LoupedeckPackage.yaml: the fourth GUID location, shipped CORRECT here ---
    yaml_text = entries["metadata/LoupedeckPackage.yaml"].decode()
    fixed = []
    for line in yaml_text.splitlines():
        if line.startswith("name:"):
            fixed.append(f"name: {guid}")
        elif line.startswith("displayName:"):
            fixed.append(f"displayName: {display_name}")
        else:
            fixed.append(line)
    entries["metadata/LoupedeckPackage.yaml"] = ("\n".join(fixed) + "\n").encode()

    # --- metadata/AdvancedInfo.json: name OUR plugin, not the donor's ---
    entries["metadata/AdvancedInfo.json"] = json.dumps(
        {"additionalPluginNames": [PLUGIN]}).encode()

    # Cosmetic preview has the home-page controls; live faces are rendered by the plugin.
    labels = ["Conversation 1", "Conversation 2", "Conversation 3", "All Chats", "New Chat",
              "Search / Changes"] + (["Voice Draft · DRAFT", "Send", "Stop"] if home == EVERYDAY_HOME
                                      else ["Approve", "Deny", "Voice · SEND"])
    icons = ["all_chats", "all_chats", "all_chats", "all_chats", "new_claude", "explore"] + (
        ["voice_draft", "enter", "stop"] if home == EVERYDAY_HOME else ["yes_idle", "no_idle", "voice"])
    icon_dir = ROOT / "src/Products/VizhiDesktop/Resources/desktop_icons"
    def icon_bytes(icon):
        path = icon_dir / f"{icon}.png"
        if not path.exists():
            path = ROOT / "src/Core/Resources/icons" / f"{icon}.png"
        return path.read_bytes()

    entries["metadata/ProfilePreview.json"] = json.dumps({
        "buttonPages": [{"controlId": i, "actionName": binding, "displayName": label,
                         "description": "Live desktop action; follows the active conversation and mode.",
                         "image": base64.b64encode(icon_bytes(icon)).decode()}
                        for i, (binding, label, icon) in enumerate(zip(home, labels, icons))],
        "encoderPages": []}, indent=2).encode()

    # --- write (store, like the originals) ---
    out.parent.mkdir(parents=True, exist_ok=True)
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED) as z:
        for name, data in entries.items():
            z.writestr(zipfile.ZipInfo(name, date_time=(2026, 9, 17, 0, 0, 0)), data)
    out.write_bytes(buf.getvalue())

    bound = sum(1 for b in home + PAGE_TWO + PAGE_THREE if b)
    print(f"wrote {out.relative_to(ROOT)}: 3 pages, {bound} bound keys, GUID {guid}")


def main() -> None:
    build_profile(OUT, GUID, PROFILE_DISPLAY, PAGE_ONE)
    build_profile(EVERYDAY_OUT, EVERYDAY_GUID, "Vizhi Everyday", EVERYDAY_HOME)


if __name__ == "__main__":
    main()
