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

# The compute backends. ggml 0.15+ is a DYNAMIC-BACKEND build: libggml.dylib is only a registry,
# and CPU/Metal/BLAS live in separate .so files it dlopens at runtime. dlopen leaves no trace in
# the Mach-O load commands, so the closure walk above cannot see them — which is how the bundle
# shipped with ZERO backends and aborted on GGML_ASSERT(device) for every user without Homebrew,
# while working perfectly here, where libggml's compiled-in search path still resolves (#24).
# They have to be copied explicitly. ggml_backend_load_best searches the directory of the running
# EXECUTABLE, so a flat bundle finds them with no environment variable set.
BACKEND_DIR="${GGML_BACKEND_SRC:-$(brew --prefix ggml 2>/dev/null || echo /opt/homebrew/opt/ggml)/libexec}"
backends=""
if [ -d "$BACKEND_DIR" ]; then
  for so in "$BACKEND_DIR"/*.so; do
    [ -e "$so" ] || continue
    b="$(basename "$so")"
    cp -L "$so" "$OUT/$b"
    chmod u+w "$OUT/$b"
    backends="$backends $b"
    process "$OUT/$b"   # each backend has its own closure — the CPU ones pull in libomp
  done
fi
if [ -z "$backends" ]; then
  echo "warn: no ggml backends in $BACKEND_DIR — only correct if this ggml is a static build." >&2
  echo "      The transcription smoke test below is what actually decides." >&2
fi
echo ">>> backends:$backends"

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
  # The main executable carries the Metal entitlements; nothing it loads needs its own, since
  # entitlements belong to the process. A backend gets no -id (it is dlopened by path, never
  # linked) but it IS signed: under the hardened runtime an unsigned dlopen is refused.
  case "$isdylib" in
    0) sign_macho "$f" ent ;;
    *) sign_macho "$f" ;;
  esac
}

for base in $copied; do relocate "$OUT/$base" 1; done
for base in $backends; do relocate "$OUT/$base" so; done
relocate "$OUT/$CLI_BASE" 0

# Bundle the MIT licenses for the redistributed binaries.
WC_LIC="$(ls -d /opt/homebrew/Cellar/whisper-cpp/*/LICENSE 2>/dev/null | head -1 || true)"
GG_LIC="$(ls -d /opt/homebrew/Cellar/ggml/*/LICENSE 2>/dev/null | head -1 || true)"
[ -n "$WC_LIC" ] && cp "$WC_LIC" "$OUT/LICENSE.whisper.cpp"
[ -n "$GG_LIC" ] && cp "$GG_LIC" "$OUT/LICENSE.ggml"

# Verify no Homebrew paths leaked into the relocated closure.
echo ">>> verifying no residual Homebrew references"
if [ -n "$backends" ]; then
  refs="$(otool -L "$OUT/$CLI_BASE" "$OUT"/*.dylib "$OUT"/*.so)"
else
  refs="$(otool -L "$OUT/$CLI_BASE" "$OUT"/*.dylib)"
fi
if printf '%s\n' "$refs" | grep -q "/opt/homebrew"; then
  echo "error: residual /opt/homebrew references remain:" >&2
  printf '%s\n' "$refs" | grep "/opt/homebrew" >&2
  exit 1
fi

# Smoke test 1: launch with Homebrew off PATH; if dyld can't resolve the closure it says so on stderr.
echo ">>> smoke test 1: dyld closure (Homebrew not on PATH)"
err="$(PATH=/usr/bin:/bin "$OUT/$CLI_BASE" --help 2>&1 >/dev/null || true)"
case "$err" in
  *"Library not loaded"*|*"image not found"*|*"Symbol not found"*|*"dyld"*)
    echo "error: dyld could not resolve the bundle standalone:" >&2
    printf '%s\n' "$err" >&2
    exit 1 ;;
esac

# Smoke test 2: transcribe for real, on a machine pretending to have no Homebrew.
#
# The sandbox is the whole point, and it is not belt-and-braces. ggml loads its compute backends
# by dlopen at runtime, and falls back to the path Homebrew's build compiled in. On THIS machine
# that path exists, so a bundle carrying no backends at all transcribes perfectly — which is
# precisely how a broken bundle passed CI and shipped (#24). Denying the Homebrew prefix is what
# makes this test able to fail. Asserting that a backend was loaded FROM THE BUNDLE is what makes
# it able to fail for the right reason.
SMOKE_MODEL="${WHISPER_SMOKE_MODEL:-$HOME/.claude/claude-console/whisper/ggml-base.en.bin}"
SMOKE_MARKER="$OUT/TRANSCRIPTION_SMOKE_OK"
rm -f "$SMOKE_MARKER"
if [ ! -f "$SMOKE_MODEL" ]; then
  echo "warning: no speech model at $SMOKE_MODEL — transcription smoke SKIPPED." >&2
  echo "         Set WHISPER_SMOKE_MODEL. Release packaging will refuse this bundle." >&2
else
  echo ">>> smoke test 2: real transcription, Homebrew unreachable"
  TMPD="$(mktemp -d)"
  trap 'rm -rf "$TMPD"' EXIT
  # 1 second of 16 kHz mono silence. Silence is enough: a missing backend aborts at registration,
  # before a single sample is read.
  /usr/bin/perl -e '$n=32000; print pack("A4VA4A4VvvVVvvA4V", "RIFF", 36+$n, "WAVE", "fmt ", 16, 1, 1, 16000, 32000, 2, 16, "data", $n), "\0" x $n' > "$TMPD/smoke.wav"
  BREW_PREFIX="$(brew --prefix 2>/dev/null || echo /opt/homebrew)"
  printf '%s\n' '(version 1)' '(allow default)' \
    "(deny file-read* (subpath \"$BREW_PREFIX\"))" > "$TMPD/no-brew.sb"

  smoke_rc=0
  /usr/bin/sandbox-exec -f "$TMPD/no-brew.sb" "$OUT/$CLI_BASE" \
    -m "$SMOKE_MODEL" -f "$TMPD/smoke.wav" -nt >/dev/null 2>"$TMPD/smoke.err" || smoke_rc=$?

  if [ "$smoke_rc" -ne 0 ]; then
    echo "error: the bundle cannot transcribe on a machine without Homebrew (exit $smoke_rc)." >&2
    echo "       This is the #24 failure: no compute backend registered." >&2
    tail -25 "$TMPD/smoke.err" >&2
    exit 1
  fi
  # Loaded, but from where? If ggml found a backend outside $OUT the bundle is still not standalone.
  if ! grep -q "load_backend: loaded .* from $OUT/" "$TMPD/smoke.err"; then
    echo "error: transcription succeeded but no backend was loaded from the bundle." >&2
    echo "       ggml resolved its compute backend somewhere else — the bundle is not self-contained." >&2
    grep -i "load_backend\|search path" "$TMPD/smoke.err" >&2 || true
    exit 1
  fi
  grep -o "load_backend: loaded [A-Za-z]* backend" "$TMPD/smoke.err" | sed 's/^/    /'
  printf 'model=%s\nbackends=%s\n' "$(basename "$SMOKE_MODEL")" "$backends" > "$SMOKE_MARKER"
fi

echo "✅ self-contained whisper-cli -> $OUT"
if [ "$SIGN_IDENTITY" = "-" ]; then
  echo "   (ad-hoc signed; run sign-and-notarize.sh for a Gatekeeper-clean, notarized build)"
else
  echo "   (signed: $SIGN_IDENTITY)"
fi
