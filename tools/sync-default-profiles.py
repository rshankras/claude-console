#!/usr/bin/env python3
"""Keep the downloadable keypad profiles aligned with the approved keypad design.

The plugin is universal (#23): it ships no packaged profile, and the layout reaches a keypad
only through the two importable fallbacks in profiles/. An LP5 stores its key bindings and a
static first-page preview, so moving keys in Options+ fixes one developer's keypad without
fixing anyone else's import. This script rewrites the FIRST PAGE of both profiles to the
approved layout and reorders each profile's preview thumbnails to match; every other page and
every other zip member is preserved byte for byte.

Run from the repository root: python3 tools/sync-default-profiles.py
"""

from __future__ import annotations

import copy
import json
import os
import tempfile
import zipfile


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROFILES = [
    os.path.join(ROOT, "profiles", "ClaudeConsole-Keypad.lp5"),    # macOS, Terminal entry
    os.path.join(ROOT, "profiles", "ClaudeConsole-Windows.lp5"),   # Windows Terminal entry
]

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
        raise RuntimeError(f"preview is missing actions: {', '.join(missing)}")

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
    for path in PROFILES:
        profile, preview = read_documents(path)
        apply_first_page(profile)
        write_documents(path, profile, design_preview(preview))
        print(f"updated {os.path.relpath(path, ROOT)}")


if __name__ == "__main__":
    main()
