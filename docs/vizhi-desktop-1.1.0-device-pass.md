# Vizhi Desktop 1.1.0 — device pass for #138–#149 (branch `fix/vizhi-desktop-138-149` @ `2c411ed`)

Everything below is on the Mac with ChatGPT 26.917 and Vizhi Desktop 0.17.23 currently installed.
Run each phase in order; the state one phase leaves is the next phase's starting point.
Paste the evidence line for each step into the issue it names.

Shell variables used throughout (paste once per Terminal window):

```bash
L="$HOME/Library/Application Support/Logi/LogiPluginService"
D="$L/PluginData/VizhiDesktop"                 # the new runtime home (SDK plugin data directory)
S="$L/PluginData/VizhiDesktop Settings"        # settings backup + reinstall receipts
C="$HOME/.claude/claude-console"               # Claude Console's folder — must never change
log() { grep -h "Vizhi Desktop" "$(ls -t "$L"/Logs/*LogiPluginService.log | head -1)" | tail -${1:-30}; }
```

## Phase 0 — prepare (10 min, mostly waiting on Apple)

0.1 Version: both `src/Products/VizhiDesktop/VizhiDesktopPlugin.csproj` and
    `src/Products/VizhiDesktop/package/metadata/LoupedeckPackage.yaml` say `1.0.0`. Bump both to
    `1.1.0` (ask Claude to do it; `ProductVersionTests` pins that they agree), commit.

0.2 Pack from the worktree — this builds and notarizes BOTH helpers, so it needs the Developer ID
    in the keychain and the `claude-console-notary` profile:

```bash
cd ~/Work/MyApps/claude-console/.worktrees/desktop-issues
DOTNET_ROLL_FORWARD=LatestMajor bash tools/voice/pack-release.sh 1.1.0 VizhiDesktop
cp VizhiDesktop_1.1.0.lplug4 ~/Downloads/"Claude Console"/
```

0.3 Baselines — take these BEFORE installing, keep the files:

```bash
ls -la "$C" > ~/Desktop/claude-console-before.txt
ls -la "$L/Applications/Loupedeck70/@_vizhidesktop/Profiles" > ~/Desktop/profiles-before.txt
pgrep -fl "LogiPluginService|logioptionsplus_agent" > ~/Desktop/pids-before.txt
```

