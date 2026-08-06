#!/bin/bash
# Claude Console — activity hook.
# Wired into ~/.claude/settings.json hooks; pushes Claude Code's current activity to files the
# keypad's Activity key reads. $1 = busy | waiting | done. Claude Code also pipes event JSON on
# stdin, which we don't need, so it's ignored.
#
#   UserPromptSubmit -> busy     (you just sent a turn)
#   PostToolUse      -> busy     (re-assert "working" after each tool/approval)
#   Notification     -> waiting  (Claude needs your input/permission)
#   Stop             -> done     (turn finished)
#
# Like the statusline handler, this writes a SHARED file plus a PER-TAB file keyed by the terminal
# tab's TTY, so the Activity key can follow whichever tab is frontmost across multiple sessions.
# Files live in the private /tmp/claude-console root (0700/0600 — matches the plugin).

umask 077
# CLAUDE_CONSOLE_IPC_ROOT is a test hook (tests/scripts/test-bridge-scripts.sh) so the suite can
# run against a temp root. Don't set it in your shell — the plugin always reads the default path.
ROOT="${CLAUDE_CONSOLE_IPC_ROOT:-/tmp/claude-console}"
ACTIVITY="$ROOT/activity"
mkdir -p "$ACTIVITY"
# Refuse a root another local user squatted before we could create it.
[ -O "$ROOT" ] || exit 0
chmod 700 "$ROOT" 2>/dev/null

STATE="${1:-done}"
TS="$(date +%s)"
PAYLOAD="$(printf '{"state":"%s","ts":%s}' "$STATE" "$TS")"

# Atomic write (tmp + mv) so the 500ms poller never reads a half-written file.
write() { printf '%s\n' "$PAYLOAD" > "$1.tmp.$$" && mv "$1.tmp.$$" "$1"; }

write "$ACTIVITY/shared.json"

# Per-tab file: climb the parent chain until a real controlling TTY appears (the hook may be
# spawned without one, showing "??").
tty_key=""
pid=$$
for _ in 1 2 3 4 5 6; do
  t="$(ps -o tty= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
  case "$t" in
    ''|'?'|'??') ;;
    *) tty_key="${t##*/}"; break ;;
  esac
  pid="$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
  { [ -z "$pid" ] || [ "$pid" -le 1 ]; } && break
done

[ -n "$tty_key" ] && write "$ACTIVITY/$tty_key.json"
