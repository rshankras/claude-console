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
if [ "$STATUS" -eq 0 ]; then
  echo "✅ all suites passed"
else
  echo "❌ some suites failed"
fi
exit "$STATUS"
