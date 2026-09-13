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
INTERMEDIATE_DIR="$ROOT/src/Products/$PRODUCT/obj/Release"

HOME_DIR="$HOME/.claude/claude-console"
APP="$HOME_DIR/ClaudeVoiceHelper.app"
WBIN="$HOME_DIR/whisper-bin"
# A Windows whisper.cpp bundle prepared and smoke-tested ON WINDOWS (#47). Kept separate from the
# macOS bundle: both contain a whisper-cli with platform-specific dependencies and cannot share one
# directory in the package. The plugin copies it from bin/voice/whisper-bin-win/ into the runtime
# home on first use (BridgeManager.EnsureVoiceRuntimeInstalledWindows) — until 2.2.0 no package
# carried it, so packaged Windows voice failed with "whisper-cli.exe missing" on every machine
# where nobody had placed the bundle by hand.
WIN_WBIN="${WINDOWS_WHISPER_DIR:-$HOME_DIR/whisper-bin-win}"

verify_macos_helper() {
  local helper="$1"
  if ! codesign --verify --deep --strict --verbose=4 "$helper"; then
    echo "error: $helper failed strict code-signature verification." >&2
    return 1
  fi
  if ! spctl --assess --type execute --verbose=4 "$helper"; then
    echo "error: Gatekeeper rejected $helper." >&2
    return 1
  fi
  if ! xcrun stapler validate "$helper"; then
    echo "error: $helper has no valid stapled notarization ticket." >&2
    return 1
  fi
}

# Which products ship offline voice. Both do: voice is agent-neutral — it records, transcribes and
# injects into the focused session without asking which agent runs there. The payload installs to a
# runtime home shared by every product (~/.claude/claude-console) under one bundle id, so a user with
# both packages gets one notarized helper, one Microphone grant and one 141 MB model download.
case "$PRODUCT" in
  ClaudeConsole|VizhiCodex) SHIPS_VOICE=1 ;;
  *)
    echo "error: unsupported product '$PRODUCT' (expected ClaudeConsole or VizhiCodex)." >&2
    exit 2
    ;;
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
  verify_macos_helper "$APP"
  # The Windows bundle needs the same proof, and it can only be produced on Windows: run
  # whisper-cli.exe against a real recording there, then write the marker beside it.
  [ -d "$WIN_WBIN" ] || {
    echo "error: Windows whisper bundle missing ($WIN_WBIN) — set WINDOWS_WHISPER_DIR to a smoke-tested Windows bundle (#47)." >&2
    exit 1
  }
  [ -f "$WIN_WBIN/whisper-cli.exe" ] || {
    echo "error: Windows whisper bundle has no whisper-cli.exe ($WIN_WBIN)." >&2
    exit 1
  }
  [ -f "$WIN_WBIN/TRANSCRIPTION_SMOKE_OK" ] || {
    echo "error: $WIN_WBIN has not passed a real transcription smoke test on Windows." >&2
    exit 1
  }
  echo ">>> voice payload OK (helper notarized + stapled, both bundles transcription-verified)"
else
  echo ">>> $PRODUCT ships no voice payload — skipping the notarization preflight"
fi

# --- build the plugin (Release) ------------------------------------------------------------------
# SkipPluginLink is NOT optional here. Without it the csproj PostBuild target drops a dev
# ClaudeConsolePlugin.link next to the INSTALLED package, so the service registers the plugin
# twice ("already loaded") and it fails to load — which looks like "the plugin installed but the
# profile didn't import", because the app registration never runs. A release build must never
# touch the live plugin directory.
# Wipe BOTH halves of the Release build first. CopyPackage never removes deleted payload files from
# bin/Release, so stale output has shipped before. The intermediate directory is just as important:
# `dotnet build -t:Compile -c Release` writes a newer DLL there without first generating embedded
# resources; a later incremental build can copy that DLL unchanged and ship zero icons, bridge
# scripts, or PluginConfiguration.xml. A release must never depend on what command ran before it.
echo ">>> clearing stale Release output and intermediates"
rm -rf "$BUILD_DIR" "$INTERMEDIATE_DIR"

echo ">>> building plugin (Release)"
( cd "$ROOT/src/Products/$PRODUCT" && dotnet build -c Release -p:SkipPluginLink=true >/dev/null )

# Catch a resource-less incremental DLL at the artifact boundary as well as preventing it above.
# These names live in the assembly manifest and therefore appear literally in the managed binary.
# One common icon plus the product-specific bridge resource proves the three resource item groups
# (icons, PluginConfiguration, bridge scripts) all reached the DLL that will actually be packed.
PLUGIN_DLL="$BUILD_DIR/bin/${PRODUCT}Plugin.dll"
case "$PRODUCT" in
  ClaudeConsole) BRIDGE_RESOURCE="ClaudeConsole.statusline-handler.sh" ;;
  VizhiCodex)    BRIDGE_RESOURCE="CodexConsole.codex-hook.sh" ;;
esac
for resource in \
  "Loupedeck.ClaudeConsolePlugin.PluginConfiguration.xml" \
  "Loupedeck.ClaudeConsolePlugin.Resources.icons.allow.png" \
  "$BRIDGE_RESOURCE"
