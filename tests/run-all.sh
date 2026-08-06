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
