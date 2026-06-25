#!/bin/bash
# Build ClaudeVoiceHelper.app — a tiny signed bundle that owns its own Microphone TCC grant.
# The .app form (with NSMicrophoneUsageDescription in Info.plist) is what lets macOS prompt for
# and remember mic access independent of LogiPluginService. Run: bash build.sh
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
APP="$HERE/ClaudeVoiceHelper.app"
BIN="$APP/Contents/MacOS/ClaudeVoiceHelper"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp "$HERE/Info.plist" "$APP/Contents/Info.plist"

echo ">>> compiling (Swift, arm64)"
swiftc -O "$HERE/ClaudeVoiceHelper.swift" -o "$BIN" \
  -framework Foundation -framework AVFoundation -framework AppKit

# Signing identity: "-" (ad-hoc, the default for dev builds) or a "Developer ID Application: …" name
# (exported by sign-and-notarize.sh for a release). Developer-ID signing also turns on the hardened
# runtime + secure timestamp and applies the microphone entitlement.
SIGN_IDENTITY="${SIGN_IDENTITY:--}"
if [ "$SIGN_IDENTITY" = "-" ]; then
  echo ">>> ad-hoc signing"
  codesign --force --sign - --identifier com.rshankar.claudeconsole.voicehelper "$APP"
else
  echo ">>> Developer-ID signing (hardened runtime + mic entitlement): $SIGN_IDENTITY"
  ENT="${HELPER_ENTITLEMENTS:-$HERE/helper.entitlements}"
  codesign --force --timestamp --options runtime --entitlements "$ENT" \
    --identifier com.rshankar.claudeconsole.voicehelper --sign "$SIGN_IDENTITY" "$APP"
fi

echo ">>> signature:"
codesign -dvv "$APP" 2>&1 | grep -E "Identifier=|Authority=|Signature=" || true
echo "built: $APP"

# Install to the plugin's runtime home (next to the whisper model + hooks). The plugin launches
# the app from here, NOT from the source tree. NOTE: recompiling changes the ad-hoc code hash, so
# macOS will re-prompt for Microphone permission the next time the Voice key is used. (A real
# Developer-ID signature would keep the grant stable across rebuilds — for the shipping build.)
RUNTIME="$HOME/.claude/claude-console/ClaudeVoiceHelper.app"
echo ">>> installing to $RUNTIME"
mkdir -p "$(dirname "$RUNTIME")"
rm -rf "$RUNTIME"
cp -R "$APP" "$RUNTIME"
echo "installed: $RUNTIME"

# Bundle a self-contained whisper-cli next to the helper so voice needs no Homebrew at runtime.
# (Skips gracefully if whisper-cli isn't installed to build from — voice falls back to a system
# whisper-cli, and the plugin downloads the speech model itself on first use.)
if command -v whisper-cli >/dev/null 2>&1; then
  echo ">>> bundling self-contained whisper-cli"
  bash "$HERE/bundle-whisper.sh"
else
  echo ">>> skipping whisper bundle (no whisper-cli to vendor; 'brew install whisper-cpp' to enable)"
fi
