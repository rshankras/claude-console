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

# The product's permanent identity. Minted 2026-08-25; never regenerate — the service dedupes
# imports by this GUID, and a changed GUID re-imports as a second profile on every update.
GUID = "EF7972524F2B4BEABD3B7D8BD57DB350"
APP = "@_vizhidesktop"
DISPLAY = "Vizhi Desktop"
PLUGIN = "VizhiDesktop"
BUNDLE = "com.openai.codex"
DESCRIPTION = "Codex agent controls for the ChatGPT desktop app."

NS = "Loupedeck.ClaudeConsolePlugin.DesktopActions"


def act(type_name, param=None):
    base = f"${PLUGIN}___{NS}.{type_name}"
    return f"{base}___{param}" if param else base


# The nine keys of page 1 (controlId 0-8: top row first). Status glances, Approve/Deny answer,
# the middle row controls, Voice speaks, Focus is the one deliberate window-bringer.
PAGE_ONE = [
    act("DesktopStatusCommand"),                  # 0
    act("DesktopApprovalCommand", "approve"),     # 1
    act("DesktopApprovalCommand", "deny"),        # 2
    act("DesktopControlCommand", "stop"),         # 3
    act("DesktopControlCommand", "mode"),         # 4
    act("DesktopControlCommand", "new_chat"),     # 5
    act("DesktopVoiceCommand"),                   # 6
    act("DesktopControlCommand", "focus"),        # 7
    None,                                         # 8 spare
]


def main() -> None:
    donor = zipfile.ZipFile(DONOR)
    entries = {i.filename: donor.read(i.filename) for i in donor.infolist() if not i.is_dir()}

    # --- ProfileInfo.json: identity + a single rebuilt page ---
    profile = json.loads(entries["ProfileInfo.json"])
    profile["name"] = GUID
    profile["packageName"] = GUID          # self-owning: installs refresh instead of skipping
    profile["displayName"] = DISPLAY
    profile["description"] = DESCRIPTION
    profile["applicationName"] = APP
    profile["nativePluginName"] = PLUGIN

    for mode in profile["layout"]["layoutModes"]:
        for ws in mode["workspaces"]:
            pages = ws["pressPages"]
            if not pages:
                sys.exit("donor has no press pages — wrong donor?")
            page = pages[0]
            page["displayName"] = "Agent"
            if len(page["controls"]) != len(PAGE_ONE):
                sys.exit(f"donor page 1 has {len(page['controls'])} controls, expected {len(PAGE_ONE)}")
            for control, binding in zip(page["controls"], PAGE_ONE):
                control["pressAction"] = binding
            ws["pressPages"] = [page]      # pages 2-5: dropped, not blanked

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
            fixed.append(f"displayName: {DISPLAY}")
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

    bound = sum(1 for b in PAGE_ONE if b)
    print(f"wrote {OUT.relative_to(ROOT)}: 1 page, {bound} bound keys, GUID {GUID}")


if __name__ == "__main__":
    main()
