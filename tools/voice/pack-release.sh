#!/bin/bash
# pack-release.sh — build a distributable .lplug4 with offline voice EMBEDDED.
#
# Copies the Developer-ID-signed, notarized voice helper + self-contained whisper-cli into the
# packed plugin (under bin/voice/), so a package-only install has working voice: the plugin installs
# them to ~/.claude/claude-console/ and strips quarantine on first use (BridgeManager
# .EnsureVoiceRuntimeInstalled). The ~142 MB speech model is NOT embedded — it downloads on first use.
#
# Prerequisite: run tools/voice/sign-and-notarize.sh first so the runtime-home artifacts are
# Developer-ID signed + notarized (the helper stapled). This script refuses to package an
# un-notarized helper.
#
# Usage: bash tools/voice/pack-release.sh [version]     (version defaults to 1_1)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

VER="${1:-1_1}"
OUT="$ROOT/ClaudeConsole_${VER}.lplug4"

HOME_DIR="$HOME/.claude/claude-console"
APP="$HOME_DIR/ClaudeVoiceHelper.app"
WBIN="$HOME_DIR/whisper-bin"

# --- preflight: the voice payload must exist and be notarized ------------------------------------
[ -d "$APP" ]  || { echo "error: helper missing ($APP) — run sign-and-notarize.sh first." >&2; exit 1; }
[ -d "$WBIN" ] || { echo "error: whisper bundle missing ($WBIN) — run sign-and-notarize.sh first." >&2; exit 1; }
if ! xcrun stapler validate "$APP" >/dev/null 2>&1; then
  echo "error: $APP is not stapled/notarized — run tools/voice/sign-and-notarize.sh first." >&2
  exit 1
fi
echo ">>> voice payload OK (helper notarized + stapled)"

# --- build the plugin (Release) ------------------------------------------------------------------
echo ">>> building plugin (Release)"
( cd "$ROOT/src" && dotnet build -c Release >/dev/null )

# --- embed the notarized voice payload next to the plugin DLL (bin/voice/) ------------------------
PKG_VOICE="$ROOT/bin/Release/bin/voice"
echo ">>> embedding voice payload -> $PKG_VOICE"
rm -rf "$PKG_VOICE"
mkdir -p "$PKG_VOICE"
ditto "$APP"  "$PKG_VOICE/ClaudeVoiceHelper.app"   # ditto preserves signature + exec bits
ditto "$WBIN" "$PKG_VOICE/whisper-bin"

# --- pack ----------------------------------------------------------------------------------------
echo ">>> packing $OUT"
rm -f "$OUT"
logiplugintool pack "$ROOT/bin/Release" "$OUT"

echo
echo "✅ $OUT"
echo "   size: $(du -h "$OUT" | cut -f1)"
echo "   voice payload in package:"
unzip -l "$OUT" | grep -iE "voice/.*(ClaudeVoiceHelper|whisper-cli)" | sed 's/^/     /'
echo
echo "Test on a clean Mac (no dev tools): install, press Voice, allow Microphone, dictate."