0.4 Check the package itself (#140): the helper must name Vizhi Desktop, not Claude Console.

```bash
cd $(mktemp -d) && unzip -q ~/Downloads/"Claude Console"/VizhiDesktop_1.1.0.lplug4 'bin/voice/*' && \
plutil -p bin/voice/ClaudeVoiceHelper.app/Contents/Info.plist | grep -E "BundleIdentifier|BundleName|Microphone|ShortVersion"
```
    Expected: `com.rshankar.vizhidesktop.voicehelper`, name `Vizhi Desktop`, prompt names Vizhi
    Desktop, version `1.1.0`. → evidence on #140.

## Phase 1 — install over 0.17.23 (#141, #140, #142 notice)

1.1 Logi Options+ → Vizhi Desktop → install `VizhiDesktop_1.1.0.lplug4` (it replaces 0.17.23).

1.2 Watch the load: `log 40`. Expected lines, in this order:
    - `Desktop settings` migration (copies desktop-*.json from `$C` into `$D`)
    - `VizhiDesktopPlugin: Loaded — driving the ChatGPT/Codex desktop app`
    - NO `restarting Logi Plugin Service` line, NO `DesktopWorkflowMigration: upgraded …` line.

1.3 Where the files went:
```bash
ls -la "$D"            # desktop-workflows.json, desktop-chatgpt-workflows.json, labels, shortcut, VizhiAxBridge, ipc/
diff <(ls -la "$C") ~/Desktop/claude-console-before.txt && echo "Claude Console folder untouched"
```
    Expected: the four desktop-*.json files (and their .before-* backups) are in `$D`; the diff
    prints "untouched". → evidence on #141.

1.4 Reload twice (Options+ → disable/enable the plugin, or `! killall LogiPluginService` — launchd
    restarts it) and run:
```bash
grep -c "DesktopWorkflowMigration: upgraded" "$(ls -t "$L"/Logs/*LogiPluginService.log | head -1)"
stat -f '%Sm %N' "$D"/desktop-workflows.json "$D"/desktop-chatgpt-workflows.json
```
    Expected: count `0`; the two mtimes do not change across the reloads. → evidence on #149.

## Phase 2 — the speech model (#142)

2.1 Optional but worth doing once: hide Claude Console's model so the download path runs.
    `mv "$C/whisper" "$C/whisper.off"` (restore it in Phase 9!).

2.2 Press **Dictate** once. Expected:
    - the microphone prompt names **Vizhi Desktop** (new helper identity → one-time prompt). Allow.
    - the key shows **Downloading · VOICE MODEL · 148 MB** and keeps showing it (not an 8 s flash);
    - Options+ shows a card: "Voice is downloading its speech model (142 MB, one time)…" — this is
      the `Notify` wiring that was missing;
    - when it finishes: a "model is downloaded" card; `ls -la "$D/whisper/"` shows `ggml-base.en.bin`.
    If you skipped 2.1: no download, `log` shows the model was copied from another product.
    → evidence on #142.

## Phase 3 — voice recovery (#139). Needs the **Dictate & Send** key on the keypad:
    Options+ → Vizhi Desktop → MX Creative Keypad → add the action from the **Agent** group.

3.1 A — send with the app gone. Quit ChatGPT. Press **Dictate & Send**, say a sentence, press again.
    Expected: key shows **Insert Draft · OPEN APP · TAP TO RETRY**; paste in TextEdit — the sentence
    is on the clipboard. Open ChatGPT, open a chat, tap the key → the words appear in the composer,
    NOT sent. Repeat once more and this time HOLD the key → face says **Discarded**.

3.2 B — draft with the app behind. ChatGPT running but another app frontmost. Press **Dictate**, speak,
    press again. Expected: **Insert Draft · FOCUS CHAT INPUT**; click into the composer, tap Dictate
    → inserted.

3.3 C — long dictations. (i) Dictate for ~90 s, stop: the whole text arrives (old cap was 60 s).
    (ii) Dictate for 3½ minutes: at 3:00 the key flips to **Recording ended**; press → the first
    180 s arrive. `log 20` must NOT contain `transcript not produced within`.

3.4 D — audio is gone after transcription: `ls -la "$D/ipc/voice/"` → no `capture.wav`.
    → evidence on #139 (all four).

## Phase 4 — the deleted profile (#138)

4.1 Options+ → Vizhi Desktop → MX Creative Keypad → delete the **Vizhi Home** profile.
4.2 `! killall LogiPluginService` and immediately watch for 45 s:
```bash
for i in 1 2 3 4 5 6 7 8 9; do pgrep -fl "LogiPluginService|logioptionsplus_agent"; sleep 5; echo ---; done
```
    Expected: the service PID changes ONCE (launchd restart) and then stays; the agent PID never
    changes; `ls "$L/Applications/Loupedeck70/@_vizhidesktop/Profiles"` still lacks the deleted
    profile; `log` has no `restarting` and no `re-extract` line. → evidence on #138.

## Phase 5 — the Mac matching fixes (#144–#147)

5.1 #144 Find Chat. Tools → **Find Chat**, then **Speak Query** (or Type Now), say a word that is in
    a chat title. Expected: one result card per matching chat with the WHOLE title; press one → that
    chat opens AND the keypad leaves the Find Chat page by itself (#151 — on the first 1.1.0 package
    it stayed on the cards; `log 20` must show `FindChat: result opened` then
    `FindChatDynamicFolder: closed after opening the chat`). Press Find Chat again → fresh cards
    (`FindChat: open in ChatGPT — search field ready`), and **Open Search** starts a new search.
    While the search box is open, also capture the tree for the record:
```bash
"$D/VizhiAxBridge" inspect --app com.openai.codex > ~/Desktop/find-chat-open.json
```
5.2 #147.1 Switch ChatGPT to **Codex** mode; repeat 5.1. Then press Find Chat AGAIN while the box
    is open → the cards come back and the field keeps focus (no bounce back to Tools). On the
    package built from `6f4048e` this bounced three times (2 Oct 11:53, "the app's mode is
    unreadable": the open box hides the mode switcher) — fixed in the next cut: `log` shows
    `FindChatDynamicFolder: mode unreadable, opening as <mode>` then `FindChat: open in <mode> —
    search field ready`.
5.3 #145 Project chat. In ChatGPT create a project (sidebar → Projects → +) and start a chat inside
    it. With that chat open: press **Copy Reply** → it copies (face not "Check Chat"). Press **Home**:
    the chat appears ONCE among the cards. Open another chat, press the project chat's card → it
    opens.
5.4 #146 View Changes. Codex mode, a project chat, ask for a file edit, wait; ask for a second edit,
    wait; close the Changes tab if open. Tools → Tasks → **View Changes** → the tab opens; press
    again → "already open" (stays).
5.5 #147.2 Voice Chat. With a chat open, the **Voice Chat** key must read *Voice Chat* (not "No
    Voice"); press → the app starts voice; press again → ends.
    → evidence on #144 #145 #146 #147.

## Phase 6 — the upgrade check (SDK contract; the one thing nobody has observed yet)

6.1 Leave a dictation pending: quit ChatGPT, press Dictate & Send, speak, press again → the key
    shows Insert Draft. Confirm `ls "$D/pending-draft.json"` exists.
6.2 In Options+, install `VizhiDesktop_1.1.0.lplug4` AGAIN (a replacement).
6.3 Expected afterwards — this is the whole check:
```bash
ls "$S"                      # settings backup + registration-reinstall/ (consumed) — the sibling SURVIVED the replacement
ls "$D"                      # recreated: desktop-*.json back, pending-draft.json back
log 40                       # "restored the pending dictation after a reinstall"; no "layout could not be restored"
ls "$L/Applications/Loupedeck70/@_vizhidesktop/Profiles"   # same list as before (the deleted one still absent)
```
    The model: `log` shows it was copied from Claude Console (or re-downloaded if you did 2.1).
    The Dictate & Send key still shows **Insert Draft**; tap it in a chat → the words are inserted.
    → evidence on #143 (upgrade half) and #138 (profiles kept).

## Phase 7 — uninstall (#143)

7.1 Options+ → Vizhi Desktop → uninstall. Then:
```bash
ls "$D" 2>&1                                  # expected: No such file or directory
ls /tmp/vizhi-desktop 2>&1                    # expected: No such file or directory
ls "$L/Applications/Loupedeck70/@_vizhidesktop" 2>&1   # expected: No such file or directory
ls "$S"                                       # expected: the small settings backup (documented), plus receipts
diff <(ls -la "$C") ~/Desktop/claude-console-before.txt && echo "Claude Console folder untouched"
pgrep -fl "VizhiAxBridge|ClaudeVoiceHelper|sleep 660" || echo "no plugin processes left"
```
    → evidence on #143 (paste all six outputs).

## Phase 8 — Windows (laptop), same package

8.1 Install; Phase 1.2–1.4 with `%LOCALAPPDATA%\Logi\LogiPluginService\PluginData\VizhiDesktop`.
8.2 Phase 3.1, 3.3(i), 3.4 (the Windows helper's separate WAV delete).
8.3 Phase 6 and Phase 7. The deferred timer no longer exists, so there is no process to watch for.

## Phase 9 — put the Mac back

- If you did 2.1: `mv "$C/whisper.off" "$C/whisper"`.
- Reinstall whichever Vizhi Desktop you want to keep running.
- If a voice press ever returns empty text with no prompt: `tccutil reset Microphone com.rshankar.vizhidesktop.voicehelper`, then press again.
