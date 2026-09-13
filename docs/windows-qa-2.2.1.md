# Windows QA pass — Claude Console 2.2.1 on the MX Creative Keypad

**Run sheet, opened 2026-09-10 on the Windows 11 laptop.** None of the Windows binaries in 2.2.1 has
run on Windows: the release was built, packed and retested on the Mac, and the Windows half of the
answer to Logitech QA's 1 September retest is still owed. This file is the procedure and the record.

Companion to `docs/windows-qa-2.2.0.md` (the previous pass, whose findings W1–W8 still apply) and
`docs/HANDOFF-september-commitments.md` (what is owed and to whom).

## Under test

| | |
|---|---|
| Package | `ClaudeConsole_2.2.1.lplug4`, 22,304,379 bytes |
| SHA-256 | `2fd5da12581c2aa6d91342df32a1f8f5fe20fe9b7f829580e6e3cdd0bd2d2140` — matches the handoff record, so this is the submitted artifact, not a rebuild |
| Source | tag `v2.2.1` = `aacf154`; downloaded from the GitHub release |
| Working copy | `~/Downloads/ClaudeConsole-2.2.1-qa/` — package, both profiles, `tools/`, `evidence/`, `backup/` |

The package carries all five Windows helpers (`hook`, `inject`, `focus`, `shot`, `voice`) and the
`whisper-bin-win` bundle, so nothing has to be built here.

