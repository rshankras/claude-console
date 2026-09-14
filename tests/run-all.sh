#!/usr/bin/env bash
# Run the whole Claude Console test suite: C# unit tests + the bridge writer script tests.
#
#   bash tests/run-all.sh
#
# Safe to run any time — the test project builds the plugin with SkipPluginLink=true, so it never
# writes the dev .link into the live Logi plugin directory or reloads LogiPluginService.
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATUS=0

# The grid code DELETES state files for sessions it judges dead. Tests must therefore drive a
# throwaway root, never the live one — pointed at /tmp/claude-console a test run would wipe a
# running session's state.
#
# Detect that by leaving a canary in the live root and checking it survives. Comparing a full
# directory listing would NOT work: with the plugin installed, Claude Code and the plugin write
# there continuously, so any snapshot differs by the time the suite finishes. Only a test that
# deletes or wipes the root can remove the canary.
LIVE_ROOT="/tmp/claude-console"
CANARY="$LIVE_ROOT/.suite-canary"
CANARY_PLACED=0
if [ -d "$LIVE_ROOT" ]; then
  if : > "$CANARY" 2>/dev/null; then CANARY_PLACED=1; fi
fi

# The same idea for the user's Claude Code settings. The engine can now be pointed at a temp home
# (BridgeManager.HomeOverride / tests/TempHome.cs), so a settings test that touches the REAL
# ~/.claude/settings.json is a test that forgot to. Record what is there before, compare after.
SETTINGS="$HOME/.claude/settings.json"
OPT_OUT="$HOME/.claude/claude-console/no-autowire"
SETTINGS_BEFORE="$( [ -f "$SETTINGS" ] && cksum < "$SETTINGS" || echo absent )"
OPT_OUT_BEFORE="$( [ -e "$OPT_OUT" ] && echo present || echo absent )"

echo "▶ C# unit tests"
if ! dotnet test "$REPO/tests/ClaudeConsolePlugin.Tests.csproj" --nologo "$@"; then
  STATUS=1
fi

echo
echo "▶ bridge script tests"
if ! bash "$REPO/tests/scripts/test-bridge-scripts.sh"; then
  STATUS=1
fi

echo
echo "▶ concurrent uninstall cleanup"
if ! python3 "$REPO/tests/scripts/test-unwire-concurrency.py"; then
  STATUS=1
fi

echo
echo "▶ codex hook tests"
if ! bash "$REPO/tests/scripts/test-codex-hook.sh"; then
  STATUS=1
fi

echo
echo "▶ profile update and cross-process input-lock tests"
if ! python3 "$REPO/tests/scripts/test-profile-update.py"; then STATUS=1; fi
if ! python3 "$REPO/tests/scripts/test-input-lock.py"; then STATUS=1; fi
if ! python3 "$REPO/tests/scripts/test-windows-input.py"; then STATUS=1; fi

echo "▶ live settings.json not touched"
SETTINGS_AFTER="$( [ -f "$SETTINGS" ] && cksum < "$SETTINGS" || echo absent )"
OPT_OUT_AFTER="$( [ -e "$OPT_OUT" ] && echo present || echo absent )"
if [ "$SETTINGS_BEFORE" != "$SETTINGS_AFTER" ]; then
  echo "  FAIL a test rewrote the LIVE $SETTINGS — tests must use TempHome (BridgeManager.HomeOverride)."
  STATUS=1
elif [ "$OPT_OUT_BEFORE" != "$OPT_OUT_AFTER" ]; then
  echo "  FAIL a test changed the LIVE opt-out marker ($OPT_OUT) — tests must use TempHome."
  STATUS=1
elif ls "$HOME/.claude"/settings.json.cc.*.tmp >/dev/null 2>&1; then
  echo "  FAIL a settings.json temp file was left behind under ~/.claude"
  STATUS=1
else
  echo "  ok   $SETTINGS and the opt-out marker are as they were"
fi

echo
echo "▶ live IPC root not wiped"
if [ "$CANARY_PLACED" -eq 0 ]; then
  echo "  skip no live IPC root on this machine — nothing a test could destroy"
elif [ -f "$CANARY" ]; then
  rm -f "$CANARY"
  echo "  ok   $LIVE_ROOT survived the test run"
else
  echo "  FAIL a suite deleted the LIVE IPC root ($LIVE_ROOT) — that wipes running sessions' state."
  echo "       Tests must use an injected temp root — see SessionRegistryTests."
  STATUS=1
fi

echo
if [ "$STATUS" -eq 0 ]; then
  echo "✅ all suites passed"
else
  echo "❌ some suites failed"
fi
exit "$STATUS"
