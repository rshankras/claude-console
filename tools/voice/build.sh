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

echo ">>> ad-hoc signing"
codesign --force --sign - --identifier com.rshankar.claudeconsole.voicehelper "$APP"

echo ">>> signature:"
codesign -dvv "$APP" 2>&1 | grep -E "Identifier=|Signature=" || true
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
