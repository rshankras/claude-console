#!/bin/bash
# bundle-whisper.sh — produce a SELF-CONTAINED whisper-cli so offline voice works without Homebrew.
#
# Homebrew's whisper-cli links against dylibs scattered across /opt/homebrew (libwhisper, libggml,
# libggml-base, libomp). This copies the binary plus that whole dylib closure into one flat folder
# and rewrites every install name / rpath to @rpath (resolved next to each file via @loader_path),
# so the bundle runs on any Apple-Silicon Mac with NO Homebrew and NO whisper-cpp/ggml formulae.
#
# Output: ~/.claude/claude-console/whisper-bin/   (sits next to ClaudeVoiceHelper.app)
#   whisper-cli  libwhisper.1.dylib  libggml.0.dylib  libggml-base.0.dylib  libomp.dylib
#   LICENSE.whisper.cpp  LICENSE.ggml
#
# Files are re-signed ad-hoc (editing a Mach-O invalidates its signature). For DISTRIBUTION in the
# .lplug4, re-sign each file with your Developer ID + hardened runtime and notarize — ad-hoc
# signatures will not pass Gatekeeper on someone else's Mac. See SUBMISSION.md.
#
# Usage: bash bundle-whisper.sh [path-to-whisper-cli]
# NOTE: written for macOS /bin/bash (3.2) — no associative arrays.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"

SRC_CLI="${1:-$(command -v whisper-cli || true)}"
if [ -z "$SRC_CLI" ] || [ ! -x "$SRC_CLI" ]; then
  echo "error: whisper-cli not found. Install it (brew install whisper-cpp) or pass its path." >&2
  exit 1
fi
echo ">>> source whisper-cli: $SRC_CLI"

OUT="$HOME/.claude/claude-console/whisper-bin"
echo ">>> output dir: $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"

# Signing identity: "-" (ad-hoc, default for dev) or a "Developer ID Application: …" name for release
# (set by sign-and-notarize.sh). Developer-ID signing also enables the hardened runtime + timestamp.
# Under the hardened runtime whisper-cli needs Metal entitlements (see whisper.entitlements), applied
# to the executable only — dylibs inherit the process's entitlements from it.
SIGN_IDENTITY="${SIGN_IDENTITY:--}"
WHISPER_ENTITLEMENTS="${WHISPER_ENTITLEMENTS:-$HERE/whisper.entitlements}"
sign_macho() {
  local f="$1" want_ent="${2:-}"
  if [ "$SIGN_IDENTITY" = "-" ]; then
    codesign --force --sign - "$f"
  elif [ -n "$want_ent" ] && [ -f "$WHISPER_ENTITLEMENTS" ]; then
    codesign --force --timestamp --options runtime --entitlements "$WHISPER_ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$f"
  else
    codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY" "$f"
  fi
}

