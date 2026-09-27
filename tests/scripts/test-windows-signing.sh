#!/usr/bin/env bash
# Tests for the Windows code-signing gate (#110):
#   tools/windows/sign-windows-payload.sh      signs at pack time (needs the SimplySign token —
#                                              NOT exercised here; pack-release is its test)
#   tools/windows/verify-windows-signatures.sh the gate verify-package.sh runs on the packed files
#
# What can be pinned without a certificate: the gate refuses an unsigned PE, and only downgrades
# it to a warning when pack-release has declared a dev pack (ALLOW_UNSIGNED_WINDOWS=1); the sign
# script's skip mode leaves files byte-identical and says so; and its preflight fails with a
# useful message when SimplySign is not installed, instead of a Java stack trace from jsign.
#
#   bash tests/scripts/test-windows-signing.sh
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SIGN="$ROOT/tools/windows/sign-windows-payload.sh"
VERIFY="$ROOT/tools/windows/verify-windows-signatures.sh"
PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); echo "  ok   $1"; }
fail() { FAIL=$((FAIL+1)); echo "  FAIL $1"; }
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT

# A minimal, valid, unsigned PE32+ image: enough header for osslsigncode to parse it and report
# "No signature found", which is exactly the case the gate exists for.
python3 - "$T/mini.exe" <<'PY'
import struct, sys
dos = bytearray(64); dos[0:2] = b'MZ'; struct.pack_into('<I', dos, 60, 64)
coff = struct.pack('<IHHIIIHH', 0x00004550, 0x8664, 0, 0, 0, 0, 240, 0x0022)
opt = bytearray(240)
struct.pack_into('<H', opt, 0, 0x20b); struct.pack_into('<Q', opt, 24, 0x140000000)
struct.pack_into('<I', opt, 32, 0x1000); struct.pack_into('<I', opt, 36, 0x200)
struct.pack_into('<H', opt, 40, 6); struct.pack_into('<H', opt, 48, 6)
struct.pack_into('<I', opt, 56, 0x1000); struct.pack_into('<I', opt, 60, 0x200)
struct.pack_into('<H', opt, 68, 3); struct.pack_into('<I', opt, 108, 16)
data = dos + coff + opt; data += b'\0' * (0x200 - len(data))
open(sys.argv[1], 'wb').write(data)
PY
mkdir -p "$T/tree/voice/whisper-bin-win"
cp "$T/mini.exe" "$T/tree/claude-console-hook.exe"
cp "$T/mini.exe" "$T/tree/voice/whisper-bin-win/ggml.dll"

if ! command -v osslsigncode >/dev/null 2>&1; then
  echo "  skip osslsigncode not installed (brew install osslsigncode) — gate tests need it"
  # The gate must still refuse rather than pass silently when it cannot check.
  out="$(bash "$VERIFY" "$T/tree" 2>&1)"; rc=$?
  [ $rc -eq 1 ] && grep -q 'osslsigncode is not installed' <<<"$out" && ok "gate refuses when osslsigncode is missing" || fail "gate without osslsigncode: rc=$rc: $out"
  out="$(ALLOW_UNSIGNED_WINDOWS=1 bash "$VERIFY" "$T/tree" 2>&1)"; rc=$?
  [ $rc -eq 0 ] && ok "dev pack passes without osslsigncode, with a warning" || fail "dev pack without osslsigncode: rc=$rc"
else
  out="$(bash "$VERIFY" "$T/tree" 2>&1)"; rc=$?
  if [ $rc -eq 1 ] && grep -q 'claude-console-hook.exe is UNSIGNED' <<<"$out" && grep -q 'ggml.dll is UNSIGNED' <<<"$out"; then
    ok "gate refuses a tree with unsigned .exe and .dll"
  else
    fail "gate on unsigned tree: rc=$rc: $out"
  fi
  out="$(ALLOW_UNSIGNED_WINDOWS=1 bash "$VERIFY" "$T/tree" 2>&1)"; rc=$?
  if [ $rc -eq 0 ] && grep -q 'warn  claude-console-hook.exe is UNSIGNED (allowed' <<<"$out"; then
    ok "ALLOW_UNSIGNED_WINDOWS=1 downgrades unsigned files to warnings"
  else
    fail "gate in dev mode: rc=$rc: $out"
  fi
