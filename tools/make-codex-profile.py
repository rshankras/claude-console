#!/usr/bin/env python3
"""Build the Codex keypad profile from Claude Console's.

The layout is deliberately the same — the two products do the same job — but a copied profile is
not simply a rename. Three things have to change or the keypad lies to the user:

  1. Key bindings name the plugin that owns them ("<PluginShortName>___<Type>___<param>"). Left
     alone, every key points at a plugin this package does not contain and does nothing.
  2. Identity is the GUID, not the display string, and the service dedupes by it. Sharing Claude
     Console's would collapse the two profiles into one.
  3. Keys for things Codex does not have must be DROPPED, not merely disabled in code. Gating an
     action so it never registers, while the profile still binds it, turns the key into an
     unresolvable binding — a live-looking key that cannot fire. The profile and the product have
     to agree.

Run from the repo root:  python3 tools/make-codex-profile.py
"""

import json
import os
import zipfile

SRC = "src/Products/ClaudeConsole/package/profiles/DefaultProfile70.lp5"
DST = "src/Products/VizhiCodex/package/profiles/DefaultProfile70.lp5"

GUID = "7B2C4E9A15D8436FA0C3E17D5B84962F"
APP = "@_codexconsole"
DISPLAY = "Vizhi for Codex"
PLUGIN = "VizhiCodex"

# Bindings to drop, and why each one does not apply to Codex.
DROP = {
    "ControlCommand___tab": "no completion to accept — the press would do nothing",
    "CostDisplayCommand": "Codex bills a subscription and reports no spend; the key would show a dash",
    "VoiceCommand": "this product ships no voice payload",
    "VoiceDraftCommand": "this product ships no voice payload",
    "ProjectVoiceCommand": "this product ships no voice payload",
}


def drops(action: str):
    for marker, reason in DROP.items():
        if marker in action:
            return reason
    return None


def strip_unsupported(profile) -> int:
    """Clear bindings Codex cannot honour. Returns how many keys were freed."""
    freed = 0
    for mode in profile["layout"]["layoutModes"]:
        for workspace in mode.get("workspaces", []):
            for page in workspace.get("pressPages", []):
                for control in page.get("controls", []):
                    action = control.get("pressAction") or ""
                    reason = drops(action)
                    if reason:
                        control["pressAction"] = None
                        freed += 1
                        print(f"   dropped {action.split('___')[-1] or action}: {reason}")
    return freed


def main() -> None:
    with zipfile.ZipFile(SRC) as zin, zipfile.ZipFile(DST, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)

            if item.filename == "ProfileInfo.json":
                text = data.decode("utf-8").replace("ClaudeConsole___", PLUGIN + "___")
                doc = json.loads(text)
                doc["name"] = GUID
                doc["packageName"] = GUID
                doc["displayName"] = DISPLAY
                doc["applicationName"] = APP
                doc["nativePluginName"] = PLUGIN
                freed = strip_unsupported(doc)
                print(f"   {freed} keys freed")
                data = json.dumps(doc, indent=2).encode("utf-8")

            elif item.filename == "ApplicationInfo.json":
                doc = json.loads(data)
                doc["name"] = APP
                doc["displayName"] = DISPLAY
                doc["description"] = "Codex CLI controls for Terminal.app."
                doc["nativePluginName"] = PLUGIN
                doc["defaultProfileName"] = GUID
                data = json.dumps(doc, indent=2).encode("utf-8")

            elif item.filename == "metadata/LoupedeckPackage.yaml":
                data = (data.decode("utf-8")
                        .replace("Claude Console", DISPLAY)
                        .replace("ClaudeConsole", PLUGIN)).encode("utf-8")

            zout.writestr(item, data)

    print(f"wrote {DST}")


if __name__ == "__main__":
    os.chdir(os.path.join(os.path.dirname(__file__), ".."))
    main()
