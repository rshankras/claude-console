#!/bin/bash
# Build VizhiAxBridge — the one-shot AX automation helper for desktop agent apps.
#
# A PLAIN BINARY, deliberately not an .app bundle: unlike the voice helper (which must be its
# own TCC subject to own the Microphone grant), this helper wants the OPPOSITE — spawned as a
# direct child of LogiPluginService, its AX calls attribute to the service, which already holds
# the Accessibility grant the terminal products required. No new permission prompt, and ad-hoc
# re-signing does not reset anything.
#
# SIGN_IDENTITY: "-" (ad-hoc, dev default) or a "Developer ID Application: ..." name for release
# (exported by tools/voice/sign-and-notarize.sh).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BIN="$HERE/VizhiAxBridge"

swiftc -O "$HERE/VizhiAxBridge.swift" -o "$BIN"

SIGN_IDENTITY="${SIGN_IDENTITY:--}"
if [ "$SIGN_IDENTITY" = "-" ]; then
  echo ">>> ad-hoc signing"
  codesign --force --sign - --identifier com.rshankar.vizhi.axbridge "$BIN"
else
  echo ">>> Developer-ID signing (hardened runtime): $SIGN_IDENTITY"
  codesign --force --timestamp --options runtime \
    --identifier com.rshankar.vizhi.axbridge --sign "$SIGN_IDENTITY" "$BIN"
fi

codesign -dvv "$BIN" 2>&1 | grep -E "Identifier=|Authority=|Signature=" || true

# Install to the SHARED runtime home (same reasoning as the voice helper: one binary serves
# every product that ships it). The plugin launches from here, never the source tree; on a
# package-only install, EnsureDesktopRuntimeInstalled copies it out of the .lplug4 instead.
RUNTIME="$HOME/.claude/claude-console/VizhiAxBridge"
echo ">>> installing to $RUNTIME"
mkdir -p "$(dirname "$RUNTIME")"
cp -f "$BIN" "$RUNTIME"
echo "installed: $RUNTIME"