fi

mkdir -p "$T/empty"
out="$(bash "$VERIFY" "$T/empty" 2>&1)"; rc=$?
[ $rc -eq 1 ] && grep -q 'no .exe or .dll' <<<"$out" && ok "gate refuses a tree with no Windows payload at all" || fail "empty tree: rc=$rc: $out"

# --- sign script: skip mode and preflight ---------------------------------------------------------
before="$(shasum -a 256 "$T/tree/claude-console-hook.exe" | cut -d' ' -f1)"
out="$(WINDOWS_SIGNING=skip bash "$SIGN" "$T/tree" ClaudeConsole 2>&1)"; rc=$?
after="$(shasum -a 256 "$T/tree/claude-console-hook.exe" | cut -d' ' -f1)"
if [ $rc -eq 0 ] && [ "$before" = "$after" ] && grep -q 'UNSIGNED' <<<"$out"; then
  ok "WINDOWS_SIGNING=skip exits 0, changes nothing, and says so loudly"
else
  fail "skip mode: rc=$rc same=$([ "$before" = "$after" ] && echo yes || echo no): $out"
fi

out="$(bash "$SIGN" "$T/empty" ClaudeConsole 2>&1)"; rc=$?
[ $rc -eq 1 ] && grep -q 'nothing to sign' <<<"$out" && ok "sign script refuses an empty tree" || fail "sign on empty tree: rc=$rc: $out"

out="$(bash "$SIGN" "$T/tree" NotAProduct 2>&1)"; rc=$?
[ $rc -eq 2 ] && ok "sign script rejects an unknown product" || fail "unknown product: rc=$rc: $out"

if command -v jsign >/dev/null 2>&1 && command -v osslsigncode >/dev/null 2>&1 && command -v pkcs11-tool >/dev/null 2>&1; then
  out="$(CODESIGN_PKCS11_MODULE="$T/absent.dylib" bash "$SIGN" "$T/tree" ClaudeConsole 2>&1)"; rc=$?
  [ $rc -eq 1 ] && grep -q 'SimplySign PKCS#11 library not found' <<<"$out" && ok "preflight names the missing SimplySign library" || fail "missing module: rc=$rc: $out"
else
  out="$(bash "$SIGN" "$T/tree" ClaudeConsole 2>&1)"; rc=$?
  [ $rc -eq 1 ] && grep -q 'brew install jsign osslsigncode opensc' <<<"$out" && ok "preflight names the missing tools" || fail "missing tools: rc=$rc: $out"
fi

# --- the committed CA and publisher (review P1: *.pem was gitignored and the intermediate never landed) ---
CA="$ROOT/tools/windows/signing/certum-code-signing-2021-ca.pem"
if [ -f "$CA" ] && ! grep -q 'PRIVATE KEY' "$CA" && openssl x509 -in "$CA" -noout >/dev/null 2>&1; then
  fp="$(openssl x509 -in "$CA" -noout -fingerprint -sha256 | sed 's/^.*=//')"
  if grep -q "$fp" "$ROOT/tools/windows/signing/README.md"; then
    ok "issuing CA is present, is a certificate (no key), and matches the fingerprint in the README"
  else
    fail "issuing CA fingerprint $fp is not the one documented in the README"
  fi
else
  fail "tools/windows/signing/certum-code-signing-2021-ca.pem missing, unparsable, or contains a key"
fi
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if git -C "$ROOT" ls-files --error-unmatch tools/windows/signing/certum-code-signing-2021-ca.pem >/dev/null 2>&1; then
    ok "issuing CA is tracked by git (a clean checkout can sign)"
  else
    fail "issuing CA is NOT tracked by git — check .gitignore's *.pem rule and its exception"
  fi
fi
[ "$(head -1 "$ROOT/tools/windows/signing/expected-publisher.txt" 2>/dev/null)" = "CN=Ravi Shankar S" ] \
  && ok "expected publisher is committed" || fail "expected-publisher.txt missing or changed"

