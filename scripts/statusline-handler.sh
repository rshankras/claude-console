#!/bin/bash
# Claude Console Statusline Handler
# Called by Claude Code on every assistant message with the live session JSON on stdin.
# Writes the state to a SHARED file (fallback / single session) AND a PER-TAB file keyed by the
# terminal tab's TTY, so the keypad's live keys (Model/Cost/Context) can follow whichever tab is
# frontmost when you run several Claude Code sessions at once. macOS/Linux only.

STATE_FILE="/tmp/claude-console-state.json"

# Claude Code pipes the session JSON in — capture it once.
JSON="$(cat)"

# Atomic write (tmp + mv) so the plugin's 500ms poller never reads a half-written file.
write() { printf '%s' "$JSON" > "$1.tmp.$$" && mv "$1.tmp.$$" "$1"; }

# Shared file (last-writer-wins) — the plugin uses this when it can't match the frontmost tab.
write "$STATE_FILE"

# Per-tab file: find the controlling TTY of this Claude session. The hook may be spawned without
# its own controlling terminal (shows "??"), so climb the parent chain until a real tty appears.
tty_key=""
pid=$$
for _ in 1 2 3 4 5 6; do
  t="$(ps -o tty= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
  case "$t" in
    ''|'?'|'??') ;;                          # no tty here — go up a level
    *) tty_key="${t##*/}"; break ;;          # e.g. /dev/ttys003 or ttys003 -> ttys003
  esac
  pid="$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
  { [ -z "$pid" ] || [ "$pid" -le 1 ]; } && break
done

[ -n "$tty_key" ] && write "/tmp/claude-console-state-$tty_key.json"
