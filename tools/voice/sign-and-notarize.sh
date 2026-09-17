#!/bin/bash
# sign-and-notarize.sh — RELEASE step. Builds the voice helper + whisper bundle signed with your
# Developer ID + hardened runtime, then notarizes them so they pass Gatekeeper on other Macs.
#
# Prerequisites:
#   - a "Developer ID Application" identity in your keychain (security find-identity -v -p codesigning)
#   - a stored notarytool keychain profile (xcrun notarytool store-credentials <PROFILE> ...)
#
# It delegates the actual signing to build.sh / bundle-whisper.sh by exporting SIGN_IDENTITY, so
# there is a single signing code path (ad-hoc for dev, Developer ID here).
#
# Usage: bash sign-and-notarize.sh
#   override via env: SIGN_IDENTITY=... NOTARY_PROFILE=... HELPER_ENTITLEMENTS=...
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"

export SIGN_IDENTITY="${SIGN_IDENTITY:-Developer ID Application: Ravi Shankar (8LEAJKRS3U)}"
export HELPER_ENTITLEMENTS="${HELPER_ENTITLEMENTS:-$HERE/helper.entitlements}"
PROFILE="${NOTARY_PROFILE:-claude-console-notary}"

HOME_DIR="$HOME/.claude/claude-console"
APP="$HOME_DIR/ClaudeVoiceHelper.app"
WBIN="$HOME_DIR/whisper-bin"

# --- preflight ------------------------------------------------------------------------------------
# Capture, THEN grep. This file runs under pipefail, and `cmd | grep -q` is a race under pipefail:
# grep -q exits on the first match and closes the pipe, the producer dies of SIGPIPE, and the
# pipeline reports failure — a signed file read as unsigned. It fired on the first backend of an
# otherwise clean 2.2.0 release round and stopped the script one step from done.
identities="$(security find-identity -v -p codesigning)"
grep -q "$SIGN_IDENTITY" <<<"$identities" \
  || { echo "error: signing identity not found: $SIGN_IDENTITY" >&2; exit 1; }
[ -f "$HELPER_ENTITLEMENTS" ] || { echo "error: entitlements missing: $HELPER_ENTITLEMENTS" >&2; exit 1; }
xcrun notarytool history --keychain-profile "$PROFILE" >/dev/null 2>&1 \
  || { echo "error: notary profile '$PROFILE' not usable — run 'xcrun notarytool store-credentials'." >&2; exit 1; }

echo "============================================================"
echo " RELEASE build — Developer-ID sign + notarize"
echo "   identity : $SIGN_IDENTITY"
echo "   profile  : $PROFILE"
echo "============================================================"

# --- 1) build + sign (helper + whisper) with the exported Developer ID ----------------------------
bash "$HERE/build.sh"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# --- 2) notarize the helper, then staple (the .app can carry its ticket offline) ------------------
echo ">>> submitting helper to the notary service (can take a few minutes)…"
ditto -c -k --keepParent "$APP" "$TMP/helper.zip"
xcrun notarytool submit "$TMP/helper.zip" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$APP"

# --- 3) notarize the whisper bundle ---------------------------------------------------------------
# Loose Mach-O (bare executable + dylibs) can't be stapled; notarizing still registers their hashes
# with Apple so the online Gatekeeper check passes. (When shipped inside the .lplug4 / .app, the
# enclosing stapled container covers the offline case too.)
if [ -d "$WBIN" ]; then
  echo ">>> submitting whisper-bin to the notary service…"
  ditto -c -k --keepParent "$WBIN" "$TMP/whisper.zip"
  xcrun notarytool submit "$TMP/whisper.zip" --keychain-profile "$PROFILE" --wait
fi

# --- 4) verify ------------------------------------------------------------------------------------
echo ">>> verifying helper signature + Gatekeeper assessment"
# These are release gates, not diagnostic decoration. `codesign -d` only prints metadata and
# spctl/stapler were previously tolerated with `|| true`, so a rejected helper could still produce
# the script's green success line (#66).
codesign --verify --deep --strict --verbose=4 "$APP"
spctl --assess --type execute --verbose=4 "$APP"
xcrun stapler validate "$APP"
codesign -dvvv "$APP" 2>&1 | grep -E "Authority|TeamIdentifier|Identifier=|Runtime|flags" || true
if [ -d "$WBIN" ]; then
  echo ">>> verifying whisper-cli signature"
  codesign -dvvv "$WBIN/whisper-cli" 2>&1 | grep -E "Authority|TeamIdentifier|Runtime" || true
  # The compute backends are dlopened, so Gatekeeper meets them at RUNTIME rather than at launch:
  # an ad-hoc leftover here is a voice failure on the user's machine, not a notarization error
  # here. Verify each one really carries the Developer ID (#24).
  for so in "$WBIN"/*.so; do
    [ -e "$so" ] || continue
    # Same pipefail trap as the preflight: capture first, grep second. And say WHICH check failed —
    # "not signed" covered both an invalid signature and a wrong identity, and the pipefail race.
    if ! codesign -v "$so" 2>/dev/null; then
      echo "error: $(basename "$so") has an invalid signature" >&2
      exit 1
    fi
    details="$(codesign -dvvv "$so" 2>&1)"
    if ! grep -q "$SIGN_IDENTITY" <<<"$details"; then
      echo "error: $(basename "$so") is not signed with $SIGN_IDENTITY" >&2
      exit 1
    fi
  done
  echo ">>> ggml backends signed: $(ls "$WBIN"/*.so 2>/dev/null | wc -l | tr -d ' ')"
fi

echo "✅ Developer-ID signed + notarized. Helper stapled."
