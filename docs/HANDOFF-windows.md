# Handoff — after the Windows QA pass of 2026-08-30

Read this on the Mac (or any machine with `gh` configured). The evidence is in
`docs/windows-qa-2.2.0.md`; this file is only what is left to DO, in order, with the text to paste.

State: `main` at `18ffe16`. Four commits landed from the Windows laptop today, all with
`--no-verify` because 22 Mac-shaped tests fail there (item 5): `4439a36` #47 packaging, `069417d`
No/Esc fix + report, `4540120` report, `18ffe16` #33. Every 2.2.0 fix that applies to Windows was
exercised on the MX keypad; two defects were found and fixed on the way (#47, No/Esc), one was
found half-fixed and completed (#33).

## 1. Update the tracker (nothing was posted — the laptop has no gh)

`gh` was installed on the laptop but never authenticated; do this from the configured machine.
Paste the sections from the bottom of this file. Summary of what each issue needs:

| Issue | Action |
|---|---|
| #47 | comment + **close** (fixed `4439a36`, hardware-verified) |
| #33 | comment + **close** (fixed `18ffe16`, hardware-verified; `e66c1ee` alone did not cover it) |
| #21 | comment on the closed issue: Windows had its own No/Esc no-op, fixed `069417d` |
| #51 #48 #18 #49 #46 #27 #45 #23 #22 #25 #26 #28 #30 #31 #32 #50 #52 | Windows verification comment each |
| NEW | **Windows uninstall leaves settings.json hooks and self-registered app entries** (W1) |
| NEW | **LiveStatus\* suites fail on Windows while the feature passes on hardware** (W5) |

## 2. Copy the smoke-tested Windows whisper bundle to the Mac (#47)

`pack-release.sh` now refuses to pack without it. Source on the laptop:
`C:\Users\sahan\.claude\claude-console\whisper-bin-win\` (13 files + `TRANSCRIPTION_SMOKE_OK`).
Put it at `~/.claude/claude-console/whisper-bin-win` on the Mac, or point `WINDOWS_WHISPER_DIR` at
it. The marker records the evidence (official whisper.cpp v1.9.2, SHA-matched, transcribed on
Windows via the packaged copy path). Do NOT regenerate the marker on the Mac — it means "transcribed
on Windows".

## 3. Salvage the local `fix/qa-windows` branch on the laptop

`.worktrees/qa-windows` (branch `fix/qa-windows`, unpushed, based on the pre-icons main). Of its
three commits, two are now superseded on main (#47 → `4439a36`, Windows Terminal → `18ffe16`).
Still worth rebasing: `4a773d4 fix(windows): recognize Store-installed Codex CLI` — and
`tools/windows/prepare-whisper-bundle.ps1` from `5687e04` is a better bundle-prep script than
"copy by hand". Then delete the worktree.

## 4. Verify the one thing the log could not: the #33 card

The refusal path is log-verified (16 presses, each beeped). Whether the amber Options+ card
("The navigation keys need an open Windows Terminal window…") RENDERS was not confirmed by eye.
Recipe: hide the WT window (`docs/windows-qa-2.2.0.md` W8) or close all WT windows, press Next Tab,
look at Options+. One line in the report either way.

## 5. Make the LiveStatus suites platform-aware (W5)

`LiveStatusRoundTripTests`, `LiveStatusActionsTests`, `LiveStatusPromptTests` — 22 tests — assert
Mac-shaped facts (`scripts/` exists after load; `EnableLiveStatus` under the TempHome rig) that the
Windows gates deliberately skip. Until fixed, every laptop commit needs `--no-verify`. Either make
the assertions platform-aware or self-skip on Windows like the file-mode tests do.

## 6. A Windows uninstall remedy (W1)

`EnsureRecoveryScriptsInstalled` assumes "Windows uninstall deletes the application data outright".
It does not: after an Options+ uninstall the laptop had 5 dead hooks + statusLine in
`settings.json` and two orphaned `@_` registrations (`@_codexconsole` stamped `selfRegisteredBy`).
Wanted: a PowerShell/`.cmd` equivalent of `uninstall.sh --unwire` extracted to the runtime home, or
the plugin unwiring on its own unload. Until then the only clean exit is long-press Turn off BEFORE
uninstalling.

## 7. A test for the inject helper's input records (from `069417d`)

Nothing drives `claude-console-inject.exe`'s actual `INPUT_RECORD`s; `WindowsInjectionTests` stops
at argument construction, which is why Escape-with-no-character shipped. Factor the record builder
out of `Program.cs` so a test can assert `UnicodeChar` per key.

## 8. Housekeeping on the laptop

- Four rebuilt helper exes under `tools/windows/*/publish-win-x64/` are modified and uncommitted
  (build artifacts from today's publish; the Mac pack rebuilds them). Commit or discard.
- `ClaudeConsole_2.2.0.lplug4` in the repo root (gitignored) is the verified package: DLL with #33,
  fixed inject helper, `whisper-bin-win`.
- Machine state: 2.2.0 installed, live status ON, `@_defaultwin` + `windowsterminal` the only app
  entries, German layout removed, archived cleanup material in the session scratchpad only.

---

# Issue text to paste

## #47 — close

Fixed in `4439a36` and verified on the Windows laptop with the MX keypad, 2026-08-30.

The plugin's copy path (`EnsureVoiceRuntimeInstalledWindows`) was already complete; no package had ever carried `bin/voice/whisper-bin-win/`. `pack-release.sh` now requires `WINDOWS_WHISPER_DIR` (default `~/.claude/claude-console/whisper-bin-win`) containing `whisper-cli.exe` and a `TRANSCRIPTION_SMOKE_OK` marker, copies the bundle beside the macOS one, strips the marker, and fails the pack if the finished zip lacks the exe.

Hardware evidence: hand-placed runtime removed from the runtime home → 2.2.0 installed with the bundle → log `installing whisper bundle from package -> …\whisper-bin` → Voice key transcribed. Bundle = official whisper.cpp v1.9.2 `whisper-bin-x64.zip`, all 13 files SHA-256-matched. The smoke-tested bundle + marker live on the laptop; the Mac needs a copy before its next pack.

## #33 — close

`e66c1ee` fired only when `wt.exe` failed to run. With Windows Terminal installed and Claude Code in a classic console, `wt -w 0 …` exits 0 (acts on whatever WT window exists, or spawns one), so the nav press was still a silent no-op — the retest complaint exactly.

Fixed in `18ffe16`: the platform probes for a visible `CASCADIA_HOSTING_WINDOW_CLASS` window before running wt, treats a nonzero exit as failure, and reports the reason; the engine beeps on every refused press and posts the Options+ card once per load. New Claude (Window) is exempt.

Verified 2026-08-30: with the WT window hidden for 40 s, 16 nav presses each logged `no Windows Terminal window is running — Windows Terminal is required for this action` and beeped; with the window visible, tab switching works (no false refusals). Residual: with a WT window open, a nav press from a classic console acts on that WT window — documented in the README, not silent.

## #21 — comment on the closed issue

Windows had its own version of this bug. `claude-console-inject.exe key` wrote every named key with `UnicodeChar = '\0'`; Node's console reader identifies Escape by that character, so Escape was dropped unread while Return survived via its VK mapping. Result: Yes answered approvals, **No and Esc did nothing**, and the log claimed "rejected … by key" each time. Fixed in `069417d` (carry `\x1b` / `\r` / `\t`), verified on the keypad: one No press rejects, Esc interrupts. Test gap stated in the commit: nothing drives the helper's real input records.

## #51 — comment

Windows 2.2.0, 2026-08-30: idle sessions carry no amber dot; during a real `PermissionRequest` the Yes/No keys carry the dot and the Session key shows the *allow* face; clears after the answer. PASS.

## #48 / #18 — comment

Windows 2.2.0: a missing whisper runtime → WARN + beep + face within 200 ms (no 20 s dead key); two silent recordings → "No speech", nothing typed. PASS.

## #49 — comment

Windows 2.2.0: a session idle 28 minutes kept its own state file (own cost, own context %) and its slot. PASS.

## #46 / #27 — comment

Windows 2.2.0: no overrun or backoff lines in an afternoon of use; `LogiPluginService` ≈ 5.7 % of one core over 90 s with two idle sessions (upper bound — the measuring session was alive).

## #23 — comment

Windows 2.2.0: package loads clean (`version '2.2.0' loaded … 464 ms`), registers NO application, keypad shows nothing until the `.lp5` import — the universal shape holds on Windows. Importing `profiles/ClaudeConsole-Windows.lp5` created Options+'s own `windowsterminal` entry ("Terminal", `hasNativePlugin: false`, `additionalNativePluginNames: [DefaultWin, ClaudeConsole]`), confirming the by-analogy names. PASS.

## #22 — comment

Windows 2.2.0: German QWERTZ layout active in the session's tab, Explain prompt sent from the keypad — arrived character-exact (y/z, apostrophe, em dash, hyphen). The inject helper resolves every character through `VkKeyScanW` against the live layout. PASS.

## #25 — comment

Windows 2.2.0: pin / release / re-pin across two sessions; Cost and Context follow the pin (Windows has no frontmost signal by design, so display falls through to the routing session). Tab focus by identity selects the right Windows Terminal tab for Session 1 / Session 2. PASS.

## #26 — comment

Windows 2.2.0: Go to Project by voice — whisper caught only "console" of "claude console" (the ~1 s helper start-up clips the first word, known) and discovery still matched `ravi\claude-console` out of 3 candidates and opened a new session in slot 3. PASS.

## #28 — comment

Windows 2.2.0: Voice Draft started, Voice pressed mid-recording → `Send key stopped a Draft capture — routing to Draft`; text typed unsubmitted. Intent fixed at start, any voice key stops. PASS.

## #30 — comment

Windows 2.2.0: Esc during a turn interrupted the session and the busy face resolved within seconds (after the Escape-delivery fix in `069417d` — before it, Esc never reached Claude Code on Windows at all). A hand-written stale `busy` state was correctly refused by the transcript rule. PASS.

## #31 — comment

Windows 2.2.0, full round trip: install writes nothing (`Live status at load: NotEnabled`, 0 references); first press arms only; second press within 15 s writes exactly 5 hooks + statusLine with the user's 5 keys untouched, rolling backup = the pre-enable file, no temp left; long press arms, short presses in between still run `/cost`, second long press unwires (0 refs, `no-autowire` written, keys read Off); two short presses re-enable. Windows has `Prompt = null` so it is the two-step press, no dialog — as designed. PASS.

## #32 — comment

Windows 2.2.0: zero ERROR/WARN/Exception lines across the load (only the known benign "already loaded" pair from the service's double scan). PASS.

## #50 — comment (wontfix stands)

Three Session keys with two sessions behaved per the stable-slot rule on Windows (a released slot stayed empty, a new session took slot 3).

## #20 / #24 / #29 / #39 / #45 — comment: not applicable on Windows

#20 and #45's orphan sweep are moot since universal on both platforms — but the *assumption* that Windows uninstall deletes app data is wrong (new issue below). #24 (ggml dynamic backends) is macOS-only; the Windows payload is whisper.cpp's own prebuilt zip with the backend DLLs beside the exe, and it transcribed from a clean runtime home. #29 — Windows never used that path; #33 now covers the classic-console case. #39 — Mac tooling.

## #52 — comment

Windows 2.2.0 shows the same `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` pair at every service start with the plugin fully working — the same LPS-side double scan as on the Mac.

## NEW — Windows: an Options+ uninstall leaves the settings.json hooks and self-registered app entries behind

Found on the Windows laptop 2026-08-30 (`docs/windows-qa-2.2.0.md`, W1). After uninstalling Claude Console through Options+:

- `~/.claude/settings.json` still carried the 5 hooks + statusLine pointing at the deleted `claude-console-hook.exe`, so every Claude Code prompt ran a dead hook command.
- `Applications/Loupedeck70/@_codexconsole` (Vizhi's self-registered entry, `selfRegisteredBy` stamped) and the pre-universal `@_claudeconsole` survived their uninstalls and kept showing in Options+.
- `uninstall.sh` is not installed on Windows (gated off as a bash/macOS script), so there is no remedy on the machine. `EnsureRecoveryScriptsInstalled`'s comment ("Windows uninstall deletes the application data outright, so there is no orphan to sweep") is wrong.

Today the only clean exit is a long-press *Turn off* BEFORE uninstalling. Wanted: a Windows equivalent of `uninstall.sh --unwire` (PowerShell or `.cmd`, extracted to the runtime home like the Mac scripts), or the plugin unwiring on its own unload.

## NEW — the LiveStatus* suites fail on Windows while the feature passes on hardware

22 tests (`LiveStatusRoundTripTests`, `LiveStatusActionsTests`, `LiveStatusPromptTests`) fail on Windows on main. They assert Mac-shaped facts — `scripts/` exists after load (`EnsureBridgeInstalled` / `EnsureRecoveryScriptsInstalled` deliberately skip Windows), `EnableLiveStatus` outcomes under the TempHome rig — while the same flows pass on the keypad (`docs/windows-qa-2.2.0.md`). Until they are platform-aware (or self-skip on Windows like the file-mode tests), every commit from the laptop needs `--no-verify`, which is how `4439a36`, `069417d`, `4540120` and `18ffe16` were made.
