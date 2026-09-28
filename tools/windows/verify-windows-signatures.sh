#!/usr/bin/env bash
# verify-windows-signatures.sh — every Windows executable and DLL under a directory carries a
# valid, timestamped Authenticode signature from OUR publisher, chained to a public root.
#
#   bash tools/windows/verify-windows-signatures.sh <dir>
#
# Used by tools/verify-package.sh on the extracted package (the last gate before a package can be
# uploaded) and runnable by hand on any tree. Exit 1 on the first unsigned or wrong file.
#
# What "signed" means here, per file:
#   - osslsigncode exits 0 AND prints "Signature verification: ok" (a verifier that crashes is not
#     a pass)
#   - the timestamp countersignature verifies (an unstamped signature dies with the certificate)
#   - the signer's Subject CN is the expected publisher (tools/windows/signing/expected-publisher.txt,
#     or CODESIGN_EXPECTED_CN) — any other trusted publisher is a wrong file in our tree
#   - the signer's Issuer is the CA we commit and embed (tools/windows/signing/*.pem)
#   - across files, the signer certificate is the SAME one (issuer + serial), not merely the same
#     name: a stale certificate on one file is a mistake in the staging tree
#
#   ALLOW_UNSIGNED_WINDOWS=1   downgrade an UNSIGNED file to a warning (dev packs: pack-release sets
#                              this when WINDOWS_SIGNING=skip and names the package -unsigned). A
#                              wrong publisher or a broken signature is never downgraded.
#   CODESIGN_EXPECTED_CN       expected Subject CN (default: the committed expected-publisher.txt)
#   CODESIGN_INTERMEDIATE      PEM of the expected issuing CA (default: the committed one)
#   CODESIGN_CAFILE            CA bundle to chain to (default: Homebrew's, then /etc/ssl)
#
# Prints one line per file and a summary naming the signer, so a release log shows WHO signed.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
DIR="${1:-}"
[ -n "$DIR" ] && [ -d "$DIR" ] || { echo "usage: bash tools/windows/verify-windows-signatures.sh <dir>" >&2; exit 2; }
ALLOW="${ALLOW_UNSIGNED_WINDOWS:-0}"

PES=()
while IFS= read -r -d '' f; do PES+=("$f"); done < <(find "$DIR" -type f \( -iname '*.exe' -o -iname '*.dll' \) -print0 | sort -z)
if [ "${#PES[@]}" -eq 0 ]; then
  echo "    FAIL  no .exe or .dll under $DIR — the Windows payload is missing"
  exit 1
fi

if ! command -v osslsigncode >/dev/null 2>&1; then
  if [ "$ALLOW" = "1" ]; then
    echo "    warn  osslsigncode not installed — Windows signatures NOT checked (ALLOW_UNSIGNED_WINDOWS=1)"
    exit 0
  fi
  echo "    FAIL  osslsigncode is not installed, so Windows signatures cannot be checked — brew install osslsigncode"
  exit 1
fi

# Who must have signed. Committed next to the CA it chains to, so a clean checkout carries the
# whole expectation and a package signed by anyone else — however trusted — is refused.
EXPECTED_CN="${CODESIGN_EXPECTED_CN:-}"
if [ -z "$EXPECTED_CN" ] && [ -f "$ROOT/tools/windows/signing/expected-publisher.txt" ]; then
  EXPECTED_CN="$(head -1 "$ROOT/tools/windows/signing/expected-publisher.txt" | tr -d '\r')"
fi
if [ -z "$EXPECTED_CN" ]; then
  echo "    FAIL  no expected publisher — tools/windows/signing/expected-publisher.txt is missing (or set CODESIGN_EXPECTED_CN)"
  exit 1
fi
INTERMEDIATE="${CODESIGN_INTERMEDIATE:-$ROOT/tools/windows/signing/certum-code-signing-2021-ca.pem}"
EXPECTED_ISSUER=""
if [ -f "$INTERMEDIATE" ] && command -v openssl >/dev/null 2>&1; then
  # osslsigncode prints DNs most-specific-first with no spaces ("CN=…,O=…,C=…"); openssl's -nameopt
  # RFC2253 prints the same form, so the two compare as strings.
  EXPECTED_ISSUER="$(openssl x509 -in "$INTERMEDIATE" -noout -subject -nameopt RFC2253 2>/dev/null | sed 's/^subject=//')"
