#!/usr/bin/env bash
# Publish the two Windows helper executables and stage them into the plugin package tree.
#
# Both cross-compile from macOS, so a single .lplug4 built here carries the payload for BOTH
# platforms (pluginFolderMac + pluginFolderWin in LoupedeckPackage.yaml both point at bin/).
#
#   claude-console-inject.exe   types into one Claude session's console (Phase 2)
#   claude-console-hook.exe     statusline + activity hooks (Phase 4)
#
# Usage: tools/windows/build-windows-payload.sh [Release|Debug] [win-x64|win-arm64]
set -euo pipefail

CONFIG="${1:-Release}"
RID="${2:-win-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEST="$ROOT/bin/$CONFIG/bin"

echo ">>> building Windows helpers ($CONFIG, $RID)"
mkdir -p "$DEST"

for proj in ClaudeConsoleInject ClaudeConsoleHook; do
  echo ">>>   $proj"
  # Framework-dependent single file. Self-contained would add ~70 MB of runtime PER helper to
  # the package; every machine running Options+ 6.4 already has .NET, and RollForward=LatestMajor
  # lets these net8.0 builds run on it. The plugin locates each exe by name beside its own DLL.
  dotnet publish "$ROOT/tools/windows/$proj" \
    -c "$CONFIG" -r "$RID" --self-contained false \
    -p:PublishSingleFile=true \
    -o "$ROOT/tools/windows/$proj/publish-$RID" >/dev/null

  # Only the .exe ships — the .pdb is debug weight.
  find "$ROOT/tools/windows/$proj/publish-$RID" -maxdepth 1 -name "*.exe" -exec cp {} "$DEST/" \;
done

echo ">>> staged into $DEST:"
ls -1 "$DEST"/*.exe 2>/dev/null | sed 's|.*/|      |'
