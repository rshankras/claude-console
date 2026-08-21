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
}

# Voice is NOT dropped. It is agent-neutral — the helper records, whisper transcribes, and the text
# lands in whichever session has focus — and this package now embeds the payload, so the keys work.


# Codex frees two slots (Tab, Cost) and a hole on page 1 is the one the user stares at. Esc moves
# down beside Yes and No — yes, no, escape are the three ways to answer an approval prompt, so they
# belong on one row — which frees the slot next to Voice for Voice Draft: the same capture, but it
# types the transcript WITHOUT submitting, so you can fix whatever whisper misheard.
#
# Keyed by (page index, control id). Each entry declares what it expects to overwrite, so a change
# to Claude Console's layout upstream fails here loudly instead of silently shipping a keypad whose
# keys have quietly moved.
# The Screenshot key takes Clear's page 1 slot — it feeds the CURRENT conversation, which earns a
# front-page key the way a reset verb does not. Cost's freed slot goes to Review, Codex's own
# first-class verb ("/review" opens the picker: uncommitted / base branch / commit) — a slot that
# opened because Codex lacks a Claude number is exactly where a Codex-only verb belongs. Clear is
# demoted to the last corner of page 2, present but out of the way of habit.
PLACE = {
    (0, 3): ("ControlCommand___clear", "ScreenshotCommand"),
    (0, 5): ("ControlCommand___esc", "VoiceDraftCommand"),
    (0, 8): (None, "ControlCommand___esc"),
    (1, 0): (None, "ControlCommand___review"),
    (1, 8): (None, "ControlCommand___clear"),
}

ACTION = "Loupedeck.ClaudeConsolePlugin.Actions."


def rearrange(profile) -> int:
    """Move keys the Codex layout places differently. Returns how many were placed."""
    placed = 0
    for mode in profile["layout"]["layoutModes"]:
        for workspace in mode.get("workspaces", []):
            for pi, page in enumerate(workspace.get("pressPages", [])):
                for control in page.get("controls", []):
                    key = (pi, control.get("controlId"))
                    if key not in PLACE:
                        continue
                    expected, wanted = PLACE[key]
                    actual = control.get("pressAction")
                    actual_short = actual.split("___", 1)[1].replace(ACTION, "") if actual else None
                    if actual_short != expected:
                        raise SystemExit(
                            f"page {pi} control {key[1]}: expected {expected!r} to move, found "
                            f"{actual_short!r} — Claude Console's layout changed, so fix PLACE.")
                    control["pressAction"] = f"${PLUGIN}___{ACTION}{wanted}"
                    print(f"   placed {wanted} at page {pi + 1} key {key[1] + 1}")
                    placed += 1
    return placed


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
                rearrange(doc)
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