CLI_BASE="$(basename "$SRC_CLI")"
copied=""   # space-separated basenames of dylibs already copied (bash-3.2-safe set)
in_copied() { case " $copied " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

is_system() { case "$1" in /usr/lib/*|/System/*) return 0 ;; *) return 1 ;; esac; }

# Homebrew lib dirs searched to turn an @rpath/<name> reference into a real file on disk.
HB_LIBDIRS=(
  "$(brew --prefix 2>/dev/null || echo /opt/homebrew)/lib"
  /opt/homebrew/opt/whisper-cpp/lib
  /opt/homebrew/opt/ggml/lib
  /opt/homebrew/opt/libomp/lib
  /usr/local/lib
)
resolve_ref() {
  local ref="$1" b d
  case "$ref" in
    @rpath/*) b="${ref#@rpath/}"
      for d in "${HB_LIBDIRS[@]}"; do [ -f "$d/$b" ] && { echo "$d/$b"; return 0; }; done ;;
    /*) [ -f "$ref" ] && { echo "$ref"; return 0; } ;;
  esac
  return 1
}

# deps of a Mach-O file (skip the otool header line)
deps_of() { otool -L "$1" | tail -n +2 | awk '{print $1}'; }

# Recursively copy a file's non-system dylib closure into $OUT, naming each by its install basename.
process() {
  local f="$1" dep base real
  while read -r dep; do
    [ -z "$dep" ] && continue
    is_system "$dep" && continue
    base="$(basename "$dep")"
    [ "$base" = "$(basename "$f")" ] && continue   # the file's own id line
    in_copied "$base" && continue
    if ! real="$(resolve_ref "$dep")"; then
      echo "warn: could not resolve $dep" >&2
      continue
    fi
    cp -L "$real" "$OUT/$base"
    chmod u+w "$OUT/$base"
    copied="$copied $base"
    process "$OUT/$base"
  done < <(deps_of "$f")
}

cp -L "$SRC_CLI" "$OUT/$CLI_BASE"
chmod u+wx "$OUT/$CLI_BASE"
process "$OUT/$CLI_BASE"
echo ">>> bundled:$copied $CLI_BASE"

# Rewrite install names + rpaths so everything resolves from its own directory, then re-sign.
relocate() {
  local f="$1" isdylib="$2" dep base
  [ "$isdylib" = "1" ] && install_name_tool -id "@rpath/$(basename "$f")" "$f"
  while read -r dep; do
    [ -z "$dep" ] && continue
    is_system "$dep" && continue
    base="$(basename "$dep")"
    [ "$base" = "$(basename "$f")" ] && continue
    [ "$dep" = "@rpath/$base" ] && continue        # already correct — skip no-op
    install_name_tool -change "$dep" "@rpath/$base" "$f"
  done < <(deps_of "$f")
  install_name_tool -add_rpath "@loader_path" "$f" 2>/dev/null || true
  # The main executable carries the Metal entitlements; the dylibs don't need them.
  if [ "$isdylib" = "1" ]; then sign_macho "$f"; else sign_macho "$f" ent; fi
}

for base in $copied; do relocate "$OUT/$base" 1; done
relocate "$OUT/$CLI_BASE" 0

# Bundle the MIT licenses for the redistributed binaries.
WC_LIC="$(ls -d /opt/homebrew/Cellar/whisper-cpp/*/LICENSE 2>/dev/null | head -1 || true)"
GG_LIC="$(ls -d /opt/homebrew/Cellar/ggml/*/LICENSE 2>/dev/null | head -1 || true)"
[ -n "$WC_LIC" ] && cp "$WC_LIC" "$OUT/LICENSE.whisper.cpp"
[ -n "$GG_LIC" ] && cp "$GG_LIC" "$OUT/LICENSE.ggml"

# Verify no Homebrew paths leaked into the relocated closure.
echo ">>> verifying no residual Homebrew references"
if otool -L "$OUT/$CLI_BASE" "$OUT"/*.dylib | grep -q "/opt/homebrew"; then
  echo "error: residual /opt/homebrew references remain:" >&2
  otool -L "$OUT/$CLI_BASE" "$OUT"/*.dylib | grep "/opt/homebrew" >&2
  exit 1
fi

# Smoke test: launch with Homebrew off PATH; if dyld can't resolve the closure it says so on stderr.
echo ">>> smoke test (Homebrew not on PATH)"
err="$(PATH=/usr/bin:/bin "$OUT/$CLI_BASE" --help 2>&1 >/dev/null || true)"
case "$err" in
  *"Library not loaded"*|*"image not found"*|*"Symbol not found"*|*"dyld"*)
    echo "error: dyld could not resolve the bundle standalone:" >&2
    printf '%s\n' "$err" >&2
    exit 1 ;;
esac

echo "✅ self-contained whisper-cli -> $OUT"
if [ "$SIGN_IDENTITY" = "-" ]; then
  echo "   (ad-hoc signed; run sign-and-notarize.sh for a Gatekeeper-clean, notarized build)"
else
  echo "   (signed: $SIGN_IDENTITY)"
fi
