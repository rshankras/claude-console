# Uninstall / clean reinstall

Claude Console's footprint spans Logi's store, `~/.claude/claude-console/` (incl. the ~142 MB speech model), `/tmp/claude-console`, a Microphone permission, and — if you enabled live status — `~/.claude/settings.json`.

## 1. Remove the plugin + profile (Logi Options+)

In Logi Options+, **right‑click the Claude Console plugin → Uninstall** (or `logiplugintool uninstall ClaudeConsole`), and delete the imported **Claude Console — Keypad** profile. This is the actual uninstall.

## 2. Run the cleanup script

The plugin installs this script *outside* its package precisely so it survives the uninstall — you don't need the repo:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh            # confirm, then remove
bash ~/.claude/claude-console/scripts/uninstall.sh --dry-run  # preview only
```

It removes the plugin's `statusLine` + hook entries from `~/.claude/settings.json` surgically (your own entries stay; a chained status line is put back) **before** it deletes the scripts they point at; then removes `~/.claude/claude-console/` (voice helper, whisper, the speech model, your `prompts.json`, and the auto-installed scripts — including itself), the `/tmp/claude-console` IPC files, the Microphone grant (`tccutil reset`), any crash‑disable marker, and a dev `.link` if present. It prints its targets and asks before deleting; **it never removes the plugin** (step 1 does).

If you only want the live-status wiring gone and the plugin kept: long-press a live key and choose *Turn off*, or run the script with `--unwire`.

Don't restore `settings.json.claude-console.bak` by hand to undo the plugin — it is a rolling backup of the state one change ago, useful if a write went wrong, not a pre-install snapshot.

**Installs older than 2.2.0** registered their own application entry in Options+, and an uninstall left it behind still claiming Terminal.app — Claude Console stayed listed as if the uninstall had failed, and opening Terminal switched the keypad to a layout of dead keys. The script sweeps such orphans (only entries whose plugin is gone, never another vendor's); then restart the service so Options+ forgets it: `killall LogiPluginService`. Since 2.2.0 the plugin is universal and creates no entry, so there is nothing to sweep.

## Windows

The cleanup script is a bash script and is **not installed on Windows** yet ([#55](https://github.com/rshankras/claude-console/issues/55)). An Options+ uninstall there leaves the five hooks and the `statusLine` in `~/.claude/settings.json` pointing at a deleted `claude-console-hook.exe`, so every Claude Code prompt runs a dead command. Until a Windows remedy ships, **turn live status off first** — hold a live key → *Turn off* (or a second long press within 15 s) — then uninstall in Options+.

## Clean reinstall

Do 1–2, then reinstall from [Releases](https://github.com/rshankras/claude-console/releases) and re‑import the profile.
