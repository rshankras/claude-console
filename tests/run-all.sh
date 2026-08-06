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
# running session's state. Fingerprint the live root before and after to prove they never do.
LIVE_ROOT="/tmp/claude-console"
live_fingerprint() { ls -la "$LIVE_ROOT" 2>/dev/null | sort; }
LIVE_BEFORE="$(live_fingerprint)"

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
echo "▶ live IPC root untouched"
if [ "$(live_fingerprint)" = "$LIVE_BEFORE" ]; then
  echo "  ok   $LIVE_ROOT unchanged by the test run"
else
  echo "  FAIL a suite wrote to or deleted from the LIVE IPC root ($LIVE_ROOT)."
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
