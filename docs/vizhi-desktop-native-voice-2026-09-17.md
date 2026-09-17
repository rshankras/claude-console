# Vizhi Desktop native Voice — 2026-09-17

Branch: `integrate/vizhi-desktop-main`. Current correction: **0.12.3 / Vizhi Adaptive 3**.

Status: **0.12.3 installed locally** with Adaptive 3 selected and **Control–Shift–V** configured.
The service log confirms the shortcut path is enabled. Live keypad start/stop awaits a user test.
AX voice recognition and direct draft insertion remain unresolved; the new shortcut bypasses
voice-button recognition, while 0.12.2's clipboard recovery remains available for failed drafts.

## 0.12.3 confirmed Voice Chat shortcut

The user supplied a settings screenshot at 15:44 showing **Toggle voice chat — Start or stop voice
chat**, assigned **⌃⇧V (Control–Shift–V)**. It also shows **Start dictation** assigned **⌃⇧D**.
This establishes toggle semantics and the actual configured chord; earlier screenshots established
only that the waveform control exists. Voice Draft retains its offline capture/recovery workflow.

Use the explicitly configured toggle as the primary Voice Chat path on this installation. Set
`~/.claude/claude-console/desktop-voice-shortcut.json` to:

```json
{ "toggleVoiceChat": "Control+Shift+V" }
```

The same home key now renders **Voice Chat / TOGGLE** independent of AX surface/voice recognition.
One press posts Control–Shift–V to the already-frontmost ChatGPT process. It neither activates an
app nor scans controls; both key-down and key-up retain the same target PID even if focus changes.
If ChatGPT is not frontmost, return **Open App** and send nothing. Successful posting reports
**Requested**; it cannot establish whether the app accepted the event or which voice state followed.
There is no second AX fallback after a shortcut attempt, which could otherwise double-toggle.

Preserve 1.2-second repeat suppression. Refuse any configured toggle while local capture is
starting, recording, cancelling, or transcribing. The existing observed-active guard for new
dictation remains, but an unobservable native session cannot be inferred from shortcut presses:
end native Voice in the app before using offline Voice Draft. No claimed microphone lock.

Configuration is opt-in and local; a release package does not assume this user's binding for every
installation. Missing/invalid configuration retains the original exact-button path described below.
The parser accepts ANSI letter-key positions plus Control/Shift/Option/Command; Control or Command
is required. Existing profile identities and bindings do not change. Reload to apply configuration.

Validation: **323 targeted C# tests passed**; production Swift modifier/dispatch functions passed
with synthetic PIDs and an inert event sink; previous AX targeting regressions passed. The full
helper compiles and signs. Tests cover unknown AX state, configuration validation, one toggle per
press, repeat suppression, local-capture exclusion, failure without fallback, and fixed-PID routing
across a simulated focus change. No live events were posted to ChatGPT; the Computer Use restriction
was respected. Live start/stop remains a user acceptance check after installation.

Installation: package built from `405756b`, verified, installed, and loaded by Logi Plugin Service
at **15:53:32** on 2026-09-17. The same service log confirms **Voice Chat uses configured shortcut**.
The installed DLL and runtime helper match the archive. All **28 application/profile files** are
unchanged; Adaptive 3 remains selected. The local shortcut file contains the screenshot-confirmed
Control–Shift–V binding. Backup/receipt: `~/.claude/claude-console/backups/vizhi-desktop/20260917-155324/`.

Manual acceptance: keep ChatGPT frontmost, press home bottom-right **Voice Chat / TOGGLE** once,
confirm native Voice opens, then press again after at least 1.2 seconds and confirm it ends. Check
that a default-profile copy of the key reports **Open App** with another app frontmost. No live
start/stop or microphone interaction is claimed by the package and dispatch tests.

## Historical 0.12.0 design

### Decision and revised plan

Add native Voice as a separate app-owned action. The current offline Dictate & Send and Voice
Draft actions remain available. The default profile places **Voice Chat** at home bottom-right;
Controls retains **Voice Draft → review → Send**, and all nine workflows remain. Everyday keeps
its explicit **Voice Draft / Send / Stop** row. Adaptive 3 has a new profile identity so importing
it cannot overwrite an Adaptive 2 customization or silently change the user's selected default.

