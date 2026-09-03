# The live status bridge — what is written, and wiring it by hand

The live keys (Cost / Model / Context / Activity, and the approval badge) read state files under a private `/tmp/claude-console/` directory that Claude Code writes via a status‑line handler (`sessions/`) and five hooks (`activity/`). Everything in it is owner‑only (0700 dirs / 0600 files).

Turning this on edits `~/.claude/settings.json`, so the plugin does it only when you ask — by pressing a live key (see the [README](../README.md#the-live-status-bridge)). On every load the plugin writes its two scripts to `~/.claude/claude-console/scripts/` (its own folder) and leaves `settings.json` alone until then.

## What the switch writes

When you turn it on, the plugin merges exactly this — appending only hooks that aren't already there, chaining an existing `statusLine` so yours still renders, and rewriting `settings.json.claude-console.bak` immediately before every change it makes (so the backup is always the state one change ago, never a stale snapshot):

```json
{
  "statusLine": {
    "type": "command",
    "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/statusline-handler.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/statusline-handler.sh\""
  },
  "hooks": {
    "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" busy" }] }],
    "PostToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" busy" }] }],
    "Notification":      [{ "hooks": [{ "type": "command", "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" waiting" }] }],
    "Stop":              [{ "hooks": [{ "type": "command", "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" done" }] }],
    "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "[ ! -f \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" ] || bash \"$HOME/.claude/claude-console/scripts/activity-hook.sh\" permission" }] }]
  }
}
```

(The plugin writes the absolute path rather than `$HOME`.) Each command checks that its script still exists before running it: an Options+ uninstall removes the plugin but cannot remove this wiring, and without the guard a user who then deleted `~/.claude/claude-console/` saw "Stop hook error occurred" on every turn (#55). With it, a missing script is a silent no-op.

The status‑line handler captures session state for the plugin and prints no visible status line. Claude Code reads hooks and `statusLine` at session start, so the keys come alive on your **next** session.

## Wiring it by hand

Add the block above to `~/.claude/settings.json` yourself (the scripts live at `~/.claude/claude-console/scripts/`, or use `scripts/` from a clone), merging the `hooks` into any existing block. On macOS a running session picks the change up on its next activity (verified 2026-09-02, Claude Code 2.1.258); on Windows restart Claude Code.

## Turning it off

Hold a live key → *Turn off* (or a second long press within 15 s), or:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh --unwire
```

Either removes *only* the plugin's entries (your own hooks and status bar stay; a chained status line is put back) and leaves a marker file, `~/.claude/claude-console/no-autowire`, which is how the live keys know to read **Off** rather than **Set up**. Pressing a live key turns it back on and clears the marker. Before 2.2.0 the plugin wired itself on first load and that file was the only opt-out; if you created it back then it is honoured.

## What each key needs

- **Activity** (working / waiting / done) and the **approval badge** come entirely from the hooks. Without them Activity reads **Ready** and never changes, and Yes/No have no approval to see.
- **Cost / Model / Context** need only the status line. Context turns **amber at 75%** / **red at 90%**.
