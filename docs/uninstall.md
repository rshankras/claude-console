# Uninstall / clean reinstall

Claude Console's footprint spans Logi's store, `~/.claude/claude-console/` (incl. the ~142 MB speech model), `/tmp/claude-console`, a Microphone permission, and — if you enabled live status — `~/.claude/settings.json`.

## 1. Remove the plugin + profile (Logi Options+)

In Logi Options+, **right‑click the Claude Console plugin → Uninstall** (or `logiplugintool uninstall ClaudeConsole`), and delete the imported **Claude Console — Keypad** profile. This is the actual uninstall.

## 2. Run the cleanup script (macOS)

The plugin installs this script *outside* its package precisely so it survives the uninstall — you don't need the repo:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh            # confirm, then remove
bash ~/.claude/claude-console/scripts/uninstall.sh --dry-run  # preview only
```

It removes the plugin's `statusLine` + hook entries from `~/.claude/settings.json` surgically (your own entries stay; a chained status line is put back) **before** it deletes the scripts they point at; then removes `~/.claude/claude-console/` (voice helper, whisper, the speech model, your `prompts.json`, and the auto-installed scripts — including itself), the `/tmp/claude-console` IPC files, the Microphone grant (`tccutil reset`), any crash‑disable marker, and a dev `.link` if present. It prints its targets and asks before deleting; **it never removes the plugin** (step 1 does).

If you only want the live-status wiring gone and the plugin kept: long-press a live key and choose *Turn off*, or run the script with `--unwire`.

**If you skipped step 2, the hooks unwire themselves** (#73). The plugin records where it is installed in `~/.claude/claude-console/plugin-home` on every load; a hook that finds that place missing records nothing, and once it has been missing for over a minute across two runs it runs the same surgical unwire as `--unwire` (rolling backup, your entries untouched, Off marker set) and writes `~/.claude/claude-console/unwired-after-uninstall` with the time. One miss is ignored on purpose — an Options+ update replaces the folder for a few seconds, and a reloaded plugin clears the note. Nothing else is removed: the runtime home and the IPC files still need the script.

Don't restore `settings.json.claude-console.bak` by hand to undo the plugin — it is a rolling backup of the state one change ago, useful if a write went wrong, not a pre-install snapshot.

**Installs older than 2.2.0** registered their own application entry in Options+, and an uninstall left it behind still claiming Terminal.app — Claude Console stayed listed as if the uninstall had failed, and opening Terminal switched the keypad to a layout of dead keys. The script sweeps such orphans (only entries whose plugin is gone, never another vendor's); then restart the service so Options+ forgets it: `killall LogiPluginService`. Since 2.2.0 the plugin is universal and creates no entry, so there is nothing to sweep.

## Windows

Starting with **2.3.1**, the Windows plugin implements the SDK's `Uninstall()` callback. During a normal packaged-plugin uninstall, it removes its five hook entries and its `statusLine` from `~/.claude/settings.json` before Options+ deletes the package. A status line it had chained is restored. Other hooks, settings, comments and formatting stay; the previous file is backed up to `settings.json.claude-console.bak`. No subsequent Claude turn or one-minute delay is required. Ordinary plugin unload and service restart do not remove wiring.

Options+ also calls uninstall during an **upgrade**. The plugin saves only its owned wiring in `~/.claude/claude-console/reinstall-wiring.json` before cleanup, then merges it back during install using the new helper path. A subsequent load retries if install could not access settings. New user settings and a newer user status line are preserved. A fresh install does not enable live status. **Reinstall remembers the previous setup too**; to leave live status off, turn it off before uninstalling, or retain/create the existing `no-autowire` marker before reinstalling. The receipt is removed after restoration or when the Off marker is present.

The runtime directory, downloaded model and user prompts remain for reuse. This change does not delete user data or install an extra executable. The macOS cleanup script remains macOS-only. To reset saved Windows integration state without deleting prompts/models, remove `reinstall-wiring.json` while the plugin is uninstalled. Delete other runtime data only if you no longer need it.

**If settings cannot be changed:** invalid JSON, an unreadable chain, a held lock, a failed backup or a denied write makes cleanup report failure. Read `~/.claude/claude-console/lifecycle.log` and the Logi Plugin Service log. The installed host continues deleting the package even if the callback fails; a failure is not a clean-uninstall success. Existing launcher guards keep missing helpers from producing hook errors. Resolve the reported file problem, reinstall, then turn live status off and retry uninstall. Do not restore the entire rolling backup over newer settings. If the runtime directory itself is unwritable, only the service log may be available.

Manual deletion of the package folder and removal of a developer `.link` can bypass the SDK callback. For those development/recovery paths, turn live status off before removing the files. Version 2.3.1 does not add a resident watcher to detect manual deletion. Packaged Options+ device acceptance is recorded separately in [the verification plan](windows-uninstall-2.3.1-plan.md).

## Clean reinstall

On macOS, do 1–2, then reinstall from the Logi Marketplace (or the package from [vizhi.dev](https://vizhi.dev/claude-console/#install)) and re‑import the profile. On Windows, use the cleanup and saved-preference guidance above before reinstalling.
