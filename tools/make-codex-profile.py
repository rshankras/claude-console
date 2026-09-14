#!/usr/bin/env python3
"""Build the Codex keypad profile from Claude Console's.

Both are DOWNLOADS, not package contents: the plugins are universal (#23), so neither package
carries a profile. Each .lp5 in profiles/ is a Terminal profile — bound to Options+'s own entry
for Terminal (com.apple.terminal), NOT to the plugin — with the product's actions placed on it.
The user imports it, or drags the actions on by hand; either way the binding stays Terminal's.

The layout is deliberately the same — the two products do the same job — but a copied profile is
not simply a rename. Three things have to change or the keypad lies to the user:

  1. Key bindings name the plugin that owns them ("<PluginShortName>___<Type>___<param>"). Left
     alone, every key points at a plugin this package does not contain and does nothing. The
     profile also LISTS the plugins it draws on (additionalNativePluginNames), and that list has
     to name this product.
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

DISPLAY = "Vizhi for Codex — Keypad"
PLUGIN = "VizhiCodex"

# One product, two host-terminal identities. These GUIDs are stable so importing an updated
# download refreshes the same profile rather than leaving another duplicate in Options+.
PROFILES = (
    (
        "profiles/ClaudeConsole-Keypad.lp5",
        "profiles/VizhiCodex-Keypad.lp5",
        "7B2C4E9A15D8436FA0C3E17D5B84962F",
    ),
    (
        "profiles/ClaudeConsole-Windows.lp5",
        "profiles/VizhiCodex-Windows.lp5",
        "90CC0B6D7FE0405AB63B311DCA7476B7",
    ),
)

# Bindings to drop, and why each one does not apply to Codex.
DROP = {
    "ControlCommand___tab": "no completion to accept — the press would do nothing",
    "CostDisplayCommand": "Codex bills a subscription and reports no spend; the key would show a dash",
}

# Voice is NOT dropped. It is agent-neutral — the helper records, whisper transcribes, and the text
# lands in whichever session has focus — and this package now embeds the payload, so the keys work.


# Codex frees two slots (Tab, Cost), but the first page must still be deliberate rather than merely
# inherit holes from Claude Console. Each page has one job:
#   1. sessions and interaction, with Esc / No / Yes matching the physical approval convention
#   2. Codex-native controls, grouped as configuration / conversation / inspection
#   3. reusable prompts, grouped as understand / change / verify
#   4. terminal creation and navigation, with menu arrows kept together
#   5. Git only — empty cells are more honest than unrelated filler controls
#
# Keyed by (page index, control id). Each entry declares what it expects to overwrite, so a change
# to Claude Console's layout upstream fails here loudly instead of silently shipping a keypad whose
# keys have quietly moved.
# The Screenshot key takes Clear's page 1 slot — it feeds the CURRENT conversation, which earns a
# front-page key the way a reset verb does not. Context is a live remaining-capacity gauge; the
# native Status, Clear and Scroll actions remain available for custom layouts without mixing their
# functions into the default Git page.
PLACE = {
    (0, 3): ("ControlCommand___clear", "ControlCommand___esc"),
    (0, 4): ("AnswerCommand___no", "AnswerCommand___no"),
    (0, 5): ("AnswerCommand___yes", "AnswerCommand___yes"),
    (0, 6): ("ControlCommand___esc", "ScreenshotCommand"),
    (0, 7): (None, "VoiceCommand"),
    (0, 8): ("VoiceCommand", "VoiceDraftCommand"),
    (1, 0): (None, "ModelCycleCommand"),
    (1, 1): ("ModelCycleCommand", "ControlCommand___plan_native"),
    (1, 2): ("ControlCommand___compact", "ControlCommand___skills"),
    (1, 3): ("AnswerCommand___up", "ControlCommand___review"),
    (1, 4): ("AnswerCommand___enter", "ContextCommand"),
    (1, 5): ("AnswerCommand___down", "ControlCommand___compact"),
    (1, 6): ("ScrollCommand___scroll_up", "AnswerCommand___up"),
    (1, 7): ("ScrollCommand___scroll_down", "AnswerCommand___enter"),
    (1, 8): (None, "AnswerCommand___down"),
    (2, 2): ("PromptCommand___review", "PromptCommand___document"),
    (2, 5): ("PromptCommand___write_tests", "PromptCommand___fix_bug"),
    (2, 6): ("PromptCommand___document", "PromptCommand___review"),
    (2, 7): ("PromptCommand___fix_bug", "PromptCommand___write_tests"),
    (3, 2): ("NavCommand___next_tab", "NavCommand___new_claude"),
    (3, 4): ("NavCommand___new_claude", "NavCommand___next_tab"),
    (3, 6): (None, "ControlCommand___agent"),
    (3, 7): (None, "ControlCommand___fork"),
    (3, 8): (None, "ControlCommand___resume"),
    (4, 0): ("GitCommand___commit", "GitCommand___status"),
    (4, 1): ("GitCommand___create_pr", "GitCommand___diff"),
    (4, 2): ("GitCommand___diff", "GitCommand___log"),
    (4, 3): ("GitCommand___log", "GitCommand___commit"),
    (4, 5): ("GitCommand___status", "GitCommand___create_pr"),
}

ACTION = "Loupedeck.ClaudeConsolePlugin.Actions."

PREVIEW_LABELS = {
    "ScreenshotCommand": "Screenshot",
    "VoiceCommand": "Dictate",
    "VoiceDraftCommand": "Draft",
    "AnswerCommand___yes": "Yes",
    "AnswerCommand___no": "No",
    "ControlCommand___esc": "Esc",
}


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


def first_page(profile) -> list[dict]:
    return profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0]["controls"]


def update_preview(preview, profile) -> None:
    """Make the static Options+ strip tell the same truth as the imported first page.

    The source preview has exactly nine entries, one per control. Reusing entries by position gives
    newly placed Codex actions a slot; render-profile-preview.py replaces their inherited source
    images with the right artwork immediately after this generator runs.
    """
    controls = {control["controlId"]: control for control in first_page(profile)}
    entries = {entry["controlId"]: entry for entry in preview["buttonPages"]}
    if sorted(controls) != list(range(9)) or sorted(entries) != list(range(9)):
        raise SystemExit("the first page and preview must both contain controls 0 through 8")

    for control_id in range(9):
        action = controls[control_id].get("pressAction")
        entry = entries[control_id]
        entry["actionName"] = action
        short = action.split("Actions.", 1)[-1] if action else ""
        if short in PREVIEW_LABELS:
            entry["displayName"] = PREVIEW_LABELS[short]
        if entry.get("description"):
            entry["description"] = entry["description"].replace("Claude", "Codex")


def build_profile(src: str, dst: str, guid: str) -> None:
    with zipfile.ZipFile(src) as zin:
        items = zin.infolist()
        files = {item.filename: zin.read(item.filename) for item in items}

    profile = json.loads(files["ProfileInfo.json"].decode("utf-8").replace("ClaudeConsole___", PLUGIN + "___"))
    profile["name"] = guid
    if profile.get("packageName"):
        profile["packageName"] = guid
    profile["displayName"] = DISPLAY
    # applicationName stays the host terminal's own entry; this profile USES the plugin, it is not
    # owned by an application registration from the universal plugin.
    profile["additionalNativePluginNames"] = [
        PLUGIN if name == "ClaudeConsole" else name
        for name in profile.get("additionalNativePluginNames", [])
    ]
    freed = strip_unsupported(profile)
    print(f"   {freed} keys freed")
    rearrange(profile)
    files["ProfileInfo.json"] = json.dumps(profile, indent=2).encode("utf-8")

    application = json.loads(files["ApplicationInfo.json"])
    application["defaultProfileName"] = guid
    files["ApplicationInfo.json"] = json.dumps(application, indent=2).encode("utf-8")

    preview = json.loads(files["metadata/ProfilePreview.json"])
    update_preview(preview, profile)
    files["metadata/ProfilePreview.json"] = json.dumps(preview, indent=2).encode("utf-8")

    files["metadata/LoupedeckPackage.yaml"] = (
        files["metadata/LoupedeckPackage.yaml"]
        .decode("utf-8")
        .replace("Claude Console", DISPLAY)
        .replace("ClaudeConsole", PLUGIN)
        .encode("utf-8")
    )

    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in items:
            zout.writestr(item, files[item.filename])
    print(f"wrote {dst}")


def main() -> None:
    for src, dst, guid in PROFILES:
        build_profile(src, dst, guid)


if __name__ == "__main__":
    os.chdir(os.path.join(os.path.dirname(__file__), ".."))
    main()
