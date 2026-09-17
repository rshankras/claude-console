#!/bin/bash
# Claude Console — activity hook.
# Wired into ~/.claude/settings.json hooks; pushes Claude Code's current activity to files the
# keypad's Activity key reads. $1 = busy | waiting | done. Claude Code also pipes event JSON on
# stdin, which we don't need, so it's ignored.
#
#   UserPromptSubmit  -> busy        (you just sent a turn)
#   PostToolUse       -> busy        (re-assert "working" after each tool/approval)
#   Notification      -> waiting     (Claude needs your input/permission)
#   Stop              -> done        (turn finished)
#   PermissionRequest -> permission  (a tool is waiting for approval — payload captured, see below)
#
# "permission" is the only mode that reads stdin. PermissionRequest carries `tool_name` and
# `tool_input` (with `tool_input.command` for Bash), which is what lets the keypad tell a routine
# approval from `git push --force`. Notification deliberately isn't used for this: it carries no
# tool name, and for permission prompts the CLI delays it by ~6 seconds — approve faster than that
# and it never fires at all.
#
# Like the statusline handler, this writes a SHARED file plus a PER-TAB file keyed by the terminal
# tab's TTY, so the Activity key can follow whichever tab is frontmost across multiple sessions.
# Files live in the private /tmp/claude-console root (0700/0600 — matches the plugin).

umask 077

# Liveness (#73). An Options+ uninstall removes the plugin and nothing else — the SDK gives a
# plugin no uninstall moment — so this wiring outlived the plugin and kept recording every prompt
# and permission request with nothing left to read them. The plugin writes where it is installed
# on every load; once that place has been gone for over a minute across two runs, the plugin was
# uninstalled: take the wiring out (surgically, with the rolling backup, via the cleanup script the
# plugin installed beside us), leave a breadcrumb, and stop. One missing run is not enough — an
# Options+ update replaces the folder, and the service restarts on its own. While it is missing,
# record nothing: there is no one to read it.
RUNTIME="$HOME/.claude/claude-console"
if [ -s "$RUNTIME/plugin-home" ]; then
  plugin_home="$(cat "$RUNTIME/plugin-home" 2>/dev/null)"
  missing="$RUNTIME/plugin-missing-since"
  if [ -n "$plugin_home" ] && [ ! -e "$plugin_home" ]; then
    now="$(date +%s)"
    first="$(cat "$missing" 2>/dev/null)"
    case "$first" in ''|*[!0-9]*) first="" ;; esac
    if [ -z "$first" ]; then
      printf '%s' "$now" > "$missing"
    elif [ $((now - first)) -ge 60 ]; then
      if [ -f "$RUNTIME/scripts/uninstall.sh" ] && bash "$RUNTIME/scripts/uninstall.sh" --unwire >/dev/null 2>&1; then
        rm -f "$missing"
        printf '%s\n' "$now" > "$RUNTIME/unwired-after-uninstall"
      fi
    fi
    exit 0
  fi
  rm -f "$missing" 2>/dev/null
fi

# CLAUDE_CONSOLE_IPC_ROOT is a test hook (tests/scripts/test-bridge-scripts.sh) so the suite can
# run against a temp root. Don't set it in your shell — the plugin always reads the default path.
ROOT="${CLAUDE_CONSOLE_IPC_ROOT:-/tmp/claude-console}"
ACTIVITY="$ROOT/activity"
mkdir -p "$ACTIVITY"
# Refuse a root another local user squatted before we could create it.
[ -O "$ROOT" ] || exit 0
chmod 700 "$ROOT" 2>/dev/null

STATE="${1:-done}"

# The pending payload is captured raw here and parsed in C# — no jq dependency, same division of
# labour as the statusline handler.
PENDING=""
if [ "$STATE" = "permission" ]; then
  PENDING="$(cat)"
  STATE="waiting"
fi

TS="$(date +%s)"
PAYLOAD="$(printf '{"state":"%s","ts":%s}' "$STATE" "$TS")"

# Atomic write (tmp + mv) so the 500ms poller never reads a half-written file.
write() { printf '%s\n' "$PAYLOAD" > "$1.tmp.$$" && mv "$1.tmp.$$" "$1"; }
write_pending() { printf '%s' "$PENDING" > "$1.tmp.$$" && mv "$1.tmp.$$" "$1"; }

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

# Record which tool is awaiting approval, or clear it the moment the session moves ON — a stale
# pending file would leave a red badge lit after the command it described has already run.
#
# CLEAR ONLY ON busy/done, NEVER on a bare "waiting". A permission menu fires TWO events: the
# immediate PermissionRequest (which lands here as "waiting" WITH a payload) and then a plain
# Notification the CLI delays by ~6s (which lands as "waiting" with NO payload, while the same
# menu is still on screen). Deleting the payload on that second event erased a live approval:
# the amber badge went dark mid-prompt (#51's regression), and the Yes/No keys — which key off
# the payload to tell a menu from a plain question (#21) — lost the one signal that a menu was
# up. Only busy (a new turn / a tool completing) or done (turn ended) means the moment passed.
if [ -n "$tty_key" ]; then
  if [ -n "$PENDING" ]; then
    write_pending "$ACTIVITY/pending-$tty_key.json"
  elif [ "$STATE" = "busy" ] || [ "$STATE" = "done" ]; then
    rm -f "$ACTIVITY/pending-$tty_key.json"
  fi
  # STATE=waiting with no payload: leave any existing pending file intact — the menu is still up.
fi
