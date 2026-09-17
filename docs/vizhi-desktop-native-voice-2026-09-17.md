# Vizhi Desktop native Voice — 2026-09-17

Branch: `integrate/vizhi-desktop-main`. Target: **0.12.0 / Vizhi Adaptive 3**.

## Decision and revised plan

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
