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
  # `script` needs a real terminal to allocate from. Under a redirected stdout, a pipe, or a CI
  # runner it fails with "tcgetattr/ioctl: Operation not supported on socket" — and exec'ing into
  # a command that dies took the whole suite down with it. Probe first, and carry on without a pty
  # when there isn't one: the TTY-keyed assertions fall back to the shared path rather than
  # failing, which is worth more than a suite that only runs interactively.
  if script -q /dev/null true >/dev/null 2>&1; then
    export CC_TESTS_PTY=1
    exec script -q /dev/null bash "$0" "$@"
  fi
  printf '  note no pty available — TTY-keyed cases fall back to the shared path\n'
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

echo "activity-hook.sh — permission mode"
PERM_PAYLOAD='{"hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"git push --force"}}'
printf '%s' "$PERM_PAYLOAD" | bash "$ACTIVITY_HOOK" permission >/dev/null 2>&1

# "permission" must land as the waiting STATE (the plugin only knows busy/waiting/ready)...
case "$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)" in
  *'"state":"waiting"'*) ok "permission maps to the waiting state" ;;
  *) bad "permission maps to the waiting state" "got [$(cat "$CLAUDE_CONSOLE_IPC_ROOT/activity/shared.json" 2>/dev/null)]" ;;
esac

if [ -n "$TTY_KEY" ]; then
  PENDING="$CLAUDE_CONSOLE_IPC_ROOT/activity/pending-$TTY_KEY.json"
  # ...and the payload must be captured verbatim, since C# parses it (no jq in the hook).
  check_file "captures the permission payload" "$PENDING"
  check_eq   "payload is stored verbatim" "$PERM_PAYLOAD" "$(cat "$PENDING" 2>/dev/null)"
  check_eq   "pending file is owner-only (600)" "600" "$(mode_of "$PENDING")"

  # A stale pending file would leave a red badge lit after the command already ran, so any
  # non-permission event must clear it.
  printf '{}' | bash "$ACTIVITY_HOOK" busy >/dev/null 2>&1
  if [ -f "$PENDING" ]; then
    bad "moving on clears the pending payload" "pending file survived a busy event"
  else
    ok "moving on clears the pending payload"
  fi
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

# --- uninstall.sh --unwire: surgical, and only ours (#31) ------------------------------------------
# HOME is already the temp root, so this never touches the real settings.json.
echo
echo "uninstall.sh --unwire"
UNINSTALL="$REPO/scripts/uninstall.sh"
mkdir -p "$HOME/.claude/claude-console"
cat > "$HOME/.claude/settings.json" <<'JSON'
{
  "model": "opus",
  "statusLine": { "type": "command", "command": "bash /tmp/x/.claude/claude-console/scripts/statusline-handler.sh" },
  "hooks": {
    "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "bash /tmp/x/.claude/claude-console/scripts/activity-hook.sh busy" } ] } ],
    "PostToolUse": [ { "matcher": "*", "hooks": [
      { "type": "command", "command": "bash /tmp/x/.claude/claude-console/scripts/activity-hook.sh busy" },
      { "type": "command", "command": "echo user-hook" } ] } ],
    "SessionStart": [ { "hooks": [ { "type": "command", "command": "echo mine" } ] } ]
  }
}
JSON
printf 'my-status --flag' > "$HOME/.claude/claude-console/statusline-chain"

# A dry run of the full cleanup must report the wiring and change nothing.
BEFORE_SUM="$(cksum < "$HOME/.claude/settings.json")"
DRY_OUT="$(bash "$UNINSTALL" --dry-run 2>&1)"
check_eq "dry run reports the wiring it would remove" "1" "$(printf '%s' "$DRY_OUT" | grep -c 'would remove 2 claude-console hook')"
check_eq "dry run changes nothing" "$BEFORE_SUM" "$(cksum < "$HOME/.claude/settings.json")"

bash "$UNINSTALL" --unwire >/dev/null 2>&1
check_eq "--unwire exits 0" "0" "$?"
j() { python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(eval(sys.argv[2]))" "$HOME/.claude/settings.json" "$1" 2>/dev/null; }
check_eq "our statusLine is replaced by the chained original" "my-status --flag" "$(j "d['statusLine']['command']")"
check_eq "no claude-console reference survives" "0" "$(grep -c 'claude-console' "$HOME/.claude/settings.json")"
check_eq "the event we owned outright is gone" "False" "$(j "'UserPromptSubmit' in d['hooks']")"
check_eq "the user's hook sharing our entry survives, alone" "['echo user-hook']" "$(j "[h['command'] for h in d['hooks']['PostToolUse'][0]['hooks']]")"
check_eq "the user's own event survives" "echo mine" "$(j "d['hooks']['SessionStart'][0]['hooks'][0]['command']")"
check_eq "unrelated settings survive" "opus" "$(j "d['model']")"
check_file "a rolling backup was written first" "$HOME/.claude/settings.json.claude-console.bak"
check_eq "the backup is the pre-unwire state" "1" "$(grep -c 'statusline-handler' "$HOME/.claude/settings.json.claude-console.bak")"
check_file "the opt-out is set so the plugin does not wire it back" "$HOME/.claude/claude-console/no-autowire"
AFTER_SUM="$(cksum < "$HOME/.claude/settings.json")"
bash "$UNINSTALL" --unwire >/dev/null 2>&1
check_eq "a second --unwire is a no-op" "$AFTER_SUM" "$(cksum < "$HOME/.claude/settings.json")"

echo
printf 'bridge scripts: %d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
