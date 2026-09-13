#!/usr/bin/env bash
# Publish the hook and shared interactive toolkit into the plugin package tree.
#
# Both cross-compile from macOS, so a single .lplug4 built here carries the payload for BOTH
# platforms (pluginFolderMac + pluginFolderWin in LoupedeckPackage.yaml both point at bin/).
#
#   claude-console-hook.exe     statusline + activity hooks, with its own watchdog and cap
#   claude-console-tools.exe    inject / focus / voice / shot, one new process per invocation
#
# The toolkit shares ONE runtime across four operations instead of shipping four copies.
# The standalone projects are retained for diagnostics, but are not release payload.
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
for proj in ClaudeConsoleHook ClaudeConsoleTools; do
  [ -d "$ROOT/tools/windows/$proj" ] || { echo "error: required project $proj is absent" >&2; exit 1; }
  echo ">>>   $proj"
  case "$proj" in
    ClaudeConsoleTools)
      # The focus/shot predecessors shipped framework-dependent once (#83). A single-file
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

# A developer can stage over an older output tree. Remove only superseded generated helpers;
# otherwise the zip still contains the redundant runtimes and the size saving disappears.
for tool in inject focus voice shot; do
  rm -f "$DEST/claude-console-$tool.exe"
done

echo ">>> staged into $DEST:"
ls -1 "$DEST"/*.exe 2>/dev/null | sed 's|.*/|      |'
