#!/usr/bin/env bash
# Derive the Windows keypad profile from the macOS one.
#
# WHY THIS EXISTS: a .lp5 carries the identity of the application it belongs to, and that
# identity is OS-specific. The macOS profile says:
#
#   ApplicationInfo.json   "processOrBundleName": "com.apple.Terminal"
#   ProfileInfo.json       "applicationName":     "com.apple.terminal"
#
# On Windows the host is Windows Terminal, matched by PROCESS name, so a mac bundle id matches
# nothing and the service silently skips the import. That is the "no profile" symptom.
#
# So: same layout, same keys, same GUID-stamped action ids — only the application binding and the
# profile GUID change. A fresh profile GUID keeps the two from deduping against each other.
#
# Both files are DOWNLOADS the user imports (Options+ -> profile menu -> import). The plugin is
# universal (#23) and carries no profile; each .lp5 is a profile for the TERMINAL's own Options+
# entry that happens to use our actions, so the binding here is Windows Terminal's, not ours.
# UNVERIFIED ON WINDOWS since the universal change: the entry name ("windowsterminal") and the
# default-plugin name ("DefaultWin") follow the mac file's pattern (com.apple.terminal, DefaultMac)
# and need one import on the laptop to confirm (#47's session is the natural place).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC="$ROOT/profiles/ClaudeConsole-Keypad.lp5"
OUT="$ROOT/profiles/ClaudeConsole-Windows.lp5"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

[ -f "$SRC" ] || { echo "error: $SRC not found" >&2; exit 1; }

unzip -q "$SRC" -d "$WORK"

# A stable, distinct GUID for the Windows profile — fixed, not random, so rebuilding this file
# doesn't mint a new profile on every release (which would pile up duplicates for the user).
# Rotated once on 2026-08-08 when the profile got its own package identity (Vizhi shape): the
# old value (…0B59) doubled as the imported instance GUID on the Windows laptop, and a package
# should not share its identity with an installed instance.
WIN_GUID="9556D49413DC49B1968CA44D3C3C3303"

python3 - "$WORK" "$WIN_GUID" <<'PY'
import json, os, sys
work, win_guid = sys.argv[1], sys.argv[2]

app_path = os.path.join(work, "ApplicationInfo.json")
app = json.load(open(app_path))
# The Windows matcher is the bare process name — no .exe, matching GetProcessName().
app["name"] = "windowsterminal"
app["displayName"] = "Windows Terminal"
app["processOrBundleName"] = "WindowsTerminal"
app["defaultProfileName"] = win_guid
json.dump(app, open(app_path, "w"), indent=2)

prof_path = os.path.join(work, "ProfileInfo.json")
prof = json.load(open(prof_path))
prof["applicationName"] = "windowsterminal"   # the service lowercases the matcher on macOS too
prof["name"] = win_guid
prof["additionalNativePluginNames"] = [
    "DefaultWin" if n == "DefaultMac" else n for n in prof.get("additionalNativePluginNames", [])]
# Self-owning package stamp (Vizhi shape): a profile whose packageName names its own package
# GUID is treated as package-owned, so installs refresh the registration instead of skipping
# it. The mac source stamps its own GUID; restamp to the Windows GUID so ownership follows
# the derived profile, not the mac original.
if prof.get("packageName"):
    prof["packageName"] = win_guid
json.dump(prof, open(prof_path, "w"), indent=2)

print(f"  app  processOrBundleName -> {app['processOrBundleName']}")
print(f"  prof applicationName     -> {prof['applicationName']}")
print(f"  profile GUID             -> {win_guid}")
PY

rm -f "$OUT"
( cd "$WORK" && zip -qr "$OUT" . )
echo ">>> wrote $OUT"
