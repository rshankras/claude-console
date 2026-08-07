#!/usr/bin/env bash
# Publish the two Windows helper executables and stage them into the plugin package tree.
#
# Both cross-compile from macOS, so a single .lplug4 built here carries the payload for BOTH
# platforms (pluginFolderMac + pluginFolderWin in LoupedeckPackage.yaml both point at bin/).
#
#   claude-console-inject.exe   types into one Claude session's console (Phase 2)
#   claude-console-hook.exe     statusline + activity hooks (Phase 4)
#   claude-console-focus.exe    selects the Windows Terminal tab for a session (Phase 3)
#   claude-console-voice.exe    microphone capture + whisper transcription (Phase 5)
#
# Usage: tools/windows/build-windows-payload.sh [Release|Debug] [win-x64|win-arm64]
set -euo pipefail

CONFIG="${1:-Release}"
RID="${2:-win-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEST="$ROOT/bin/$CONFIG/bin"

echo ">>> building Windows helpers ($CONFIG, $RID)"
mkdir -p "$DEST"

# Focus is the odd one out: it targets net8.0-windows and stays framework-dependent, because
# WPF's UI Automation client cannot be trimmed. On a machine without the .NET Desktop runtime it
# simply doesn't run and tab-focus degrades to raising the window — everything else is unaffected,
# which is why it is a separate exe rather than a verb on inject.
for proj in ClaudeConsoleInject ClaudeConsoleHook ClaudeConsoleVoice ClaudeConsoleFocus; do
  [ -d "$ROOT/tools/windows/$proj" ] || { echo ">>>   $proj (absent — skipped)"; continue; }
  echo ">>>   $proj"
  # Each csproj decides self-contained vs framework-dependent (see their comments); don't
  # override it here, or the trimming settings that keep these small get silently discarded.
  # EnableWindowsTargeting is what lets a net8.0-windows project (the focus helper) publish from
  # macOS — without it the SDK refuses with NETSDK1100 and, if output is suppressed, the exe just
  # quietly never appears in the package. Harmless for the net8.0 helpers.
  dotnet publish "$ROOT/tools/windows/$proj" \
    -c "$CONFIG" -r "$RID" \
    -p:PublishSingleFile=true \
    -p:EnableWindowsTargeting=true \
    -o "$ROOT/tools/windows/$proj/publish-$RID" >/dev/null

  # Only the .exe ships — the .pdb is debug weight.
  found=$(find "$ROOT/tools/windows/$proj/publish-$RID" -maxdepth 1 -name "*.exe" | wc -l | tr -d ' ')
  if [ "$found" = "0" ]; then
    echo "error: $proj produced no .exe — the package would silently ship without it." >&2
    exit 1
  fi
  find "$ROOT/tools/windows/$proj/publish-$RID" -maxdepth 1 -name "*.exe" -exec cp {} "$DEST/" \;
done

echo ">>> staged into $DEST:"
ls -1 "$DEST"/*.exe 2>/dev/null | sed 's|.*/|      |'
