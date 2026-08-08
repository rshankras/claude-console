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
# SkipPluginLink is NOT optional here. Without it the csproj PostBuild target drops a dev
# ClaudeConsolePlugin.link next to the INSTALLED package, so the service registers the plugin
# twice ("already loaded") and it fails to load — which looks like "the plugin installed but the
# profile didn't import", because the app registration never runs. A release build must never
# touch the live plugin directory.
# Wipe the output tree first. CopyPackage copies package/** in but never removes what has been
# deleted since, so a file dropped from the repo lingers in bin/Release and ships anyway — a
# retired profile rode along into 1.8.4 exactly this way.
echo ">>> clearing stale build output"
rm -rf "$ROOT/bin/Release"

echo ">>> building plugin (Release)"
( cd "$ROOT/src" && dotnet build -c Release -p:SkipPluginLink=true >/dev/null )

# Belt and braces: if a .link is already lying around from an earlier dev build, it will collide
# with the package we are about to install. Clear it now rather than debugging it later.
LINK="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins/ClaudeConsolePlugin.link"
[ -f "$LINK" ] && { rm -f "$LINK"; echo ">>> removed a stale dev .link (would have collided with the package)"; }

# --- build the Windows helpers into the same bin/ (cross-compiled from macOS) ---------------------
# One .lplug4 serves both platforms: LoupedeckPackage.yaml points pluginFolderMac AND
# pluginFolderWin at bin/, so these two exes ride along beside the plugin DLL and are simply
# never launched on macOS.
echo ">>> building Windows helper payload"
bash "$ROOT/tools/windows/build-windows-payload.sh" Release win-x64

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
