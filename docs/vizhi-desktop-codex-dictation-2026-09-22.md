# Codex Dictate: transcript does not reach the input

Branch: `integrate/vizhi-desktop-main`. Patch: 0.17.4.

## Report and evidence

The owner started Dictate in Codex and stopped it expecting the transcript in the chat input.
The installed 0.17.3 plugin logged `composer-selection-changed` at 12:32:54 in the service log and again on
two retries at 12:33:07 and 12:33:25. That error is raised before text insertion, after the
transcript reaches the shared delivery callback. This identifies insertion as the failed
stage; it does not evaluate transcription accuracy.

The adapter supplied only `Work with ChatGPT` as a known empty-composer hint. Static code
from the installed app 26.915.31945 (build 9922), `app-primary-426732871368.js`, shows distinct
shared-composer hints: `Work with ChatGPT`, `Ask ChatGPT`, `Do anything`,
`Describe your task to generate a plan...`, and
`Describe your goal, define measurable outcomes for best results`.

A controlled Codex fixture with `Do anything` and the old one-label configuration reproduced
the exact cursor error. The shared native implementation already handles the rich-text
placeholder; its adapter vocabulary was incomplete. Earlier claims that all modes shared
the correction did not account for this missing label coverage.

## Change

Add the five verified static hints to the app adapter. No substring matching, inferred
project/agent names, busy-state hints or localized guesses. Keep the native helper and all
its focus, mode, conversation, unchanged-input, empty-selection and readback guards unchanged.
The native fixtures now consume the actual adapter labels instead of hardcoding one ChatGPT
hint. Both-mode start/stop tests verify transcript delivery through AppendPreparedDraft,
no send, and another recording on the next Dictate tap.

Expected workflow: Dictate → speak → Dictate to stop → transcript automatically appears in
the original Codex input → review → Send. Existing words and attachments remain intact.

## Verification

Targeted C# tests: 26 passed. Full suite: 2,027 passed, 13 skipped; script suites passed.
The old configuration reproduces the refusal. The AppKit matrix passed 10 steps, including
all five hints with empty/literal text, an attached image, retries, changed text, mode changes
and explicit Send. Four Chromium runs passed: Codex insertion/send, Codex attachment-only,
Codex literal hint plus attachment, and the existing ChatGPT screenshot flow. The native
helper is unchanged and matches the previously verified 0.17.3 binary.
Version 0.17.4 is installed and loaded (228 ms). The packaged native helper is byte-identical
to every fixture copy. The selected profile, profile contents, system profile and desktop
workflow settings were preserved. The plugin service restarted; the owner should start a
fresh Dictate recording to confirm live Codex insertion. No live Codex input was automated.

[Installation receipt](../artifacts/desktop-codex-dictation/installation.json).
Rollback backup: `/Users/ravishankar/.claude/claude-console/backups/vizhi-desktop/20260922-125949-codex-dictation`. Evidence is under
`artifacts/desktop-codex-dictation/`. All GUI automation targets the disposable fixture app
and its named pasteboard; real Codex microphone/keypad acceptance remains owner-run.

One initial test expected a raw generated placeholder to differ from literal user text with
the exact same AX value while an image kept Send enabled. Those fingerprints are identical;
the helper preserved and appended to the literal text. The changed-input regression now uses
distinct actual text, while literal-hint preservation is checked separately for every hint.