The [official Voice documentation](https://learn.chatgpt.com/docs/features/voice), read on this date,
documents voice in Chat, Work, and Codex, the labels **Start voice chat**, **Start new voice chat**,
and **Stop voice chat**, and a configurable Voice chat hotkey. Existing-task support depends on
rollout. These are documented UI labels, not verified Accessibility selectors on this machine.

Implementation sequence:

1. Add exact native start/end candidates to the app adapter and observed voice state to snapshots.
2. Add a bounded helper operation that requests start or end explicitly in one pinned window.
3. Separate task Stop from Stop voice chat, including status and composer eligibility checks.
4. Add the live Voice Chat key and coordinate it with this plugin's offline dictation keys.
5. Generate Adaptive 3, preserve Everyday, update the documentation, and build a 0.12.0 package.
6. Validate state transitions, stale/ambiguous controls, profile preservation, and packaging.
   Live audio validation remains a separate manual check.

## Interaction

| Observation | Face | Press |
|---|---|---|
| One enabled documented start button | Voice Chat / TALK | Request start |
| One documented end button | End Voice / ACTIVE | Request end, only if enabled |
| Missing, duplicated, or unsupported controls | No Voice / CHECK APP | No action |
| App surface unavailable | Unavailable / CHECK APP | No action |

A successful button press briefly reports **Check App**, since first-use permissions or setup
may follow. Only a later app observation establishes ACTIVE. The key makes no claims about
listening, speaking, or mute state. App-side start/end changes refresh the key through the existing
monitor; active voice uses a one-second poll cadence. Rapid repeat presses are suppressed.

An End request never falls back to Start when the session has already ended. Duplicate buttons,
disabled buttons, partial trees, changed windows, and changed target controls refuse an action.
The helper never scans other windows to find a usable voice control. The native action does not
touch the composer, invoke local recording, change settings, or synthesize a global hotkey.

## Why this differs from the initial suggestion

Native Voice belongs in both app modes where observed, not just ChatGPT. A configurable hotkey
is useful for manual access, but provides no trustworthy state signal for an adaptive key.
The initial implementation therefore uses explicit observed controls. Separate mute/unmute and
long-press actions are deferred until their labels and behavior have been verified.

The existing generic Stop substring matcher could match **Stop voice chat**. Stop now uses a
separate exact-button verb, so an old helper cannot silently treat it as an ordinary loose press.
Voice session state does not make the task read as Working. Windows has the exact Stop correction;
native Voice remains unsupported there, and the distributable remains macOS-only.

The plugin serializes native and local voice requests. It refuses native start during local
recording/transcription and refuses a new dictation while the target window reports active native
voice. A running local capture can still stop. App-side changes and other windows cannot be made
atomic through Accessibility; this is not an exclusive microphone lock across applications.

## Validation

- Full C# suite: **1,451 passed, 13 Windows-only tests skipped**. One existing xUnit1031 warning
  remains in BridgeNoticeTests; no new test warnings were introduced.
- Production Swift matching functions executed against synthetic trees: passed. Includes native
  start/new-start/end, stale opposite requests, disabled and ambiguous controls, non-button text,
  exact Stop separation, misleading sidebar titles, and pinned-window scanning.
- Windows UIA helper builds with zero warnings/errors after the exact Stop correction.
- Both profiles regenerate byte-for-byte; Everyday is unchanged from 0.11.0.
- Complete helper compiled and signed. The 0.12.0 release package passed version, payload,
  resource, link, and signature checks; packaging leaves the installed runtime unchanged.
- Native start/end, permission prompts, actual app labels, and physical keypad interaction have
  not been validated live. No audio was recorded during automated verification.

## Manual acceptance

With the intended window open, check new-chat and existing-task voice availability in both modes.
Start Voice, complete setup if needed, verify End Voice appears, and end from the keypad. Repeat
with app-side end, a disabled control, and a switch between app windows. Verify task Stop interrupts
generation without ending Voice, and both dictation variants still work when Voice is inactive.

## Local installation record

- Package: `VizhiDesktop_0_12_0.lplug4`, built from implementation commit `f49765f`.
- Logi Plugin Service restarted and logged **VizhiDesktop 0.12.0** loaded successfully.
- Installed DLL, helper, and default profile match the verified package. The runtime helper was
  refreshed and its hash matches the packaged helper.
- **Vizhi Adaptive 3**, profile `390FE86F17D84EC6B4920C7A5C3F37FA`, is selected in the installed
  application's configuration. This explicit local adoption is separate from the package updater,
  which continues preserving other users' selected defaults.
- All **20 original profile files** remain byte-for-byte unchanged, including Adaptive 2 and Everyday.
- Backup and receipt: `~/.claude/claude-console/backups/vizhi-desktop/20260917-142110/`.
- Options+ Computer Use timed out. Installation and selection were checked through the service log
  and configuration; this is not a claim of visual verification or successful native voice capture.

## 0.12.1 detection correction

The user reported **No Voice / Check App** despite Voice being available and supplied a screenshot
of the Voice interface plus the exact tooltip **Start Voice Chat**. The 0.12.0 matcher was
case-sensitive and used the first nonempty accessibility text field. It therefore rejected the
reported capitalization and could miss a help/description label hidden behind an icon title or
value. This is a code-level compatibility defect; the screenshot alone does not reveal AX attributes.

The correction compares complete button labels after normalizing case and whitespace, and reads
title, description, help, and label together. AXValue is excluded from action identity. Generic
Close/× controls, substring matches, parent/sidebar buttons, duplicates, and disabled actions remain
ineligible. The layout and profile GUIDs are unchanged.

Synthetic regressions cover the user-supplied capitalization, each semantic label field, values
masking names, repeated labels on one button, unrelated Close, and the prior targeting guards.
Desktop/AX C# tests pass (217 tests), and the complete Swift helper compiles and signs successfully.
Live recognition and the active session's end label remain to be confirmed by the user; Computer Use
refuses access to the ChatGPT app, so no alternate live AX inspection was performed.

The 0.12.1 package was built from `c3fae66`, verified, installed, and loaded by Logi Plugin Service
at 15:12 on 2026-09-17. Runtime helper and DLL match the package. All 28 application/profile files
are unchanged, and Adaptive 3 remains selected. Backup and receipt:
`~/.claude/claude-console/backups/vizhi-desktop/20260917-151159/`.
The user subsequently confirmed **No Voice** remains after this update. Capitalization was a
code-level defect, but correcting it did not resolve recognition on this machine.

## 0.12.2 Voice Draft recovery

The user also reported no text appearing after dictation. Offline capture lifecycle records show
two presses per attempt. At 15:14 the pipeline reported **No speech**. At 15:17 it produced a
21-character transcript, then reported **Not typed**. No dictated words or app UI content were read
for this diagnosis. The second attempt therefore reached transcription and failed at delivery;
the precise composer rejection has not been established.

Changes:

- Show **PRESS TO STOP** during recording and **Transcribing / WAIT** during processing.
- Keep Voice Draft failure feedback visible until another attempt begins.
- On failed draft delivery, copy the original transcript to the macOS clipboard and show
  **Paste Draft / CMD+V**. Users check the composer and paste manually if needed. A write that
  was not confirmed may have partially landed; do not paste blindly into an existing draft.
- Clipboard recovery replaces previous clipboard contents, uses a private temporary UTF-8 file,
  bounds the copy process to two seconds, and cleans up that file afterward. It does not activate
  ChatGPT, synthesize keystrokes, or submit text. A failed copy continues to report **Not typed**.
- Successful delivery and Dictate & Send never invoke this fallback. Profile bindings stay stable.

Validation: **282 targeted C# tests passed**, including refused/throwing delivery, successful
delivery, auto-send exclusion, copy failure, Unicode preservation, private-file cleanup, and the
transcription phase's original-intent ownership. Clipboard transport was tested with a fake process
runner; no live pasteboard or ChatGPT interaction was performed. One pre-existing xUnit1031 warning
remains. This is a recovery path, not a verified fix for the app's Accessibility compatibility.

The 0.12.2 package was built from `91562f7` and passed the package/resource/signature checks.
Logi Plugin Service recorded version **0.12.2** loaded at **15:28:43** on 2026-09-17. The installed
DLL and runtime AX helper match the archive; all **28 application/profile files** are unchanged,
and Adaptive 3 remains selected. No recording helper was active at the service restart.
Backup and installation receipt: `~/.claude/claude-console/backups/vizhi-desktop/20260917-152835/`.
Live clipboard recovery and composer interaction still require a user attempt.
