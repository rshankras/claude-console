#!/usr/bin/env bash
# Tests for the Codex lifecycle hook (scripts/codex-hook.sh).
#
# Runs against a temp IPC root via CODEX_CONSOLE_IPC_ROOT, so /tmp/codex-console and any live
# Codex session are untouched. What matters is what the C# side depends on: the file PATH keyed by
# TTY, the envelope fields, the payload surviving VERBATIM, owner-only PERMISSIONS — and above all
# that the hook can never block or break Codex.
#
#   bash tests/scripts/test-codex-hook.sh
set -u

# The session key comes from the hook's controlling TTY, so without a pty that path silently falls
# back to "shared" — exactly the logic worth testing. Re-exec under one when we don't have it.
if [ ! -t 0 ] && [ -z "${CX_TESTS_PTY:-}" ]; then
  export CX_TESTS_PTY=1
  exec script -q /dev/null bash "$0" "$@"
fi

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HOOK="$REPO/scripts/codex-hook.sh"

PASS=0; FAIL=0
ok()  { PASS=$((PASS+1)); printf '  ok   %s\n' "$1"; }
bad() { FAIL=$((FAIL+1)); printf '  FAIL %s\n     %s\n' "$1" "$2"; }
eq()  { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "expected [$2], got [$3]"; fi; }

ROOT="$(mktemp -d "${TMPDIR:-/tmp}/codex-hook-tests.XXXXXX")"
export CODEX_CONSOLE_IPC_ROOT="$ROOT"
trap 'rm -rf "$ROOT"' EXIT

TTY_NAME="$(basename "$(ps -o tty= -p $$ 2>/dev/null | tr -d ' \t')" 2>/dev/null)"
[ -n "$TTY_NAME" ] && [ "$TTY_NAME" != "??" ] || TTY_NAME="shared"
STATE="$ROOT/sessions/$TTY_NAME.json"

# A real payload, trimmed from a captured codex-cli 0.145.0 PermissionRequest.
PERMISSION_PAYLOAD='{"session_id":"01a01324-6044-7bb3-8455-6a9b96c55a3c","turn_id":"01a01324-908b","cwd":"/Users/dev/project","hook_event_name":"PermissionRequest","model":"gpt-5.6-terra","permission_mode":"default","tool_name":"apply_patch","tool_input":{"command":"*** Begin Patch\n*** Add File: /tmp/codex-probe.txt\n+hello\n*** End Patch"}}'

echo "codex-hook.sh"

# --- the envelope -------------------------------------------------------------------------------
printf '%s' "$PERMISSION_PAYLOAD" | bash "$HOOK" PermissionRequest > /dev/null

if [ -f "$STATE" ]; then ok "writes the per-session state file"; else bad "writes the per-session state file" "missing $STATE"; fi

BODY="$(cat "$STATE" 2>/dev/null)"
case "$BODY" in *'"event":"PermissionRequest"'*) ok "records the event name" ;; *) bad "records the event name" "$BODY" ;; esac
case "$BODY" in *'"agent":"codex-cli"'*)         ok "tags the agent" ;;        *) bad "tags the agent" "$BODY" ;; esac
case "$BODY" in *'"schema":1'*)                  ok "stamps a schema version" ;; *) bad "stamps a schema version" "$BODY" ;; esac
case "$BODY" in *'"ts":'[0-9]*)                  ok "stamps a unix timestamp" ;; *) bad "stamps a unix timestamp" "$BODY" ;; esac

# --- the payload survives intact ------------------------------------------------------------------
# The whole point of storing it verbatim: C# extracts the fields, and a field Codex adds later is
# already on disk. If bash mangles it, the adapter is reading fiction.
case "$BODY" in *'"tool_name":"apply_patch"'*) ok "keeps tool_name" ;;               *) bad "keeps tool_name" "$BODY" ;; esac
case "$BODY" in *'*** Add File: /tmp/codex-probe.txt'*) ok "keeps the patch body verbatim" ;; *) bad "keeps the patch body verbatim" "$BODY" ;; esac
case "$BODY" in *'"model":"gpt-5.6-terra"'*)   ok "keeps the model" ;;               *) bad "keeps the model" "$BODY" ;; esac
case "$BODY" in *'"cwd":"/Users/dev/project"'*) ok "keeps the working directory" ;;  *) bad "keeps the working directory" "$BODY" ;; esac

