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
#   claude-console-shot.exe     interactive region capture -> PNG (ms-screenclip: + clipboard)
#
# Usage: tools/windows/build-windows-payload.sh [Release|Debug] [win-x64|win-arm64] [product]
set -euo pipefail

CONFIG="${1:-Release}"
RID="${2:-win-x64}"
PRODUCT="${3:-ClaudeConsole}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# Same bin/ as the macOS payload — see LoupedeckPackage.yaml for why sharing is fine.
# Per PRODUCT since the repo builds a package per agent: staging into a shared bin/ (which this
# did before the split) silently drops the helpers from every package, and Windows support is
# exactly what depends on them.
DEST="$ROOT/bin/$PRODUCT/$CONFIG/bin"

echo ">>> building Windows helpers ($CONFIG, $RID)"
mkdir -p "$DEST"

# Every helper is a self-contained, trimmed console exe. Options+ does not supply a globally
# discoverable .NET on clean installs, so a framework-dependent helper fails there (#83).
for proj in ClaudeConsoleInject ClaudeConsoleHook ClaudeConsoleVoice ClaudeConsoleFocus ClaudeConsoleShot; do
  [ -d "$ROOT/tools/windows/$proj" ] || { echo ">>>   $proj (absent — skipped)"; continue; }
  echo ">>>   $proj"
  case "$proj" in
    ClaudeConsoleFocus|ClaudeConsoleShot)
      # These two shipped framework-dependent once (#83). A framework-dependent single-file
      # publish also emits just an exe, so the sidecar check below alone cannot catch that
      # regression; ask the project what it intends.
      contained=$(dotnet msbuild "$ROOT/tools/windows/$proj/$proj.csproj" \
        -p:Configuration="$CONFIG" -p:RuntimeIdentifier="$RID" \
        -p:EnableWindowsTargeting=true -getProperty:SelfContained | tr -d '\r')
      if [ "$contained" != "true" ]; then
        echo "error: $proj must bundle its runtime (SelfContained=true)." >&2
        exit 1
      fi
      ;;
  esac
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
  # Staging only executables is safe only when all runtime dependencies are inside the bundle.
  if find "$ROOT/tools/windows/$proj/publish-$RID" -type f \( -name '*.dll' -o -name '*.runtimeconfig.json' -o -name '*.deps.json' \) | grep -q .; then
    echo "error: $proj left runtime dependencies outside its executable." >&2
    exit 1
  fi
  find "$ROOT/tools/windows/$proj/publish-$RID" -maxdepth 1 -name "*.exe" -exec cp {} "$DEST/" \;
done

echo ">>> staged into $DEST:"
ls -1 "$DEST"/*.exe 2>/dev/null | sed 's|.*/|      |'
