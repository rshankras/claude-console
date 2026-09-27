#!/usr/bin/env bash
# sign-windows-payload.sh — Authenticode-sign every Windows executable and DLL under a directory.
#
#   bash tools/windows/sign-windows-payload.sh <dir> [ClaudeConsole|VizhiCodex]
#
# Called by tools/voice/pack-release.sh on the staged bin/ tree, after the helpers are built and
# the Windows whisper bundle is copied in, and before logiplugintool packs. It runs on the Mac:
# the certificate lives in Certum's SimplySign cloud, and SimplySign Desktop presents it to this
# machine as a PKCS#11 token (a virtual smart card). jsign does the Authenticode work; the key
# never leaves Certum.
#
# Why sign at all (#110): Logitech QA's CrowdStrike quarantined the unsigned hook exe on sight.
# A signed file names a publisher, so their IT can allow-list us once by certificate instead of
# by hash on every release. It does NOT guarantee a brand-new certificate passes any scanner —
# that is what the signed-package device pass on their machine is for.
#
# What it signs: every *.exe and *.dll under <dir>, recursively — the two helpers, the plugin DLL,
# whisper-cli.exe and the ggml DLLs. Every PE in the package, because endpoint security judges
# DLL loads as well as process launches.
#
# Per release the only interactive step is being logged in to SimplySign Desktop (your email +
# the rotating code from the SimplySign phone app). Everything else is here.
#
# Environment:
#   WINDOWS_SIGNING=skip        leave the files unsigned (dev packs only; pack-release then names
#                               the package *-unsigned.lplug4 and verify-package only warns)
#   CODESIGN_PKCS11_MODULE      SimplySign's PKCS#11 library (default: the SimplySign Desktop install)
#   CODESIGN_PKCS11_SLOT        slot id for jsign's SunPKCS11 config (default 1 — "Code Signing")
#   CODESIGN_ALIAS              certificate label on the token; needed only if it holds several
#   CODESIGN_INTERMEDIATE       PEM of the issuing CA (default: tools/windows/signing/…)
#   CODESIGN_TSA                RFC 3161 timestamp server (default: Certum's)
#   CODESIGN_CAFILE             CA bundle for the post-sign verify (default: Homebrew's, then /etc/ssl)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

DIR="${1:-}"
PRODUCT="${2:-ClaudeConsole}"
[ -n "$DIR" ] && [ -d "$DIR" ] || { echo "usage: bash tools/windows/sign-windows-payload.sh <dir> [product]" >&2; exit 2; }

MODULE="${CODESIGN_PKCS11_MODULE:-/usr/local/lib/libSimplySignPKCS.dylib}"
SLOT="${CODESIGN_PKCS11_SLOT:-1}"
INTERMEDIATE="${CODESIGN_INTERMEDIATE:-$ROOT/tools/windows/signing/certum-code-signing-2021-ca.pem}"
TSA="${CODESIGN_TSA:-http://time.certum.pl}"

# The description and URL land in the file's Properties → Digital Signatures tab on Windows.
case "$PRODUCT" in
  ClaudeConsole) NAME="Claude Console";  URL="https://vizhi.dev/claude-console/" ;;
  VizhiCodex)    NAME="Vizhi for Codex"; URL="https://vizhi.dev/vizhi-codex/" ;;
  *) echo "error: unsupported product '$PRODUCT' (expected ClaudeConsole or VizhiCodex)." >&2; exit 2 ;;
esac

# Everything a Windows machine would load: helpers, the plugin DLL, whisper and its backends.
PES=()
while IFS= read -r -d '' f; do PES+=("$f"); done < <(find "$DIR" -type f \( -iname '*.exe' -o -iname '*.dll' \) -print0 | sort -z)
if [ "${#PES[@]}" -eq 0 ]; then
  echo "error: no .exe or .dll under $DIR — nothing to sign; was the Windows payload built?" >&2
  exit 1
fi

