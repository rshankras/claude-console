#!/usr/bin/env bash
# Tests for the two bridge writer scripts (scripts/statusline-handler.sh, scripts/activity-hook.sh).
#
# They run against a temp IPC root via CLAUDE_CONSOLE_IPC_ROOT, so the real /tmp/claude-console
# and any live Claude Code session are untouched. What matters here is what the C# side depends
# on: the file PATHS, the JSON payload, and the owner-only PERMISSIONS.
#
#   bash tests/scripts/test-bridge-scripts.sh
set -u

# The per-tab files are keyed by the controlling TTY, so without a pty that whole path silently
# skips — exactly the logic most worth testing. Re-exec under one when we don't have it (CI,
# non-interactive runners). `script -q /dev/null` propagates the child's exit status.
if [ ! -t 0 ] && [ -z "${CC_TESTS_PTY:-}" ]; then
  export CC_TESTS_PTY=1
  exec script -q /dev/null bash "$0" "$@"
fi

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATUSLINE="$REPO/scripts/statusline-handler.sh"
ACTIVITY_HOOK="$REPO/scripts/activity-hook.sh"

PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); printf '  ok   %s\n' "$1"; }
bad()  { FAIL=$((FAIL+1)); printf '  FAIL %s\n     %s\n' "$1" "$2"; }

check_eq() { # name expected actual
  if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "expected [$2], got [$3]"; fi
}
check_file() { # name path
  if [ -f "$2" ]; then ok "$1"; else bad "$1" "missing file $2"; fi
}
mode_of() { stat -f '%Lp' "$1" 2>/dev/null || stat -c '%a' "$1" 2>/dev/null; }

ROOT="$(mktemp -d "${TMPDIR:-/tmp}/cc-script-tests.XXXXXX")"
trap 'rm -rf "$ROOT"' EXIT
export CLAUDE_CONSOLE_IPC_ROOT="$ROOT/ipc"
# Keep the statusline chain-through out of the picture (it reads the real ~/.claude).
export HOME="$ROOT/home"
mkdir -p "$HOME"

TTY_KEY="$(ps -o tty= -p $$ 2>/dev/null | tr -d '[:space:]')"
TTY_KEY="${TTY_KEY##*/}"
case "$TTY_KEY" in ''|'?'|'??') TTY_KEY="" ;; esac

echo "statusline-handler.sh"
SESSION_JSON='{"model":{"display_name":"Opus 5"},"cost":{"total_cost_usd":1.25},"workspace":{"current_dir":"/Users/x/proj"}}'
printf '%s' "$SESSION_JSON" | bash "$STATUSLINE" >/dev/null 2>&1

check_file "writes the shared session file" "$CLAUDE_CONSOLE_IPC_ROOT/sessions/shared.json"
check_eq   "shared file holds the session JSON verbatim" \
           "$SESSION_JSON" "$(cat "$CLAUDE_CONSOLE_IPC_ROOT/sessions/shared.json" 2>/dev/null)"
check_eq   "IPC root is owner-only (700)" "700" "$(mode_of "$CLAUDE_CONSOLE_IPC_ROOT")"
check_eq   "session file is owner-only (600)" \
           "600" "$(mode_of "$CLAUDE_CONSOLE_IPC_ROOT/sessions/shared.json")"

if [ -n "$TTY_KEY" ]; then
  check_file "writes the per-tab session file" "$CLAUDE_CONSOLE_IPC_ROOT/sessions/$TTY_KEY.json"
  check_eq   "per-tab session file is owner-only (600)" \
             "600" "$(mode_of "$CLAUDE_CONSOLE_IPC_ROOT/sessions/$TTY_KEY.json")"
else
  echo "  skip per-tab session file (no controlling tty in this shell)"
fi

# No temp files may survive the atomic write — the C# poller globs this directory.
LEFTOVER="$(find "$CLAUDE_CONSOLE_IPC_ROOT/sessions" -name '*.tmp.*' 2>/dev/null | wc -l | tr -d ' ')"
check_eq "atomic write leaves no .tmp files" "0" "$LEFTOVER"

echo "activity-hook.sh"
for state in busy waiting done; do
  printf '{}' | bash "$ACTIVITY_HOOK" "$state" >/dev/null 2>&1
  payload="$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)"
  case "$payload" in
    *"\"state\":\"$state\""*) ok "records state=$state" ;;
    *) bad "records state=$state" "got [$payload]" ;;
  esac
done

payload="$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)"
case "$payload" in
  *'"ts":'[0-9]*) ok "stamps a unix timestamp" ;;
  *) bad "stamps a unix timestamp" "got [$payload]" ;;
esac

check_eq "activity file is owner-only (600)" \
         "600" "$(mode_of "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json")"

# Defaults to "done" so a mis-wired hook can never pin the key on "Working".
printf '{}' | bash "$ACTIVITY_HOOK" >/dev/null 2>&1
case "$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)" in
  *'"state":"done"'*) ok "defaults to done with no argument" ;;
  *) bad "defaults to done with no argument" "got [$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)]" ;;
esac

if [ -n "$TTY_KEY" ]; then
  check_file "writes the per-tab activity file" "$CLAUDE_CONSOLE_IPC_ROOT/activity/$TTY_KEY.json"
fi

echo "hostile root"
# A root owned by someone else (simulated here by a plain file where the dir should be) must make
# the scripts bail rather than write state into it.
HOSTILE="$ROOT/hostile"
CLAUDE_CONSOLE_IPC_ROOT="$HOSTILE" bash -c 'printf "{}" | bash "$0" busy' "$ACTIVITY_HOOK" >/dev/null 2>&1
if [ -d "$HOSTILE" ] && [ ! -O "$HOSTILE" ]; then
  bad "bails on a root it does not own" "wrote into a foreign root"
else
  ok "creates and owns its root, or bails"
fi

echo
printf 'bridge scripts: %d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
