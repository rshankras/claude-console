#!/usr/bin/env python3
"""Keep the Claude Console default profiles aligned with the approved keypad design.

The live command images come from the plugin, but an LP5 also stores its key bindings and a
static first-page preview.  Moving keys only in Options+ therefore fixes one developer's keypad
without fixing a clean install.  This script updates both the packaged default and the manual
macOS fallback; the Windows fallback is then derived from the packaged default by
tools/windows/make-windows-profile.sh.

Run from the repository root: python3 tools/sync-default-profiles.py
"""

from __future__ import annotations

import copy
import json
import os
import tempfile
import zipfile


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGED = os.path.join(
    ROOT, "src", "Products", "ClaudeConsole", "package", "profiles", "DefaultProfile70.lp5"
)
MAC_FALLBACK = os.path.join(ROOT, "profiles", "ClaudeConsole-Keypad.lp5")

PREFIX = "$ClaudeConsole___Loupedeck.ClaudeConsolePlugin.Actions."
FIRST_PAGE = [
    "SessionSlotCommand___1",
    "SessionSlotCommand___2",
    "SessionSlotCommand___3",
    "ControlCommand___clear",
    "AnswerCommand___no",
    "AnswerCommand___yes",
    "ControlCommand___esc",
    "ControlCommand___tab",
    "VoiceCommand",
]


def read_documents(path: str) -> tuple[dict, dict]:
    with zipfile.ZipFile(path) as archive:
        profile = json.loads(archive.read("ProfileInfo.json"))
        preview = json.loads(archive.read("metadata/ProfilePreview.json"))
    return profile, preview


def write_documents(path: str, profile: dict, preview: dict) -> None:
    """Replace two JSON entries while preserving every other LP5 member and its metadata."""
    replacements = {
        "ProfileInfo.json": json.dumps(profile, indent=2).encode("utf-8"),
        "metadata/ProfilePreview.json": json.dumps(preview, indent=2).encode("utf-8"),
    }
    directory = os.path.dirname(path)
    with tempfile.NamedTemporaryFile(dir=directory, suffix=".lp5", delete=False) as temporary:
        temporary_path = temporary.name
    try:
        with zipfile.ZipFile(path) as source, zipfile.ZipFile(
            temporary_path, "w", zipfile.ZIP_DEFLATED
        ) as target:
            for item in source.infolist():
                target.writestr(item, replacements.get(item.filename, source.read(item.filename)))
        os.replace(temporary_path, path)
    finally:
        if os.path.exists(temporary_path):
            os.unlink(temporary_path)


def first_page(profile: dict) -> list[dict]:
    return profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0]["controls"]


def apply_first_page(profile: dict) -> None:
    controls = {control["controlId"]: control for control in first_page(profile)}
    if sorted(controls) != list(range(9)):
        raise RuntimeError("first page must contain controls 0 through 8")
    for control_id, action in enumerate(FIRST_PAGE):
        controls[control_id]["pressAction"] = PREFIX + action


def design_preview(source_preview: dict) -> dict:
    """Reorder the existing static thumbnails to mirror the live first page."""
    by_action = {
        item["actionName"].split("Actions.", 1)[-1]: item
        for item in source_preview["buttonPages"]
    }
    missing = [action for action in FIRST_PAGE if action not in by_action]
    if missing:
        raise RuntimeError(f"packaged preview is missing actions: {', '.join(missing)}")

    result = copy.deepcopy(source_preview)
    result["buttonPages"] = []
    for control_id, action in enumerate(FIRST_PAGE):
        item = copy.deepcopy(by_action[action])
        item["controlId"] = control_id
        if action == "VoiceCommand":
            item["displayName"] = "Dictate"
        result["buttonPages"].append(item)
    return result


def main() -> None:
    packaged_profile, packaged_preview = read_documents(PACKAGED)
    apply_first_page(packaged_profile)
    packaged_preview = design_preview(packaged_preview)
    write_documents(PACKAGED, packaged_profile, packaged_preview)
    print(f"updated {os.path.relpath(PACKAGED, ROOT)}")

    fallback_profile, _ = read_documents(MAC_FALLBACK)
    apply_first_page(fallback_profile)
    write_documents(MAC_FALLBACK, fallback_profile, packaged_preview)
    print(f"updated {os.path.relpath(MAC_FALLBACK, ROOT)}")


if __name__ == "__main__":
    main()
