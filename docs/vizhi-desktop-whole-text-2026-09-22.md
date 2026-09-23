# Whole-text insertion across Mac keypad commands

## Plan and scope

The owner requested removal of the old 20-character typing fallback across commands,
following the truncated/duplicated Review Changes report. Work remains on
`integrate/vizhi-desktop-main`; target release is 0.17.6.

1. Inventory every text-entry route, including older optional assignments and recovery.
2. Consolidate native composer `write` and `append` onto one whole-text insertion path.
3. Remove the Unicode chunk fallback from legacy reply-field paste as well.
4. Preserve existing-input, attachment, destination, mode, focus and Send safeguards.
5. Exercise long prompts, multilingual text, retries, partial failure and clipboard races
   in controlled AppKit and Chromium apps; run the full unit/script suite.
6. Package, verify and install without replacing the owner's keypad profiles or configuration.

## Command coverage

| Entry point | Text operation |
|---|---|
| ChatGPT Prompts, Codex Tasks, Saved Prompts and custom workflow assignments | Whole-text append; immediate presets send only after exact verification. |
| Dictate and spoken workflow briefs | Whole-text append; remain unsent for review. |
| Optional Dictate & Send | Legacy `write` now enters the same insertion implementation; verifies before Send. |
| Insert Draft and legacy recovery | Same whole-text implementation; an already-inserted exact copy is acknowledged, a different draft is preserved. |
| Paste into Chat and instructions after screenshots/files | Existing append implementation now uses the shared insertion function. Attachments stay in place. |
| Legacy reply-field paste | Uses the shared whole-text setter/paste function with its own source-window and editor checks. |
| Speak Query / search query insertion | Already sets and verifies the entire query in the search field; never uses chunked typing or the chat composer. |
| Send, Voice Chat, navigation, screenshot and file capture | Do not type prompt text; retain their existing actions. |

This change concerns the Mac desktop AX helper. It does not alter Windows input or terminal
product injection, speech models, button assignments, polling or native ChatGPT Voice.

## Implementation

The unified composer branch keeps `write`'s empty-draft requirement and `append`'s pinned
original-draft fingerprint. Both establish an empty selection at the insertion boundary,
then call `insertWholeText`:

- Attempt one insertion-only accessibility write of the entire text.
- If readback confirms the complete result, finish.
- If the setter had no effect and the draft and caret are unchanged, make one native paste.
  Snapshot and restore all clipboard formats, except when a newer external copy must be kept.
- A partial insertion, user edit, focus change or changed destination stops the operation;
  there is no second full-text paste after a partial write.

The helper contains no `keyboardSetUnicodeString` calls or chunk-splitting function.
The four-second character-typing deadline is gone. Bounded helper execution and readback
waits remain so an unresponsive application cannot hang the plugin indefinitely.

Legacy direct writes now receive the adapter's mode prefix and conversation marker too,
allowing the helper to check those during the operation. No input contents are added to logs.

## Verification record

Evidence lives under `artifacts/desktop-whole-text/`. Controlled GUI tests use only
`com.vizhi.desktop.testfixture` and a named test clipboard; no live chat inspection or
submission is part of these checks. Physical keypad acceptance is recorded separately.

- Full C# suite: **2,051 passed, 13 skipped**. All script suites passed.
- AppKit: 37 broader command checks, 6 preset checks under slow AX reads, and 14
  text-entry checks passed. These cover both modes, long multiline Tamil/emoji drafts,
  one-paste fallback, direct insertion, partial failure without duplicate/send, exact
  recovery, source-field paste, clipboard text/HTML restoration and a newer external copy.
- Chromium: ChatGPT and Codex legacy writes, Codex presets, screenshot instructions and
  Codex dictation/append passed. Legacy writes include a roughly 3,600-character Unicode
  prompt (3,732 characters), explicit Send, direct write-and-send and populated-draft protection.
- The native helper contains no Unicode character-event insertion calls. A script
  regression prevents reintroducing that fallback.

The first AppKit text test sampled fixture clipboard state before its next publication;
the test now waits for both insertion and clipboard restoration. One broader native run
refused after inserting into its second window and before Send; the cause was not established.
A fixture-only diagnostic rerun and the production-helper rerun both passed that step.
Failed evidence is retained and excluded from the passing verification counts. The
diagnostic helper targets only the fixture and is not packaged or installed.

## Installation and owner check

Version **0.17.6** was installed and loaded at 16:57:34 local time on 2026-09-22;
Logi reported a 229 ms load. The installed helper matches all eight passing native
suite copies. Existing profiles, selected profile and workflow configuration were preserved.
Receipt: `artifacts/desktop-whole-text/installation.json`.
Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260922-165728-whole-text`.

No profile reimport or reassignment is needed. On the physical keypad, try a longer
Dictate draft, then a task/prompt in an empty composer. Dictate should leave the full
text ready for review; an immediate task should submit one complete prompt. Existing
text or attachments should remain intact when adding instructions. Hardware acceptance
is pending; the automated evidence above is from controlled apps.