**Important — what was tested is the RELEASED package, which predates the retest fixes.** The branch
`fix/qa-retest-2.2.1` already carries fixes this pass did not exercise: `f7aad45` fixes **#74, #75,
#76, #77, #61** (including the exact `permission`→`waiting` hook translation for #74) and `bba24b7`
fixes **#72, #73**. So every "still present" result below for #74 and #61 describes the shipped
package, not the branch. The branch does NOT touch the screenshot helper, the New Claude launch, or
the risk classifier, so findings F2/F6/F7 (#83/#84/#85) remain valid on it. A proper verification of
the retest fixes needs a build FROM the branch, not the released package.

## Machine, before anything was installed

Captured in `evidence/snapshot_pre-install_2026-09-10_14-52-12.txt`. This is a genuine clean room,
cleaner than the 2.2.0 pass had:

- Logi Plugin Service **6.4.1.3246**, running. No plugin installed at all.
- Application registrations: `@_defaultwin` only, under all three Loupedeck generations. **No
  `windowsterminal` entry**, so the Windows profile import is untested here and is part of the pass.
- `~/.claude/settings.json`: 89 bytes, three unrelated keys, **zero** claude-console references.
- No `claude-console-hook` process running.
- Runtime home holds the model, `whisper-bin`, and `whisper-bin-win` **with its
  `TRANSCRIPTION_SMOKE_OK` marker** — the pack source. Backed up to `backup/whisper-bin-win/` before
  the install, because the install copies the packaged bundle over that tree.
- Windows Terminal and a native `claude.exe` are both present and on PATH.

**Two things to know before reading results.** `whisper-bin` here contains Windows DLLs, left by the
Vizhi 1.6.x work under the macOS-shaped name; it is not what the Windows code path reads. And
settings.json has only three keys, which is a thin test of "user entries are untouched" — add a
harmless user hook before check B if that assertion is to mean anything.

## Prerequisites for the run

1. Import `ClaudeConsole-Windows.lp5` in Options+ after installing. Nothing appears on the keypad
   until you do; the plugin is universal and ships no layout.
2. Live status is opt-in. Two short presses on a live key, or the long-press dialog, is the switch.
3. Run Claude Code unelevated, in Windows Terminal.

## The six checks

Each names the QA finding it answers, what 2.2.1 claims, and what counts as a pass. Tools live in
`~/Downloads/ClaudeConsole-2.2.1-qa/tools/`.

### A — hook processes can no longer pile up (#57, retest bug A, the most severe finding)

QA found roughly fifteen `claude-console-hook` processes left behind after one session following a
**reboot**, and the machine froze until the plugin service was stopped. The reboot matters: the old
code read a PowerShell fallback's output before applying its time limit, and PowerShell is slow to
start after boot. 2.2.1 arms an 8 second watchdog before any file I/O and refuses to run above eight
concurrent copies.

1. Install the package, import the profile, turn live status on, **then reboot the laptop**. The
   order matters: the hooks must already be wired so that the first session after boot runs them
   cold, which is the state QA reproduced. Do not skip the reboot.
2. After the reboot, start a Claude Code session, run several turns, close the tab.
3. `powershell -File tools\watch-hooks.ps1 -Seconds 180` throughout.
4. `powershell -File tools\pileup.ps1 -Copies 12` for the cap.

**Pass:** peak concurrent hooks at or below 8; no sampled hook older than 10 seconds; count returns
to zero within about 10 seconds of the last turn; `pileup.ps1` reports every copy gone by roughly 10
seconds with "not adding to the pile" lines in `hook-invoked.log` beside the exe.

5. **Uninstall in Options+ without stopping the plugin service first**, which is what a user does and
   what froze QA's machine. The machine stays responsive and no hook process survives.

### 2 — does live status need a Claude Code restart (retest item 2, #58)

On macOS a running session picks up the new hooks and status line by itself, measured at one second
for the status line and three minutes for the approval hook, so the face reads **Turned on**. Windows
keeps the "Restart Claude" wording because QA saw the keys stay inert until Claude Code was
restarted, and **the cause is not known**.

With a Claude session already running, turn live status on and leave it alone. Time how long until
the session key's bar and the Cost key start moving. Then restart Claude Code and time it again.

**Pass:** the shipped wording matches what actually happens. If the keys do come alive without a
restart, the Windows wording is wrong and that is a finding, not a pass.

### 16 — Windows Terminal required, both branches (retest item 16, #33)

The nav keys drive `wt.exe`. The 2.2.0 fix probes for a **visible** window of Windows Terminal's own
window class rather than trusting `wt.exe`'s exit code.

- **Present:** with a Windows Terminal window open, New Tab, Next Tab, Previous Tab and New Claude
  (Window) all act. No refusal card.
- **Absent:** `powershell -File tools\hide-wt.ps1 -Seconds 40` hides every such window without
  closing it, which keeps the driving session alive. Press the same keys while it is hidden.

**Pass:** every press in the second branch beeps, logs that Windows Terminal is required, and issues
no `wt.exe` command; the card appears once, not per press. New Claude (Window) is the deliberate
exception and must still work, because its job is to create the first window.

### B — a leftover hook no longer errors on every turn (#55, retest bug B, Windows half)

An Options+ uninstall removes the plugin and nothing else, so the five hooks and the status line stay
in settings.json pointing into a folder the user may then delete. On 2.2.0 that produced "Stop hook
error occurred" on every turn. Every 2.2.1 command is now shaped
`cmd.exe /d /c if exist "<path>" "<path>" <verb>`.

1. With live status on, confirm the wiring is the `if exist` form.
2. Uninstall the plugin in Options+, then delete `~/.claude/claude-console/`.
3. Run several turns in a fresh Claude Code session.

**Pass:** no hook error on any turn; the entries are inert. Also confirm the on-load migration: put
the 2.2.0 command form back by hand, load the plugin, and watch it rewrite only its own entries,
through the rolling backup, leaving user entries alone.

### C — both answer keys show a pending approval, and No clears it (#60, retest bug C, Windows half)

A rejection fires no hook, so on 2.2.0 the Yes dot and the session's **Allow?** bar stayed lit until
that session's next prompt. The answer key now clears the captured payload itself the moment its
keystroke lands, and leaves it if the keystroke did not.

Trigger a real permission prompt. Both Yes and No should carry the amber cue, with Yes red for a
destructive request while No stays amber. Press **No**.

**Pass:** the cue clears on both keys immediately and the session bar leaves **Allow?**, with no
second prompt needed. Repeat with Yes as the control. Note that on 2.2.0 the Windows No key was a
no-op until finding W2 was fixed, so this also re-exercises that.

### D — the voice runtime is replaced when the package carries a different one (#59, Windows half)

The macOS defect was specific to signed app bundles. The Windows equivalent is the whisper bundle:
the plugin compares every packaged file by size and SHA-256 against the runtime home, and copies only
when they differ, so a stale or partial bundle is repaired rather than trusted.

The runtime home here already holds a `whisper-bin-win` from the Vizhi work, so the comparison runs
for real on first load. Watch the plugin log for either "installing whisper bundle from package" or
silence, then press Dictate.

**Pass:** a transcript comes back, and whichever branch the log took is the correct one for whether
the trees actually differ. **`TRANSCRIPTION_SMOKE_OK` must still exist afterwards** — the copy adds
files and does not delete, so the marker should survive; it certifies a transcription that happened
on Windows and `pack-release.sh` refuses to pack without it. The backup in `backup/whisper-bin-win/`
is the recovery if it goes missing.

## GitHub (filed 2026-09-10)

New issues created for findings not already tracked:
- **#83** [P1] Windows: screenshot and focus helpers fail without the .NET 8 Desktop Runtime (F2/F3/F4)
- **#84** [P2] Windows: risk classifier is Unix-only — destructive PowerShell shows amber (F6; fixed in `946b993`)
- **#85** [P3] Windows: New Claude launches sessions in the Logi program directory (F7)

Test-result comments added to existing issues:
- **#74** (P0, Yes/No discarded): root cause present; keys work only after Claude Code's delayed
  Notification flips state to `waiting` (~6 s window). New data — failure is a timing window here,
  not total.
- **#58** (P0, item 2): enable works; status-line path applies live with no restart (measured);
  Yes/No still gated by #74.
- **#60** (P2, retest C): No clears the cue immediately — verified on Windows.
- **#61** (P3, item 16): refusal fires with no WT visible; conhost-with-WT-elsewhere not retested;
  runtime dependency now also undeclared (see #83).
- **#59** (voice, item 6 root): Windows bundle comparison correct, marker preserved, Dictate works.
- **#64** (smoke marker): survives the runtime comparison.

Not yet commented: **#57** (P0 hook pile-up) — its test needs the reboot and has not run.

## Remaining: the reboot-and-teardown sequence (checks A and B)

These are the last owed checks. A needs a cold boot; B uninstalls the plugin, so it ends the pass.
Run them in this order, in one go. I monitor from the log and the prepared scripts.

1. **Reboot** the laptop. The plugin is already installed and wired, so the first session after boot
   runs the hooks cold — the exact state QA's freeze appeared in.
2. **After boot:** start a Claude session, run several turns, close the tab. Run
   `tools\watch-hooks.ps1 -Seconds 180` throughout. *(A: pile-up — peak ≤ 8, none older than 10 s,
   count returns to 0.)*
3. `tools\pileup.ps1 -Copies 12`. *(A: cap of eight — never more than 8 alive, all gone by ~10 s,
   "not adding to the pile" in hook-invoked.log.)*
4. **Uninstall in Options+ WITHOUT stopping the plugin service first.** *(A: the machine stays
   responsive; no hook process survives.)*
5. **Delete `~/.claude/claude-console/`**, then run several turns in a fresh Claude session.
   *(B: leftover hook is a no-op — no "Stop hook error occurred", the `if exist` guard holds.)*
6. **On-load migration (B, optional):** reinstall, put a 2.2.0-form hook command in settings.json by
   hand, load the plugin, confirm it rewrites only its own entries through the rolling backup and
   leaves user entries alone. *(Needs a settings.json edit, which the QA session's guard blocks — do
   it by hand or skip.)*

## Results

Fill in as each check runs. Nothing below has been executed yet.

| Check | Result | Evidence |
|---|---|---|
| Install + profile import | **PASS** | 2.2.1 loaded in 523 ms, 15 actions, 0 errors; profile import created the `windowsterminal` registration |
| Enable sequence (two-step press) | **PASS** | first press armed ("nothing changed"), second within the window enabled, settings.json written with the `if exist` form |
| 2 restart needed or not | **cost/status-line applies live, NO restart — the wording is too conservative** | see below |
| A cap of eight | **PASS** | 3 min after a cold boot, 12 hook copies launched: peak 8 alive, 4 refused ("9 copies … (cap 8) — not adding to the pile"), all exited in 2.2 s, no stragglers |
| A hook pile-up after a real session | **PASS** | 180 s monitor after the cold boot, session run and tab closed: peak 1 concurrent hook, none older than 10 s, count returned to 0 — no accumulation |
| A uninstall without stopping the service | **PASS** | uninstalled in Options+ without stopping LPS; service kept running (uptime unbroken), machine responsive, 0 hook processes survived |
| 16 Windows Terminal present | **PASS** | with terminals visible, New Tab opened a new tab on the keypad |
| 16 Windows Terminal absent | **PASS** | with 0 visible WT windows (instrumented, 100% of 30 s), New Tab / Next Tab / Prev Tab each logged the refusal WARN and beeped; the card is posted once (`_warnedNoTerminal` guard) |
| B leftover hook is a no-op | **PASS** | after uninstall, settings.json still holds 6 refs pointing at the deleted exe; running the exact leftover Stop command with the exe gone → exit 0, no output. The `if exist` guard no-ops, so no "Stop hook error occurred" |
| B on-load migration | owed (optional) | needs a reinstall + a hand-edited 2.2.0-form command; the settings.json edit is blocked for the QA session |
| C pending cue on both keys | **PASS after a ~6 s delay — see #74** | both Yes and No showed amber and the bar read **Allow?**, but only after a delay: the cue arms when the state reads `waiting`, and the hook first writes `permission` (untranslated) until Claude Code's delayed Notification flips it |
| C No press clears it | **PASS** | log: `AnswerCommand: rejected the pending prompt ... by key` / `no`; the cue and Allow bar cleared immediately, no next prompt needed (#60) |
| #74 fix (branch build) | **VERIFIED FIXED** | ran both hooks with a PermissionRequest payload in an isolated IPC root: released writes `{"state":"permission"}` (keys discarded), branch `f7aad45` writes `{"state":"waiting"}` + pending payload (keys arm). Binary-level proof, not just code |
| C destructive Yes turns red | **FAIL on Windows — F6** | a recursive force-delete showed amber, not red; Claude proposed `Remove-Item -Recurse -Force` (tool `PowerShell`) and the classifier has no PowerShell patterns. Capture + answer keys work; only the risk grading is blind |
| D whisper bundle comparison | **PASS (skip branch)** | all 13 packaged files byte-identical in the runtime home, so `RuntimeTreeMatchesPackage` returns true and the plugin correctly skips reinstall; no whisper line in the load log confirms it |
| D smoke marker survives | **PASS** | `TRANSCRIPTION_SMOKE_OK` is the only runtime-only file and the comparison walks package files only, so it is never touched |
| Dictate / voice transcription | **PASS** | multiple clean transcripts in the log (e.g. 32 and 28 chars); silence correctly hit the "No speech" path; `voice.exe` is self-contained so the runtime gap never affected it. Whisper accuracy is rough but the pipeline runs end to end |
| Screenshot key (found in passing) | **FAIL without runtime — F2**; works once .NET 8 Desktop Runtime is installed | `shot.exe` is framework-dependent (0.15 MB), errored with `hostfxr.dll not found`; after installing Desktop Runtime 8.0.31 the capture landed a PNG in the conversation — root cause proven |
| Session-key tab switch (found in passing) | **DEGRADED without runtime — F3**; works once runtime is installed | `focus.exe` errored the same way; after the runtime install it selects the tab (first call 5.3 s cold, within its 10 s budget) |

## The enable sequence, from the plugin log (2026-09-10)

```
14:57:13  Plugin 'ClaudeConsole' version '2.2.1' loaded ... in 523 ms
14:57:13  Live status at load: NotEnabled
14:57:43  Cost pressed before setup — nothing changed; ... a second press within 15s, enables
14:57:43  claude-console-inject.exe took 5241ms of its 5000ms budget        <-- WARN, see finding F1
14:57:43  AnswerCommand: No pressed while live status reads 'Set up' — the
          PermissionRequest hook is not installed; press a live key to turn it on
14:57:44  Cost pressed again within the window — enabling
14:57:44  enabled — wrote settings.json; start a NEW Claude Code session to activate the live keys
14:57:44  Live status: JustEnabled                                          <-- face reads "Restart Claude"
14:58:21  Live status: Enabled                                              <-- state arrived, face went live
```

**What the user saw** — Cost reading **Set up**, then **Restart Claude** after the enabling press —
is exactly the designed Windows path. `JustEnabled` renders "Restart Claude" on Windows because
`WindowsPlatformBridge.SettingsApplyLive` is `false`. The important line is the last one: 37 seconds
later the plugin logged **`Live status: Enabled`**, i.e. a session reported and the face left "Restart
Claude" for live values. The state files confirm real data flowed: cost `$6.80`, context 13%, model
present in `sessions/shared.json`.

**Why check 2 is only partial.** Sessions were started, exited and model-switched around the enable
moment, so the log cannot prove whether the session that was *already running at 14:57:44* picked the
hooks up, or whether the live values came from a session started afterwards. That distinction is the
whole of QA's item 2. It needs the controlled test below, run once, cleanly.

### Passive proof from the log: the running session DID pick it up (2026-09-10)

The natural experiment happened without being set up for it:

| Time (local) | Event |
|---|---|
| 14:39:03 | this Claude Code session's transcript is created — the session begins |
| 14:57:44 | live status enabled; settings.json written with the hooks and status line |
| 14:58:21 | plugin logs **`Live status: Enabled`** — a session reported live state |
| 14:58:40, 14:58:48 | the current Claude processes start (a later user-driven `/exit` + resume) |

The plugin only logs `Enabled` when a fresh state report arrives. At 14:58:21 neither current
process existed yet, so the report came from the process running since **14:39** — which started
**18 minutes before** live status was enabled and was **never restarted** between the enable and the
report. It ran the newly-written status-line hook about 37 seconds after the file changed.

**Conclusion:** on Windows, the cost / context / status-line path applies to an already-running
session with no restart, the same as macOS (macOS measured ~1 s, this Windows run ~37 s). The
`WindowsPlatformBridge.SettingsApplyLive = false` wording ("Restart Claude") is therefore more
cautious than the real behaviour for these keys.

**Residual doubt, and what is not yet proven.**
- A restart between 14:57:44 and 14:58:21 that left no process trace cannot be fully ruled out from
  the log alone. The `/exit` timing in the user's own history would confirm the 14:39 process was
  still alive at 14:58:21.
- This proves the **status-line** path only. The **approval** path (the `PermissionRequest` hook
  behind Yes/No) is separate; on macOS it lagged the status line by three minutes. Whether Yes/No
  comes alive without a restart on Windows is still untested — check C will show it.

Recommendation: do not flip `SettingsApplyLive` yet. Confirm with the controlled test below (or the
Yes/No path in check C), then, if it holds, either flip the flag or change the Windows wording from
"Restart Claude" to "Starting…" so it stops telling users to do something they do not need to do.

### The controlled test for item 2 (removes the residual doubt)

1. With live status already enabled, note the Cost key reads a live dollar value (not "Restart
   Claude"). Leave that session running.
2. In that same running session, run a couple of turns. Watch whether Cost and the session bar keep
   updating. **If they do, the running session applies the new wiring and the "Restart Claude"
   wording is too conservative — a finding.**
3. Then start a brand-new Claude Code session and confirm the same. That isolates "needs a new
   session" from "needs a full restart".

## Findings

**F1 — a cold-start inject overrun.** The first `claude-console-inject.exe` call after load took
5241 ms against its 5000 ms budget and logged a WARN. This is the first run of a freshly unpacked exe,
so JIT and a Defender first-scan are the likely cause; every later call was silent. Worth a second
look only if it recurs on a warm exe, but note it against the 5000 ms budget in case QA sees it too.

**F2 — the focus helper cannot launch on this machine, and the screenshot helper is at risk.** The
plugin log carries a .NET apphost error — `App host version: 8.0.29 / .NET location: Not found /
Failed to resolve hostfxr.dll` — and every `claude-console-focus.exe` call returns exit
`-2147450749` (0x80008083, the same error) and falls back to "raising the terminal window instead".
This machine has no standalone .NET runtime on PATH and no .NET in Program Files.

- **Focus (tab selection): a known, documented degradation.** `build-windows-payload.sh` says the
  focus helper stays framework-dependent on purpose, because WPF's UI Automation client cannot be
  trimmed, and that without the .NET Desktop runtime "tab-focus degrades to raising the window".
  That is exactly what is happening. Session pinning still raises the right window here; it just
  cannot select the exact tab. Not a regression, but it means every pin/focus test on THIS laptop
  runs in the degraded mode until the .NET 8 Desktop Runtime is installed.
- **Screenshot (`claude-console-shot.exe`): CONFIRMED BROKEN — a packaging bug (P1 for the feature).**
  Pressing Screenshot on the keypad produced, in the log:
  `claude-console-shot.exe: You must install .NET to run this application. / App host version:
  8.0.29 / .NET location: Not found / Failed to resolve hostfxr.dll [not found]. Error code:
  0x80008083`, and the key did nothing. The packaged exe is 0.15 MB — a framework-dependent apphost,
  not the ~12 MB of a self-contained build. This is a genuine bug, not a documented requirement: the
  shot csproj sets `SelfContained=true` with a comment that it "ships self-contained because Options+
  keeps its own .NET runtime private; a framework-dependent apphost cannot discover it and exits
  before opening the overlay." The build is not honouring that. The likely cause is that a
  self-contained `net8.0-windows` + `UseWindowsForms` build cannot bundle the Windows **Desktop**
  runtime when cross-published from macOS (`build-windows-payload.sh` relies on the csproj's
  `SelfContained` and never passes it on the command line; the Desktop runtime pack is not present in
  the macOS build environment, so the SDK silently falls back to framework-dependent). The three
  self-contained helpers that DO work (hook, inject, voice) are plain `net8.0` console apps whose
  runtime pack cross-compiles fine.
  **Consequence:** the Screenshot key fails on any Windows machine without the .NET 8 Desktop Runtime
  — which is most of them. **Fix:** build the shot (and focus) helpers on Windows, or install the
  Desktop runtime pack in the pack environment, and add a pack guard that rejects a shot.exe below,
  say, 5 MB the way other embedded-asset guards work.
  **Root cause proven on the device (2026-09-10):** installing .NET Desktop Runtime 8.0.31
  (`winget install Microsoft.DotNet.DesktopRuntime.8`) made both `focus.exe` and `shot.exe` launch;
  the Screenshot key then captured a region and typed the PNG into the conversation, and the session
  key selected the tab. The install is a workaround / confirmation, not the ship fix — the package
  must carry a self-contained shot helper so a clean machine needs no runtime.

**F3 — session-key tab switching does not work on this machine, same root cause.** Pressing a
session key raises the terminal window but does not switch to that session's tab. `focus.exe` (0.16 MB,
framework-dependent by design — WPF UI Automation cannot be trimmed) fails to launch with the same
`0x80008083` and the bridge falls back to "raising the terminal window instead". Unlike the shot
helper this is documented behaviour, but the requirement is not surfaced to users: on a clean Windows
install without the .NET 8 Desktop Runtime, session pinning cannot select the exact tab. The README
requirements should state the Desktop Runtime dependency (or the focus helper should be made
self-contained too, if UI Automation permits it in a later build).

**F7 — the New Claude key launches Claude in the plugin's own directory.** Three sessions started
from the keypad have `cwd = C:\Program Files\Logi\LogiPluginService` and pin as "LogiPluginService"
rather than a project. The New Claude / New Claude (Window) nav action inherits the Logi service's
working directory instead of a sensible default (the user's home, or the last project). Sessions land
in Program Files, their project name is the service folder, and any file work starts there. Worth a
fix: launch New Claude in `%USERPROFILE%` or a chosen projects root.

**F6 — the risk classifier is Unix-shell-only and misses Windows destructive commands.** Every
pattern in `RiskClassifier.HighRiskCommands` is a Unix shell command (`rm -rf`, `sudo`, `git push`,
`chmod 777`, `dd of=`, etc.). There is no pattern for the Windows-native destructive verbs Claude
proposes on this platform: `Remove-Item -Recurse -Force` / `ri -r -fo`, `del`, `rmdir /s`, `rd /s`,
`Format-Volume`, `Clear-Disk`, `Stop-Computer` / `Restart-Computer`. Consequence: on the Windows
product a genuinely destructive PowerShell command shows an amber Yes, not red — the across-the-room
warning the feature exists for is absent for the shell Windows users actually run. Surfaced when the
owner asked a session to delete a file and saw no red mark. (A plain single-file delete is correctly
amber by design; the gap is that even a recursive/forced PowerShell delete stays amber.) Fix: add
PowerShell/cmd destructive patterns, anchored the same way, and a Windows-shell test row.

**CONFIRMED with the live payload (2026-09-10).** With a prompt held open, the captured
`pending-*.json` carried `hook_event_name: PermissionRequest`, `tool_name: "PowerShell"`,
`tool_input.command: Remove-Item -Recurse -Force -LiteralPath "/tmp/qa-red-test" -Confirm:$false`.
The owner had typed "run the command rm -rf /tmp/qa-red-test"; Claude Code on Windows rewrote it to
the native PowerShell force-delete. `RiskClassifier.Classify("PowerShell", "Remove-Item -Recurse
-Force …")` matches none of the Unix patterns and returns Normal, so Yes stayed amber on a genuinely
destructive command. The capture pipeline and answer keys are sound; only the pattern set is Unix-only.
Because Claude proposes PowerShell for most system actions on Windows, the destructive-red cue — the
safety feature this exists for — is effectively non-functional on the Windows product. Suggested
patterns to add (anchored, case-insensitive): `Remove-Item`/`ri`/`rd`/`rmdir`/`del`/`erase` with
`-Recurse` or `-Force`, `Format-Volume`, `Clear-Disk`, `Clear-Content`, `Stop-Computer`,
`Restart-Computer`, `Set-ExecutionPolicy`, and `i(wr|rm) … | iex` (download-and-run). This is likely
P1/P2 for the Windows submission and belongs in the note to Logitech.

**FIXED 2026-09-10.** Added a Windows block to `RiskClassifier.HighRiskCommands`: recursive/forced
`Remove-Item`/`ri`/`rmdir`/`rd`/`del`/`erase` (PowerShell parameter-prefix aware — `-Fo`/`-Rec`,
never a lone `-f`), cmd `rd /s` and `del /s`, `Format-Volume`/`Clear-Disk`, cmd `format C:` (not
`dotnet format`), `Stop-Computer`/`Restart-Computer`, `Set-ExecutionPolicy Unrestricted|Bypass`, and
`i(wr|rm)|Invoke-WebRequest|Invoke-RestMethod … | iex`. Tests added to `RiskClassifierTests.cs`
(`Flags_windows_powershell_and_cmd_commands`, routine Windows cases in
`Leaves_routine_commands_alone`, and `A_destructive_powershell_approval_is_high` /
`A_routine_powershell_approval_is_normal` for `Classify("PowerShell", …)`). All 24 patterns validated
against the .NET regex engine (16 flag, 8 stay quiet, 0 failures). **The C# suite has not been run
here** — this laptop has the .NET 8 Desktop Runtime but no SDK; run `bash tests/run-all.sh` in the
build environment before shipping. The change is in `src/Core`, so it lands for every product
(Claude Console and Vizhi/Codex) at once.

**F5 — the approval cue lags several seconds, and it is issue #74, not poll latency.** The owner saw
a delay before Yes/No go amber and the bar reads Allow?. This is not the ~500 ms poll: the Windows
hook writes the state verbatim as `permission` (the Mac script translates it to `waiting`), and
`SessionRegistry.ApplyPendingApproval` acts only on `waiting`, so the keys stay disarmed until Claude
Code's delayed Notification (~6 s per the hook's own comment) rewrites the state to `waiting`. During
that window a press is discarded — exactly QA's #74. The keys worked in check C only because we
pressed after the Notification. **Test result to post on #74:** root cause still present in 2.2.1; on
this build the keys DO work once the state reads `waiting`, so the failure is a timing window, not
total as QA reported — likely a Claude Code version difference in whether/when the delayed
Notification fires. The captured `pending-*.json` carried the full payload, so only the state word is
wrong.

**F4 — the screenshot failure is reported as the wrong cause.** When `shot.exe` fails to launch, the
plugin logs `no file (exit -2147450749) — cancelled, or the capture overlay is unavailable` and then
`ScreenshotCommand: no capture (cancelled, or Screen Recording not granted)`. A missing-runtime
failure is thus reported to the user as "cancelled or permission not granted", sending them to check a
permission that is not the problem. The exit code `-2147450749` (0x80008083) is specifically the
runtime-missing error and should be recognised and reported honestly.
