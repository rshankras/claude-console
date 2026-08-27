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
# Usage: bash tools/voice/pack-release.sh [version] [product]
#        product = ClaudeConsole (default) | VizhiCodex — one repo, one package per run.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

VER="${1:-1_1}"
PRODUCT="${2:-ClaudeConsole}"
OUT="$ROOT/${PRODUCT}_${VER}.lplug4"
BUILD_DIR="$ROOT/bin/$PRODUCT/Release"

HOME_DIR="$HOME/.claude/claude-console"
APP="$HOME_DIR/ClaudeVoiceHelper.app"
WBIN="$HOME_DIR/whisper-bin"

# Which products ship offline voice. Both do: voice is agent-neutral — it records, transcribes and
# injects into the focused session without asking which agent runs there. The payload installs to a
# runtime home shared by every product (~/.claude/claude-console) under one bundle id, so a user with
# both packages gets one notarized helper, one Microphone grant and one 141 MB model download.
case "$PRODUCT" in
  ClaudeConsole|VizhiCodex) SHIPS_VOICE=1 ;;
  *)                        SHIPS_VOICE=0 ;;
esac

# --- preflight: the voice payload must exist and be notarized ------------------------------------
if [ "$SHIPS_VOICE" = "1" ]; then
  [ -d "$APP" ]  || { echo "error: helper missing ($APP) — run sign-and-notarize.sh first." >&2; exit 1; }
  [ -d "$WBIN" ] || { echo "error: whisper bundle missing ($WBIN) — run sign-and-notarize.sh first." >&2; exit 1; }
  # A whisper bundle that has never transcribed anything must not ship. 2.0.1 went out with a
  # bundle carrying no compute backends: it aborted on every user machine and passed every check
  # here, because this machine's Homebrew supplied the backends it was missing (#24). The marker
  # is written by bundle-whisper.sh only after a real transcription with Homebrew unreachable.
  if [ ! -f "$WBIN/TRANSCRIPTION_SMOKE_OK" ]; then
    echo "error: $WBIN has not passed the transcription smoke test." >&2
    echo "       Re-run tools/voice/bundle-whisper.sh (it needs a speech model; see" >&2
    echo "       WHISPER_SMOKE_MODEL) — an unverified bundle is how the voice regression shipped." >&2
    exit 1
  fi
  if ! xcrun stapler validate "$APP" >/dev/null 2>&1; then
    echo "error: $APP is not stapled/notarized — run tools/voice/sign-and-notarize.sh first." >&2
    exit 1
  fi
  echo ">>> voice payload OK (helper notarized + stapled, bundle transcription-verified)"
else
  echo ">>> $PRODUCT ships no voice payload — skipping the notarization preflight"
fi

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
rm -rf "$BUILD_DIR"

echo ">>> building plugin (Release)"
( cd "$ROOT/src/Products/$PRODUCT" && dotnet build -c Release -p:SkipPluginLink=true >/dev/null )

# A shipped binary must not name the machine it was built on. Release builds set PathMap
# (src/Directory.Build.props) so the recorded PDB path becomes /src/... instead of the author's
# home directory, which 2.0.1 disclosed to anyone running `strings` on the plugin (#26). Verify it
# here rather than trusting the property: this is the only place a Release DLL actually exists.
echo ">>> checking the Release DLL for build-machine paths"
LEAKED="$(python3 - "$BUILD_DIR" <<'PY'
import pathlib, re, sys
pat = re.compile(rb'(?:/Users/|[A-Za-z]:\\\\Users\\\\)[^\x00]{0,160}')
for dll in pathlib.Path(sys.argv[1]).rglob('*.dll'):
    for hit in pat.findall(dll.read_bytes()):
        print(f"{dll.name}: {hit.decode(errors='replace')}")
PY
)"
if [ -n "$LEAKED" ]; then
  echo "error: the Release build embeds build-machine paths:" >&2
  printf '%s\n' "$LEAKED" | head -10 >&2
  echo "       PathMap in src/Directory.Build.props should prevent this — check it applied." >&2
  exit 1
fi

# Belt and braces: if a .link is already lying around from an earlier dev build, it will collide
# with the package we are about to install. Clear it now rather than debugging it later.
LINK="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins/ClaudeConsolePlugin.link"
[ -f "$LINK" ] && { rm -f "$LINK"; echo ">>> removed a stale dev .link (would have collided with the package)"; }

# --- build the Windows helpers into the same bin/ (cross-compiled from macOS) ---------------------
# One .lplug4 serves both platforms: LoupedeckPackage.yaml points pluginFolderMac AND
# pluginFolderWin at bin/, so these two exes ride along beside the plugin DLL and are simply
# never launched on macOS.
echo ">>> building Windows helper payload"
bash "$ROOT/tools/windows/build-windows-payload.sh" Release win-x64 "$PRODUCT"

# --- embed the notarized voice payload next to the plugin DLL (bin/voice/) ------------------------
PKG_VOICE="$BUILD_DIR/bin/voice"
if [ "$SHIPS_VOICE" = "1" ]; then
  echo ">>> embedding voice payload -> $PKG_VOICE"
  rm -rf "$PKG_VOICE"
  mkdir -p "$PKG_VOICE"
  ditto "$APP"  "$PKG_VOICE/ClaudeVoiceHelper.app"   # ditto preserves signature + exec bits
  ditto "$WBIN" "$PKG_VOICE/whisper-bin"
else
  rm -rf "$PKG_VOICE"
fi

# --- pack ----------------------------------------------------------------------------------------
echo ">>> packing $OUT"
rm -f "$OUT"
logiplugintool pack "$BUILD_DIR" "$OUT"

echo
echo "✅ $OUT"
echo "   size: $(du -h "$OUT" | cut -f1)"
if [ "$SHIPS_VOICE" = "1" ]; then
  echo "   voice payload in package:"
  unzip -l "$OUT" | grep -iE "voice/.*(ClaudeVoiceHelper|whisper-cli)" | sed 's/^/     /'
fi
echo
if [ "$SHIPS_VOICE" = "1" ]; then
  echo "Test on a clean Mac (no dev tools): install, press Voice, allow Microphone, dictate."
else
  echo "Test on a clean Mac (no dev tools): install, let the layout import, then start a session."
fi
