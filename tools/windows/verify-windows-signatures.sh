#!/usr/bin/env bash
# verify-windows-signatures.sh — every Windows executable and DLL under a directory carries a
# valid, timestamped Authenticode signature that chains to a public root.
#
#   bash tools/windows/verify-windows-signatures.sh <dir>
#
# Used by tools/verify-package.sh on the extracted package (the last gate before a package can be
# uploaded) and runnable by hand on any tree. Exit 1 on the first unsigned or broken file.
#
#   ALLOW_UNSIGNED_WINDOWS=1   downgrade an unsigned file to a warning (dev packs: pack-release
#                              sets this when WINDOWS_SIGNING=skip and names the package -unsigned)
#   CODESIGN_CAFILE            CA bundle to chain to (default: Homebrew's, then /etc/ssl)
#
# Prints one line per file and a summary naming the signer, so a release log shows WHO signed.
set -uo pipefail
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
SIGNERS=()
UNSIGNED=0
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT
for f in "${PES[@]}"; do
  rel="${f#$DIR/}"
  osslsigncode verify -in "$f" -CAfile "$CAFILE" > "$LOG" 2>&1
  if grep -q '^Signature verification: ok' "$LOG"; then
    signer="$(sed -n 's/^[[:space:]]*Subject: \(CN=[^,]*\).*/\1/p' "$LOG" | head -1)"
    SIGNERS+=("$signer")
    if grep -q '^Timestamp Server Signature verification: ok' "$LOG"; then
      echo "    win-signed    $rel  ($signer, timestamped)"
    else
      echo "    FAIL  $rel is signed but carries no valid timestamp — it will stop verifying when the certificate expires"
      STATUS=1
    fi
  elif grep -qi 'No signature found' "$LOG"; then
    UNSIGNED=$((UNSIGNED + 1))
    if [ "$ALLOW" = "1" ]; then
      echo "    warn  $rel is UNSIGNED (allowed: ALLOW_UNSIGNED_WINDOWS=1)"
    else
      echo "    FAIL  $rel is UNSIGNED — endpoint security quarantines unsigned helpers (#110)"
      STATUS=1
    fi
  else
    echo "    FAIL  $rel has a signature that does not verify:"
    grep -iE 'error|fail' "$LOG" | head -3 | sed 's/^/          /'
    STATUS=1
  fi
done

# One publisher per package: a stray file signed by someone else, or by an old certificate, is a
# mistake in the staging tree even if each signature verifies on its own.
if [ "${#SIGNERS[@]}" -gt 0 ]; then
  DISTINCT="$(printf '%s\n' "${SIGNERS[@]}" | sort -u)"
  if [ "$(printf '%s\n' "$DISTINCT" | wc -l | tr -d ' ')" -ne 1 ]; then
    echo "    FAIL  files are signed by more than one certificate:"
    printf '%s\n' "$DISTINCT" | sed 's/^/          /'
    STATUS=1
  else
    echo "    win-signer    ${#SIGNERS[@]} of ${#PES[@]} Windows files signed by $DISTINCT"
  fi
fi
exit "$STATUS"
