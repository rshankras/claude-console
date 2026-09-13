# The live status bridge — what is written, and wiring it by hand

The live keys (Cost / Context / Activity, and the approval badge) read state files under a private `/tmp/claude-console/` directory that Claude Code writes via a status‑line handler (`sessions/`) and five hooks (`activity/`). Everything in it is owner‑only (0700 dirs / 0600 files).

Turning this on edits `~/.claude/settings.json`, so the plugin adds wiring only when you ask — by pressing a live key (see the [README](../README.md#the-live-status-bridge)). On every macOS load the plugin writes its two scripts to `~/.claude/claude-console/scripts/` (its own folder). An update may migrate only commands already marked as Claude Console's to their current guarded form; it never adds wiring or touches another command.

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

(The plugin writes the absolute path rather than `$HOME`.) The rest of the file is handed back the way it was found — same indentation, line ending and trailing newline, quotes and non-ASCII text left literal (#72) — so the diff of a *Turn on* is the entries above and nothing else.

Each command checks that its handler still exists before running it: an Options+ uninstall removes the plugin but cannot remove this wiring, and without the guard a user who then deleted `~/.claude/claude-console/` saw "Stop hook error occurred" on every turn (#55). With it, a missing handler is a silent no-op. The scripts also check that the plugin itself is still installed (#73): the plugin records its location in `~/.claude/claude-console/plugin-home` on every load, a hook that finds that place gone records nothing, and once it has been gone for over a minute across two runs the hook runs the surgical unwire itself and sets the Off marker — see [uninstall.md](uninstall.md). Windows uses the same six entries, each running `claude-console-hook.exe` through an encoded PowerShell launcher — `powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand …`, whose script checks `Test-Path` on the exe before piping the payload to it — so Git Bash cannot rewrite its switches or paths. Commands written by an older install (`cmd.exe /d /c if exist …`) are migrated to this form on load. Windows has no liveness check yet.

The status‑line handler captures session state for the plugin and prints no visible status line. A running session picks the wiring up on its **next activity**, on macOS and Windows alike, so the keys come alive without a restart.

## Wiring it by hand

Add the block above to `~/.claude/settings.json` yourself (the scripts live at `~/.claude/claude-console/scripts/`, or use `scripts/` from a clone), merging the `hooks` into any existing block. A running session picks the change up on its next activity — on macOS (verified 2026-09-02, Claude Code 2.1.258) and on Windows (measured 2026-09-10 and 2026-09-11, #58).

## Turning it off

Hold a live key → *Turn off* (or a second long press within 15 s), or:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh --unwire
```

Either removes *only* the plugin's entries (your own hooks and status bar stay; a chained status line is put back) and leaves a marker file, `~/.claude/claude-console/no-autowire`, which is how the live keys know to read **Off** rather than **Set up**. Pressing a live key turns it back on and clears the marker. Before 2.2.0 the plugin wired itself on first load and that file was the only opt-out; if you created it back then it is honoured.

## What each key needs

- **Activity** (working / waiting / done) and the **approval badge** come entirely from the hooks. Without them Activity reads **Ready** and never changes, and Yes/No have no approval to see.
- **Cost / Context** need only the status line. Context turns **amber at 75%** / **red at 90%**.
- **Model** needs neither: it opens the `/model` picker and keeps the same brain icon for every model (#86).