fi
if [ -z "$EXPECTED_ISSUER" ]; then
  echo "    FAIL  cannot read the expected issuing CA ($INTERMEDIATE) — it must be committed and parse with openssl"
  exit 1
fi

CAFILE="${CODESIGN_CAFILE:-}"
if [ -z "$CAFILE" ]; then
  for candidate in /opt/homebrew/etc/ca-certificates/cert.pem /usr/local/etc/ca-certificates/cert.pem /etc/ssl/cert.pem; do
    [ -f "$candidate" ] && { CAFILE="$candidate"; break; }
  done
fi
if [ -z "$CAFILE" ]; then
  echo "    FAIL  no CA bundle to verify Windows signatures against — brew install ca-certificates"
  exit 1
fi

STATUS=0
SIGNED=0
UNSIGNED=0
IDENTITIES=()
SIGNER_CN=""
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT
for f in "${PES[@]}"; do
  rel="${f#$DIR/}"
  osslsigncode verify -in "$f" -CAfile "$CAFILE" > "$LOG" 2>&1
  rc=$?
  if grep -qi 'No signature found' "$LOG"; then
    UNSIGNED=$((UNSIGNED + 1))
    if [ "$ALLOW" = "1" ]; then
      echo "    warn  $rel is UNSIGNED (allowed: ALLOW_UNSIGNED_WINDOWS=1)"
    else
      echo "    FAIL  $rel is UNSIGNED — endpoint security quarantines unsigned helpers (#110)"
      STATUS=1
    fi
    continue
  fi
  if [ "$rc" -ne 0 ] || ! grep -q '^Signature verification: ok' "$LOG"; then
    echo "    FAIL  $rel has a signature that does not verify (osslsigncode exit $rc):"
    grep -iE 'error|fail' "$LOG" | head -3 | sed 's/^/          /'
    STATUS=1
    continue
  fi
  if ! grep -q '^Timestamp Server Signature verification: ok' "$LOG"; then
    echo "    FAIL  $rel is signed but carries no valid timestamp — it will stop verifying when the certificate expires"
    STATUS=1
    continue
  fi
  # The signer block: the first "Signer #0" under "Signer's certificate:". The timestamp chain
  # further down has its own Signer #0 (Certum's TSA), which must not be read as ours.
  block="$(awk '/^Signer.s certificate:/{p=1} p{print} /^Message digest algorithm:/{if(p) exit}' "$LOG")"
  subject="$(printf '%s\n' "$block" | sed -n 's/^[[:space:]]*Subject: //p' | head -1)"
  issuer="$(printf '%s\n' "$block" | sed -n 's/^[[:space:]]*Issuer *: //p' | head -1)"
  serial="$(printf '%s\n' "$block" | sed -n 's/^[[:space:]]*Serial *: //p' | head -1)"
  cn="$(printf '%s\n' "$subject" | sed -n 's/^CN=\([^,]*\).*/CN=\1/p')"
  if [ "$cn" != "$EXPECTED_CN" ]; then
    echo "    FAIL  $rel is signed by '$subject', expected '$EXPECTED_CN' — not our certificate"
    STATUS=1
    continue
  fi
  if [ "$issuer" != "$EXPECTED_ISSUER" ]; then
    echo "    FAIL  $rel is signed by a certificate from '$issuer', expected '$EXPECTED_ISSUER'"
    STATUS=1
    continue
  fi
  if [ -z "$serial" ]; then
    echo "    FAIL  $rel: could not read the signer's serial number from osslsigncode's output"
    STATUS=1
    continue
  fi
  SIGNED=$((SIGNED + 1))
  SIGNER_CN="$cn"
  IDENTITIES+=("$issuer serial $serial")
  echo "    win-signed    $rel  ($cn, timestamped)"
done

# One certificate per package: the same subject name on a renewed or reissued certificate is a
# different certificate, and a file signed with the old one is a stale artefact in the tree.
if [ "${#IDENTITIES[@]}" -gt 0 ]; then
  DISTINCT="$(printf '%s\n' "${IDENTITIES[@]}" | sort -u)"
  if [ "$(printf '%s\n' "$DISTINCT" | wc -l | tr -d ' ')" -ne 1 ]; then
    echo "    FAIL  files are signed by more than one certificate:"
    printf '%s\n' "$DISTINCT" | sed 's/^/          /'
    STATUS=1
  else
    echo "    win-signer    $SIGNED of ${#PES[@]} Windows files signed by $SIGNER_CN ($DISTINCT)"
  fi
fi
exit "$STATUS"
