# Paste into Chat — 2026-09-21

Branch: `integrate/vizhi-desktop-main`. Version: **0.16.0**.

## Problem and plan

The Clipboard key reported **Text Added**, but kept the copied email in memory until a later
workflow inserted it. An empty chat input contradicted that feedback. Pasting immediately also
requires Dictate to work with an occupied composer rather than asking the user to clear it.

1. Insert clipboard text immediately and rename the existing key **Paste into Chat**.
2. Preserve existing text and append a dictated instruction below it.
3. Confirm insertion before showing success; keep Send separate and retries safe.
4. Retain key bindings/profile positions, test the complete sequence, and package the update.

## User flow

1. Highlight the email text and copy it with **⌘C**.
2. Open the intended ChatGPT conversation.
3. On **Tools**, tap the middle **Paste into Chat** key. **Pasting** becomes **Pasted** after
   the email appears in the input box. Existing draft text stays above it.
4. Type an instruction, or return Home and tap **Dictate**, speak, then tap again to finish.
   The instruction appears below the email. Another Dictate press can add more detail.
5. Review the input and press **Send** when ready.

Spoken saved workflows such as Draft Reply can also append their instructions. Clipboard text
is not staged a second time, so later dictation does not duplicate the email. Selection and
screenshot capture keep their existing staged workflow. Return to App still requires a known
source captured outside ChatGPT; importing clipboard text inside ChatGPT cannot identify its origin.

## Implementation and checks

- The action parameters `clipboard` and `clipboard_review` remain stable. Existing profiles
  receive the new live label and behavior without moving or replacing keys. Codex's main Tools
  position remains Review Changes; its optional clipboard action uses immediate insertion.
- New AX verbs `append-target` and `append` pin the mode, window, composer, selected chat and
  a content fingerprint. No composer text is returned by preparation or logged. Chromium cursor offsets are checked
  against the complete accessible text prefix when its ranges omit paragraph line breaks.
- Append moves the caret without selecting/deleting existing text, uses the editor's insertion
  API, and falls back to native paste only when that API made no change. It restores the clipboard
  unless another copy replaced it. Outer whitespace in the added text is trimmed. Readback tolerates paragraph spacing differences at
  the insertion boundary while checking the original text and full addition.
- A changed draft/chat refuses a delayed insertion. Partial writes report **Check Draft**;
  retries can confirm an existing complete insertion or retry against the original baseline,
  never blindly append after an uncertain result. Dictation keeps its existing hold-to-discard recovery.
- Running responses, approvals and native Voice Chat block new appends. No append operation sends.
- Controlled AppKit tests cover immediate paste, existing selections, subsequent dictation,
  duplicate/partial retries, changed drafts/chats, rich-editor paragraphs, placeholder values,
  ignored setters, clipboard preservation and explicit Send. Chromium and repository test
  results are recorded under `artifacts/paste-into-chat/`.

The existing notarized voice-capture binary and downloaded models are reused. Live ChatGPT
and physical-keypad acceptance of this update remain separate from controlled fixture results.

## Validation evidence

- Repository suite: **1,952 passed, 13 skipped**, plus shell/script checks.
- Production Swift selector, cursor-prefix and append matching regressions: **PASS**.
- Final AppKit fixture: **8/8 checkpoints passed** (`artifacts/paste-into-chat/append-final/result.json`).
- Chromium multiline Unicode paste, subsequent instruction, duplicate-safe retries, clipboard
  restoration and explicit Send: **PASS** (`chromium-append-final5/fixture-result.json`).
- Chromium wrapped Copy Reply regression: **PASS** (`chromium-copy-final/fixture-result.json`).
- Package validation: **PASS**. Packaged AX helper matches the tested helper byte-for-byte.
- Installation evidence, when installed: `artifacts/paste-into-chat/installation.json`.

Earlier fixture runs exposed paragraph-gap and cursor-offset differences in Chromium; both
are now covered. One fixture launch timed out under system load, and its Send button initially
lacked the composer wrapper required by the existing selector. The corrected final fixtures
passed. Temporary investigation output stays in test artifacts, not production logging.