if [ "${WINDOWS_SIGNING:-}" = "skip" ]; then
  echo "!!! WINDOWS_SIGNING=skip — leaving ${#PES[@]} Windows files UNSIGNED. This is not a release build."
  exit 0
fi

# --- preflight -----------------------------------------------------------------------------------
for tool in jsign osslsigncode pkcs11-tool openssl; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "error: $tool is not installed — brew install jsign osslsigncode opensc" >&2; exit 1; }
done
[ -f "$MODULE" ] || {
  echo "error: SimplySign PKCS#11 library not found at $MODULE — install SimplySign Desktop (Certum)." >&2; exit 1; }
[ -f "$INTERMEDIATE" ] || { echo "error: issuing-CA certificate missing: $INTERMEDIATE" >&2; exit 1; }

CAFILE="${CODESIGN_CAFILE:-}"
if [ -z "$CAFILE" ]; then
  for candidate in /opt/homebrew/etc/ca-certificates/cert.pem /usr/local/etc/ca-certificates/cert.pem /etc/ssl/cert.pem; do
    [ -f "$candidate" ] && { CAFILE="$candidate"; break; }
  done
fi
[ -n "$CAFILE" ] || { echo "error: no CA bundle for signature verification — brew install ca-certificates" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The token only exists while SimplySign Desktop is logged in. "No slots" is the whole story.
if ! pkcs11-tool --module "$MODULE" -L 2>/dev/null | grep -q 'token label'; then
  echo "error: no SimplySign token — SimplySign Desktop is not logged in." >&2
  echo "       Open SimplySign Desktop (menu bar) → Log in with your e-mail and the code from the" >&2
  echo "       SimplySign phone app, then run this again." >&2
  exit 1
fi

# --- which certificate ---------------------------------------------------------------------------
# The token labels the certificate with its serial number, which changes at every reissue and
# renewal — so discover it rather than hard-code it. One certificate is the normal case.
pkcs11-tool --module "$MODULE" -O --type cert 2>/dev/null > "$WORK/certs.txt" || true
LABELS=()
while IFS= read -r line; do LABELS+=("$line"); done < <(sed -n 's/^  label: *//p' "$WORK/certs.txt")
ALIAS="${CODESIGN_ALIAS:-}"
if [ -z "$ALIAS" ]; then
  if [ "${#LABELS[@]}" -eq 1 ]; then
    ALIAS="${LABELS[0]}"
  elif [ "${#LABELS[@]}" -eq 0 ]; then
    echo "error: the SimplySign token holds no certificate." >&2; exit 1
  else
    echo "error: the token holds ${#LABELS[@]} certificates — set CODESIGN_ALIAS to one of:" >&2
    printf '       %s\n' "${LABELS[@]}" >&2
    exit 1
  fi
fi

# The chain jsign embeds: OUR certificate first (jsign takes the first entry as the signer), then
# the issuing CA. The token carries only the leaf; without the intermediate in the signature a
# machine that cannot reach Certum's repository cannot build the chain, and osslsigncode says
# "unable to get local issuer certificate".
pkcs11-tool --module "$MODULE" --read-object --type cert --label "$ALIAS" -o "$WORK/leaf.der" >/dev/null 2>&1 || {
  echo "error: could not read certificate '$ALIAS' from the token." >&2; exit 1; }
openssl x509 -inform DER -in "$WORK/leaf.der" -out "$WORK/leaf.pem" 2>/dev/null
SUBJECT="$(openssl x509 -in "$WORK/leaf.pem" -noout -subject | sed 's/^subject=//')"
ISSUER="$(openssl x509 -in "$WORK/leaf.pem" -noout -issuer | sed 's/^issuer=//')"
CA_SUBJECT="$(openssl x509 -in "$INTERMEDIATE" -noout -subject | sed 's/^subject=//')"
NOT_AFTER="$(openssl x509 -in "$WORK/leaf.pem" -noout -enddate | sed 's/^notAfter=//')"
if [ "$ISSUER" != "$CA_SUBJECT" ]; then
  echo "error: the certificate's issuer is not the bundled intermediate:" >&2
  echo "       issuer:       $ISSUER" >&2
  echo "       intermediate: $CA_SUBJECT" >&2
  echo "       Download the right one from the leaf's 'CA Issuers' URL and point CODESIGN_INTERMEDIATE at it." >&2
  exit 1
fi
# Never sign with the wrong certificate, however valid: the gate would refuse the package anyway.
EXPECTED_CN="${CODESIGN_EXPECTED_CN:-}"
if [ -z "$EXPECTED_CN" ] && [ -f "$ROOT/tools/windows/signing/expected-publisher.txt" ]; then
  EXPECTED_CN="$(head -1 "$ROOT/tools/windows/signing/expected-publisher.txt" | tr -d '\r')"
fi
LEAF_CN="$(openssl x509 -in "$WORK/leaf.pem" -noout -subject -nameopt RFC2253 | sed 's/^subject=//' | sed -n 's/^CN=\([^,]*\).*/CN=\1/p')"
if [ -n "$EXPECTED_CN" ] && [ "$LEAF_CN" != "$EXPECTED_CN" ]; then
  echo "error: the token's certificate is '$LEAF_CN' but tools/windows/signing/expected-publisher.txt says '$EXPECTED_CN'." >&2
  echo "       Wrong SimplySign account, or the publisher file needs updating after a renewal." >&2
  exit 1
fi
if ! openssl x509 -in "$WORK/leaf.pem" -noout -checkend 0 >/dev/null; then
  echo "error: the code-signing certificate expired on $NOT_AFTER — renew it at Certum before packing." >&2
  exit 1
fi
if ! openssl x509 -in "$WORK/leaf.pem" -noout -checkend $((30*24*3600)) >/dev/null; then
  echo "!!! the code-signing certificate expires on $NOT_AFTER — renew it soon."
fi
cat "$WORK/leaf.pem" "$INTERMEDIATE" > "$WORK/chain.pem"

printf 'name = SimplySign\nlibrary = %s\nslot = %s\n' "$MODULE" "$SLOT" > "$WORK/pkcs11.cfg"

echo ">>> signing ${#PES[@]} Windows files as $SUBJECT"
echo "    timestamp: $TSA   expires: $NOT_AFTER"

# --- sign, then verify each file independently ---------------------------------------------------
# --replace makes this idempotent: re-running over an already signed tree replaces the signature
# instead of stacking a second one. The empty store password is right for SimplySign: the Desktop
# login is the authentication, and the token reports no PIN.
for f in "${PES[@]}"; do
  rel="${f#$DIR/}"
  if ! jsign --storetype PKCS11 --keystore "$WORK/pkcs11.cfg" --storepass "" --alias "$ALIAS" \
        --certfile "$WORK/chain.pem" --alg SHA-256 --tsaurl "$TSA" --tsmode RFC3161 --replace \
        --name "$NAME" --url "$URL" "$f" > "$WORK/jsign.log" 2>&1; then
    echo "error: jsign failed on $rel:" >&2
    grep -v '^\s*at ' "$WORK/jsign.log" | head -8 >&2
    exit 1
  fi
  # Verify with a different implementation, against a public root bundle — the same check the
  # package verifier repeats on the packed file, so a broken chain never reaches a user.
  if ! osslsigncode verify -in "$f" -CAfile "$CAFILE" > "$WORK/verify.log" 2>&1 \
     || ! grep -q '^Signature verification: ok' "$WORK/verify.log"; then
    echo "error: $rel was signed but does not verify:" >&2
    grep -iE 'error|fail|verification' "$WORK/verify.log" | head -6 >&2
    exit 1
  fi
  echo "    signed  $rel"
done
echo ">>> ${#PES[@]} files signed and verified ($SUBJECT)"