# --- gate decisions against a mock verifier (review P2) ------------------------------------------
# The mock prints osslsigncode's real 'verify' layout: a "Signer's certificate:" block, then the
# timestamp chain (whose own Signer #0 is Certum's TSA and must not be mistaken for ours).
mkdir -p "$T/mock" "$T/signed"
cp "$T/mini.exe" "$T/signed/a.exe"; cp "$T/mini.exe" "$T/signed/b.dll"
cat > "$T/mock/osslsigncode" <<'MOCK'
#!/usr/bin/env bash
f=""; while [ $# -gt 0 ]; do [ "$1" = "-in" ] && f="$2"; shift; done
cn="${MOCK_CN:-CN=Ravi Shankar S}"
serial="${MOCK_SERIAL:-32FCB0F7499EE3E2FAE67F383CEF8C59}"
case "$(basename "$f")" in b.dll) serial="${MOCK_SERIAL_B:-$serial}";; esac
cat <<EOF
Message digest algorithm  : SHA256
Signer's certificate:
	Signer #0:
		Subject: $cn,O=Ravi Shankar S,L=Chennai,ST=Tamilnadu,C=IN
		Issuer : ${MOCK_ISSUER:-CN=Certum Code Signing 2021 CA,O=Asseco Data Systems S.A.,C=PL}
		Serial : $serial
Message digest algorithm: SHA256
	Timestamp time: Sep 27 11:15:19 2026 GMT
Timestamp verified using:
	Signer #0:
		Subject: CN=Certum Timestamp 2026,O=Asseco Data Systems S.A.,C=PL
${MOCK_TS:-Timestamp Server Signature verification: ok}
Signature verification: ok
Number of verified signatures: 1
EOF
exit "${MOCK_EXIT:-0}"
MOCK
chmod +x "$T/mock/osslsigncode"
gate() { PATH="$T/mock:$PATH" CODESIGN_CAFILE="$T/mini.exe" bash "$VERIFY" "$T/signed" 2>&1; }

out="$(gate)"; rc=$?
[ $rc -eq 0 ] && grep -q 'win-signer    2 of 2 Windows files signed by CN=Ravi Shankar S' <<<"$out" \
  && ok "gate passes two files signed by the expected certificate" || fail "good mock: rc=$rc: $out"
out="$(MOCK_CN='CN=Unrelated Publisher' gate)"; rc=$?
[ $rc -eq 1 ] && grep -q "expected 'CN=Ravi Shankar S' — not our certificate" <<<"$out" \
  && ok "gate refuses a valid signature from an unexpected publisher" || fail "unexpected publisher: rc=$rc: $out"
out="$(MOCK_ISSUER='CN=Some Other CA,O=Elsewhere,C=XX' gate)"; rc=$?
[ $rc -eq 1 ] && grep -q "from 'CN=Some Other CA" <<<"$out" \
  && ok "gate refuses our name issued by a different CA" || fail "wrong issuer: rc=$rc: $out"
out="$(MOCK_EXIT=1 gate)"; rc=$?
[ $rc -eq 1 ] && grep -q 'does not verify (osslsigncode exit 1)' <<<"$out" \
  && ok "gate treats a failed verifier exit as a failure even when the text says ok" || fail "verifier exit: rc=$rc: $out"
out="$(MOCK_SERIAL_B=0000000000000000000000000000FFFF gate)"; rc=$?
[ $rc -eq 1 ] && grep -q 'more than one certificate' <<<"$out" \
  && ok "gate refuses two files signed by different certificates with the same name" || fail "mismatched serials: rc=$rc: $out"
out="$(MOCK_TS='Timestamp Server Signature verification: failed' gate)"; rc=$?
[ $rc -eq 1 ] && grep -q 'no valid timestamp' <<<"$out" \
  && ok "gate refuses a signature without a valid timestamp" || fail "no timestamp: rc=$rc: $out"

echo "windows signing: $PASS passed, $FAIL failed"
[ $FAIL -eq 0 ]