do
  if ! LC_ALL=C grep -aFq "$resource" "$PLUGIN_DLL"; then
    echo "error: $PLUGIN_DLL is missing embedded resource '$resource'." >&2
    echo "       Refusing to package an incomplete incremental build." >&2
    exit 1
  fi
done

# A shipped binary must not name the machine it was built on. Release builds set PathMap
# (src/Directory.Build.props) so the recorded PDB path becomes /src/... instead of the author's
# home directory, which 2.0.1 disclosed to anyone running `strings` on the plugin (#26). Verify it
# here rather than trusting the property: this is the only place a Release DLL actually exists.
echo ">>> checking the Release DLL and PDB for build-machine paths"
# The PDB needs its own check: a portable PDB stores each path SEGMENT as a separate blob, so the
# whole-path pattern below never matches one, and 2.2.0 shipped the author's worktree in the PDB
# twice (document paths of the engine's sources + the Source Link map) with this check green (#62).
# The user name and the checkout folder are the two segments that identify a machine.
LEAKED="$(python3 - "$BUILD_DIR" "$(basename "$HOME")" "$(basename "$ROOT")" <<'PY'
import pathlib, re, sys
root, user, checkout = pathlib.Path(sys.argv[1]), sys.argv[2].encode(), sys.argv[3].encode()
pat = re.compile(rb'(?:/Users/|[A-Za-z]:\\\\Users\\\\)[^\x00]{0,160}')
for dll in root.rglob('*.dll'):
    for hit in pat.findall(dll.read_bytes()):
        print(f"{dll.name}: {hit.decode(errors='replace')}")
for pdb in root.rglob('*.pdb'):
    data = pdb.read_bytes()
    for needle in (user, checkout, b'raw.githubusercontent.com'):
        if needle and needle in data:
            print(f"{pdb.name}: contains '{needle.decode(errors='replace')}'")
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
  ditto "$WIN_WBIN" "$PKG_VOICE/whisper-bin-win"
  # The markers are proof for THIS script, not payload: RuntimeTreeMatchesPackage compares every
  # packaged file, so anything copied here lands in every user's runtime home. 2.2.0 stripped
  # only the Windows one, and Logitech QA read the asymmetry as "the smoke test was run for Mac
  # only" (#64). Both go; the attestation lives in this script's output instead.
  rm -f "$PKG_VOICE/whisper-bin/TRANSCRIPTION_SMOKE_OK" "$PKG_VOICE/whisper-bin-win/TRANSCRIPTION_SMOKE_OK"
  echo "   smoke-tested: macOS bundle $(date -r "$WBIN/TRANSCRIPTION_SMOKE_OK" '+%Y-%m-%d %H:%M'), Windows bundle $(date -r "$WIN_WBIN/TRANSCRIPTION_SMOKE_OK" '+%Y-%m-%d %H:%M') (markers not shipped)"
else
  rm -rf "$PKG_VOICE"
fi

# --- pack ----------------------------------------------------------------------------------------
echo ">>> packing $OUT"
rm -f "$OUT"
logiplugintool pack "$BUILD_DIR" "$OUT"

# Verify what users will actually install, not only the source copied into staging. A packer can
# alter permissions, omit nested signature files, or substitute a stale bundle while the source
# helper remains valid. Extract into a fresh directory and apply the same three release gates to
# the packaged helper (#66).
VERIFY_DIR="$(mktemp -d)"
trap 'rm -rf "$VERIFY_DIR"' EXIT
ditto -x -k "$OUT" "$VERIFY_DIR"
PACKED_APP="$(find "$VERIFY_DIR" -type d -name ClaudeVoiceHelper.app -print -quit)"
if [ -z "$PACKED_APP" ]; then
  echo "error: the package contains no ClaudeVoiceHelper.app." >&2
  exit 1
fi
echo ">>> verifying the helper extracted from the final package"
verify_macos_helper "$PACKED_APP"

echo
echo "✅ $OUT"
echo "   size: $(du -h "$OUT" | cut -f1)"
if [ "$SHIPS_VOICE" = "1" ]; then
  echo "   voice payload in package:"
  unzip -l "$OUT" | grep -iE "voice/.*(ClaudeVoiceHelper|whisper-cli)" | sed 's/^/     /'
  # Not `grep -q`: under `set -o pipefail` its early exit can SIGPIPE unzip, and the pipeline then
  # fails a package that carries the bundle (3 of 30 runs on 2026-09-04; it failed the first 2.2.1
  # pack). Reading the whole listing costs nothing and cannot race.
  unzip -l "$OUT" | grep "voice/whisper-bin-win/whisper-cli.exe" >/dev/null || {
    echo "error: the package carries no Windows whisper bundle — packaged Windows voice would fail (#47)." >&2
    exit 1
  }
fi
echo
if [ "$SHIPS_VOICE" = "1" ]; then
  echo "Test on a clean Mac (no dev tools): install, press Voice, allow Microphone, dictate."
else
  echo "Test on a clean Mac (no dev tools): install, let the layout import, then start a session."
fi
