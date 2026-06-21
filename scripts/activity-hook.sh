#!/bin/bash
# Claude Console — activity hook.
# Wired into ~/.claude/settings.json hooks; pushes Claude Code's current activity to a file the
# keypad's Status key reads. $1 = busy | waiting | done. Claude Code also pipes event JSON on
# stdin, which we don't need, so it's ignored.
#
#   UserPromptSubmit -> busy     (you just sent a turn)
#   PostToolUse      -> busy     (re-assert "working" after each tool/approval)
#   Notification     -> waiting  (Claude needs your input/permission)
#   Stop             -> done     (turn finished)

STATE="${1:-done}"
FILE="/tmp/claude-console-activity.json"
TS="$(date +%s)"

# Atomic write (tmp + mv) so the 500ms poller never reads a half-written file.
printf '{"state":"%s","ts":%s}\n' "$STATE" "$TS" > "${FILE}.tmp.$$" && mv "${FILE}.tmp.$$" "$FILE"
