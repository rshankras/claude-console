#!/bin/bash
# Codex Console — lifecycle hook.
#
# Wired into ~/.codex/hooks.json (a file of our own, never an edit to the user's config.toml);
# Codex runs it for each lifecycle event and pipes that event's JSON on stdin. $1 is the event name.
#
#   SessionStart       -> idle       (a session exists)
#   UserPromptSubmit   -> busy       (you just sent a turn)
#   PreToolUse         -> busy       (a tool is about to run)
#   PermissionRequest  -> waiting    (a tool is waiting on you — this is the amber/red key)
#   PostToolUse        -> busy       (tool finished, turn continues)
#   Stop               -> idle       (turn finished; payload carries last_assistant_message)
#   SessionEnd         -> dead       (session is over)
#
# THE PAYLOAD IS STORED VERBATIM AND PARSED IN C#. Same division of labour as the Claude Code
# scripts, and for the same reason: no jq dependency, and no bash JSON parsing to get subtly wrong.
# It also means a field Codex adds later is already on disk when the plugin learns to read it —
# which matters here, because Codex documents neither these payloads nor their stability.
#
# Unlike Claude Code there is no statusline, so EVERY event carries the state the keys display
# (model, cwd, permission_mode). One file per session is therefore enough; C# maps event -> activity.
#
# Codex trusts hooks BY HASH and re-prompts when the command changes, so this file's path and
# contents are part of the install contract: change it and every user is asked to re-approve.
# Keep it a stable launcher — put logic that churns in the plugin, not here.
#
# Never blocks Codex: all failures are swallowed and it always prints {} and exits 0.

umask 077

# CODEX_CONSOLE_IPC_ROOT is a test hook (tests/scripts/test-codex-hook.sh) so the suite can run
# against a temp root. Don't set it in your shell — the plugin always reads the default path.
ROOT="${CODEX_CONSOLE_IPC_ROOT:-/tmp/codex-console}"
SESSIONS="$ROOT/sessions"

emit_and_exit() {
    printf '{}'
    exit 0
}

EVENT="${1:-Unknown}"
PAYLOAD="$(cat)"

mkdir -p "$SESSIONS" 2>/dev/null || emit_and_exit
# Refuse a root another local user squatted before we could create it.
[ -O "$ROOT" ] || emit_and_exit
chmod 700 "$ROOT" 2>/dev/null

# Session keys must identify the terminal the keypad can focus, not the hook subprocess.
# Like Claude Console's activity/statusline hooks, walk a bounded parent chain: Codex can
# detach its hook (and intermediate shells) from the controlling terminal. Checking only $$
# then writes every session into shared.json and loses Thinking/approval state on the grid.
TTY="shared"
pid=$$
for _ in 1 2 3 4 5 6; do
    t="$(ps -o tty= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
    case "$t" in
        ''|'?'|'??') ;;
        *) TTY="${t##*/}"; break ;;
    esac
    parent="$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d '[:space:]')"
    case "$parent" in ''|*[!0-9]*) break ;; esac
    { [ "$parent" -le 1 ] || [ "$parent" = "$pid" ]; } && break
    pid="$parent"
done

TS="$(date +%s)"

# The payload is already JSON, so it embeds directly. If it is empty or unreadable, record the
# event anyway — knowing a session went busy is still worth a key, even with no detail behind it.
case "$PAYLOAD" in
    '{'*) BODY="$PAYLOAD" ;;
    *)    BODY='null' ;;
esac

TMP="$SESSIONS/.$TTY.$$.tmp"
printf '{"schema":1,"agent":"codex-cli","transport":"hook","event":"%s","ts":%s,"payload":%s}\n' \
    "$EVENT" "$TS" "$BODY" > "$TMP" 2>/dev/null || emit_and_exit
chmod 600 "$TMP" 2>/dev/null

# Atomic replace: a reader polling this file must never catch a half-written one.
mv -f "$TMP" "$SESSIONS/$TTY.json" 2>/dev/null || rm -f "$TMP" 2>/dev/null

emit_and_exit
