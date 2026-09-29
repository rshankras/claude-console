# Complete task prompts — Vizhi Desktop 0.17.5

The owner reported a 120-character prefix of the stock Review Changes instruction,
followed by the complete instruction. This is the preset text, rather than a speech
transcription. Whether it followed one tap or a retry has not been confirmed.

## Diagnosis

An immediate preset in an empty composer used `WritePreparedPrompt`, bypassing the
whole-draft insertion and recovery already used by Dictate and attachment prompts.
The native `write` fallback emits 20-character Unicode chunks with a four-second
deadline. It can leave a prefix when that deadline or its target/readback guard fails.
The failure was not retained by this command. A second tap prepared a new baseline,
treated the prefix as existing material, and appended the entire recipe.

The owner's prefix is six chunks long. The plugin's operational log contains
`MacDesktopAutomation.WriteComposer: composer-target-changed` failures. This supports
the mechanism, but does not prove which guard fired during the owner's attempt.

A disposable AppKit fixture with slow AX value reads reproduced the mechanism:
the legacy route left a 180-character prefix, returned `composer-target-changed`,
and the old recapture/retry sequence appended the whole prompt underneath it.
The exact cutoff varies with timing. The first fixture attempt also encountered
AppKit smart-quote substitution; the verified reproduction disables that fixture-only
substitution to isolate the deadline behavior.

## Change

- Empty-composer presets use the same whole-draft append/recovery flow as Dictate.
- The prompt, mode, conversation target, and original draft fingerprint survive an
  insertion failure. Retry never recaptures a partial prefix as source material.
- A fresh immediate preset sends only after complete insertion, with a new native
  `--expect-text` guard. An intervening text edit, changed target, or changed mode
  prevents automatic submission.
- Recovery always stops at **Send Draft**, requiring explicit submission. A failed
  send leaves the inserted draft ready; it does not insert the recipe again.
- Rapid repeat taps after a confirmed submission are suppressed for one second.
- Existing material, attachments, spoken briefs, and Dictate retain explicit Send.

The legacy native `write` path remains for other callers. The Mac immediate preset
commands no longer use it. No speech model, profile layout, or polling change is involved.

Follow-up: [0.17.6 unifies the remaining write/append and legacy reply-field paths](vizhi-desktop-whole-text-2026-09-22.md), removing character-chunk typing from the Mac desktop helper entirely.

## Verification

- C# suite: **2,042 passed, 13 skipped**; all script suites passed.
- Coverage includes all ten default immediate ChatGPT/Codex presets, partial and
  late-complete insertion, original-baseline retry, send failure, and repeat taps.
- AppKit: old-route reproduction, successful replacement under the same delayed
  reads, exact-text send, partial/edited draft refusal, mode change, and clipboard
  preservation passed.
- Chromium: ChatGPT and Codex preset insertion, exact-text submission, duplicate
  suppression, and edited-input refusal passed. The Codex dictation/append fixture
  checks that insertion stays unsent until an explicit Send.
- An initial Chromium attempt observed an unexpected single-character draft and
  refused with `append-unconfirmed`. Its evidence is retained; a fresh run passed.
  The cause of that unexpected input was not established, and the failed attempt
  is not counted as a pass.

Evidence: `artifacts/desktop-prompt-submit/`. GUI checks use only the disposable
`com.vizhi.desktop.testfixture` app and a named fixture clipboard. They do not read
or automate live chats. Physical keypad acceptance remains separate.

## Owner trial

Installed and loaded on `integrate/vizhi-desktop-main` at 16:35:44 local time on
2026-09-22; Logi reported a 227 ms plugin load. The packaged helper matches all
verified native fixture copies. Existing profiles, selected profile, and workflow
configuration were preserved. Receipt: `artifacts/desktop-prompt-submit/installation.json`.
Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260922-163536-prompt-submit`.

After installation, clear the already duplicated input once. In Codex with an empty
composer, tap **Tasks → Review Changes** once. Expect one complete instruction to be
inserted and sent. If insertion is interrupted, retry must not append a second copy;
check the input before explicitly sending a recovered draft.

For **Dictate**, start speaking and tap again to stop. The transcription should appear
in the composer and remain unsent until **Send**. The owner's physical Codex Dictate
acceptance is still pending from 0.17.4.
