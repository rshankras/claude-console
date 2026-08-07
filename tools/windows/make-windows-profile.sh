#!/usr/bin/env bash
# Derive the Windows keypad profile from the macOS one.
#
# WHY THIS EXISTS: a .lp5 carries the identity of the application it belongs to, and that
# identity is OS-specific. The macOS profile says:
#
#   ApplicationInfo.json   "processOrBundleName": "com.apple.Terminal"
#   ProfileInfo.json       "applicationName":     "com.apple.terminal"
#
# On Windows the plugin registers its application by PROCESS name (WindowsTerminal — see
# ClaudeConsoleApplication.GetProcessName), so a mac bundle id matches nothing and the service
# silently skips the import. That is the "plugin installed but no profile" symptom.
#
# So: same layout, same keys, same GUID-stamped action ids — only the application binding and the
# profile GUID change. A fresh profile GUID keeps the two from deduping against each other
# (auto-import dedupes by GUID).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC="$ROOT/src/package/profiles/DefaultProfile70.lp5"
OUT="$ROOT/src/package/profiles/DefaultProfile70Win.lp5"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

[ -f "$SRC" ] || { echo "error: $SRC not found" >&2; exit 1; }

unzip -q "$SRC" -d "$WORK"

# A stable, distinct GUID for the Windows profile — derived, not random, so rebuilding this file
# doesn't mint a new profile on every release (which would pile up duplicates for the user).
WIN_GUID="FFDD8BE1282D4688846AFD8A1F62 0B59"
WIN_GUID="${WIN_GUID// /}"

python3 - "$WORK" "$WIN_GUID" <<'PY'
import json, os, sys
work, win_guid = sys.argv[1], sys.argv[2]

app_path = os.path.join(work, "ApplicationInfo.json")
app = json.load(open(app_path))
# The Windows matcher is the bare process name — no .exe, matching GetProcessName().
app["processOrBundleName"] = "WindowsTerminal"
app["description"] = "Claude Code controls for Windows Terminal."
app["defaultProfileName"] = win_guid
json.dump(app, open(app_path, "w"), indent=2)

prof_path = os.path.join(work, "ProfileInfo.json")
prof = json.load(open(prof_path))
prof["applicationName"] = "windowsterminal"   # the service lowercases the matcher on macOS too
prof["name"] = win_guid
json.dump(prof, open(prof_path, "w"), indent=2)

print(f"  app  processOrBundleName -> {app['processOrBundleName']}")
print(f"  prof applicationName     -> {prof['applicationName']}")
print(f"  profile GUID             -> {win_guid}")
PY

rm -f "$OUT"
( cd "$WORK" && zip -qr "$OUT" . )
echo ">>> wrote $OUT"