# --- permissions --------------------------------------------------------------------------------
# Payloads carry the user's prompts and the commands an agent wants to run. Not world-readable.
eq "state file is owner-only (600)" "600" "$(stat -f '%Lp' "$STATE" 2>/dev/null || stat -c '%a' "$STATE" 2>/dev/null)"
eq "root dir is owner-only (700)"   "700" "$(stat -f '%Lp' "$ROOT" 2>/dev/null || stat -c '%a' "$ROOT" 2>/dev/null)"

# --- every event lands --------------------------------------------------------------------------
for ev in SessionStart UserPromptSubmit PreToolUse PostToolUse Stop SessionEnd; do
  printf '{"hook_event_name":"%s","model":"gpt-5.6-terra"}' "$ev" | bash "$HOOK" "$ev" > /dev/null
  case "$(cat "$STATE" 2>/dev/null)" in
    *"\"event\":\"$ev\""*) ok "records event $ev" ;;
    *) bad "records event $ev" "$(cat "$STATE" 2>/dev/null)" ;;
  esac
done

# --- it can never break Codex ---------------------------------------------------------------------
# A hook that errors, hangs or prints garbage sits on Codex's critical path. These are the cases
# that would do it.
OUT="$(printf '%s' "$PERMISSION_PAYLOAD" | bash "$HOOK" Stop 2>/dev/null)"; RC=$?
eq "exits 0" "0" "$RC"
eq "prints an empty JSON object" "{}" "$OUT"

OUT="$(printf '' | bash "$HOOK" Stop 2>/dev/null)"; RC=$?
eq "survives an empty payload (exit)" "0" "$RC"
eq "survives an empty payload (output)" "{}" "$OUT"
case "$(cat "$STATE" 2>/dev/null)" in
  *'"payload":null'*) ok "records a null payload rather than invalid JSON" ;;
  *) bad "records a null payload rather than invalid JSON" "$(cat "$STATE" 2>/dev/null)" ;;
esac

OUT="$(printf 'not json at all' | bash "$HOOK" Stop 2>/dev/null)"; RC=$?
eq "survives a non-JSON payload (exit)" "0" "$RC"
eq "survives a non-JSON payload (output)" "{}" "$OUT"

OUT="$(printf '%s' "$PERMISSION_PAYLOAD" | bash "$HOOK" 2>/dev/null)"; RC=$?
eq "survives a missing event name (exit)" "0" "$RC"

# A root owned by someone else must be refused, not written into.
HOSTILE="$ROOT-hostile"
mkdir -p "$HOSTILE/sessions"
OUT="$(CODEX_CONSOLE_IPC_ROOT="$HOSTILE" printf '%s' "$PERMISSION_PAYLOAD" | CODEX_CONSOLE_IPC_ROOT="$HOSTILE" bash "$HOOK" Stop 2>/dev/null)"; RC=$?
eq "hostile-root check still exits 0" "0" "$RC"
rm -rf "$HOSTILE"

# --- no litter ------------------------------------------------------------------------------------
TMPCOUNT="$(find "$ROOT/sessions" -name '.*.tmp' 2>/dev/null | wc -l | tr -d ' ')"
eq "atomic write leaves no .tmp files" "0" "$TMPCOUNT"

echo
if [ "$FAIL" -eq 0 ]; then
  echo "codex hook: $PASS passed, 0 failed"
  exit 0
else
  echo "codex hook: $PASS passed, $FAIL FAILED"
  exit 1
fi
