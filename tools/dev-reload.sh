#!/usr/bin/env bash
# dev-reload.sh — rebuild a product and check the build is the kind that renders.
#
# This used to also restart the Logi Plugin Service and the Options+ agent, because a bare
# `dotnet build` sent the service a plugin RELOAD that dropped our application registration from
# its live list — the icon vanished from the Options+ strip until a restart re-read it from disk.
# The plugin is universal now (#23): it has no application registration, so there is nothing a
# reload can lose, and the csproj's own PostBuild reload is the whole dev loop again. What is
# left here is the one check worth automating.
#
# Usage:  bash tools/dev-reload.sh [Product]      (default: ClaudeConsole)
set -euo pipefail

PRODUCT="${1:-ClaudeConsole}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DLL="$REPO/bin/$PRODUCT/Debug/bin/${PRODUCT}Plugin.dll"

echo ">>> building $PRODUCT"
# NOT -t:Compile. It shares obj/ with a full build, and a full build that follows it can reuse an
# output with no embedded resources: the plugin loads and every key renders as bare text. See
# CLAUDE.md. If you have just run -t:Compile, clear obj/ and bin/ for this product first.
( cd "$REPO" && dotnet build "src/Products/$PRODUCT" ) | tail -3

if [ -f "$DLL" ]; then
  size=$(stat -f %z "$DLL")
  echo ">>> $(basename "$DLL"): $size bytes"
  # ~1 MB healthy, ~140 KB means the embedded icons were dropped — the exact signature of an
  # obj/ poisoned by a preceding -t:Compile. Worth catching here rather than while staring at a
  # keypad full of bare text.
  if [ "$size" -lt 400000 ]; then
    echo "    WARNING: that is far too small — the icon resources are missing." >&2
    echo "    rm -rf src/Products/$PRODUCT/obj bin/$PRODUCT and build again." >&2
    exit 1
  fi
fi

echo "done — the service reloaded the plugin; no restart needed."
