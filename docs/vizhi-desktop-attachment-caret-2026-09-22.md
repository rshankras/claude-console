# Screenshot → Explain: rich-text cursor mismatch

Branch: `integrate/vizhi-desktop-main`. Patch: 0.17.3.

## Evidence

Owner taps on 0.17.2 recorded `composer-selection-changed` before insertion. The owner
confirmed Explain works without a screenshot and fails with one attached. Version 0.17.1's
AppKit range model was incomplete: its placeholder was absent from AXStringForRange.

A disposable Chromium editor using a generated CSS paragraph placeholder reports both
AXValue and AXStringForRange as `Work with ChatGPT\n`, with 18 AX characters. Its editable
text is empty, however, and native Command–Down leaves the caret at zero. After a screenshot
enables Send, the old code mistakes those rendered characters for draft content. Depending
on setter semantics it refuses the cursor or inserts but cannot confirm the result.
The old helper reproduced `append-unconfirmed` in Chromium. The AppKit regression, using
the same range content with the cursor constrained to actual empty text, reproduced the
owner's exact `composer-selection-changed` refusal with the installed 0.17.2 helper.

These observations are from controlled fixture apps, not a dump of the owner's ChatGPT
input. They explain a mechanism consistent with the live refusal, not direct proof of its
exact private accessibility tree.

## Change

For an exact adapter-configured placeholder matching the composer's description, append
uses native end-of-input navigation rather than assigning an offset into generated text.
A real literal draft moves to its text end; a generated hint can remain at zero. Insertion
requires the same focused editor, window, mode, chat, unchanged initial value, and an empty
selection. It never selects all or replaces AXValue. Complete text readback must confirm
success; an uncertain or partial write does not trigger a second paste.

Explicit retry also recognizes the complete instruction after a rendered placeholder has
disappeared. This is limited to the original configured placeholder fingerprint and the
same composer label. An unchanged placeholder, extra text, or a different draft cannot
satisfy that check. Normal append fingerprint checks remain in force. Screenshot attachments
remain in the editor, the clipboard is restored, and Send remains a separate explicit action.
The ordinary draft-overwrite eligibility rules are unchanged.

## Verification status

Full suite: 2,019 C# tests passed, 13 skipped; script suites passed. The package is built
and verified, and its native helper is byte-identical to the one under fixture testing.
All eight GUI runs passed with those exact helper bytes: AppKit screenshot/instruction
(9 steps), append/dictation (8), files (7), and full app workflow (37); Chromium generated
placeholder + image, literal placeholder text + image, Unicode notes + image, and append/send.
The old helper reproduced the exact cursor refusal before the fix. Initial locked-session
attempts are retained as blocked evidence rather than counted as passes.

Version 0.17.3 was installed and loaded on 2026-09-22 in 224 ms. The runtime helper matches
the tested package; selected profile, profile contents, system profile and desktop workflow
configuration were verified unchanged. No live ChatGPT insertion was performed by automation.
The owner subsequently reported: “now the prompt seems to work”. This confirms the
reported Screenshot → Explain insertion step on hardware, not yet the full send/response
workflow or each other prompt individually.

[Installation receipt](../artifacts/desktop-attachment-caret/installation.json).
Rollback backup: `/Users/ravishankar/.claude/claude-console/backups/vizhi-desktop/20260922-095852-attachment-caret`.
Evidence: `artifacts/desktop-attachment-caret/`.

Owner retry after installation: keep the screenshot in an otherwise empty composer and
tap **Tools → Prompts → Explain** once. The instruction should appear beside the image,
and the key should show **Send Draft**. Nothing should be sent until the owner presses Send.


## Other prompts

The correction is in the shared native `append` action, not in the Explain recipe.
All nine installed ChatGPT workflows and all nine installed Codex workflows route existing
material through `DesktopWorkflowVoice` → `AppendPreparedDraft`. Spoken workflows reach
that same method after transcription. Summarize, Explain, Brainstorm, Plan and Continue
choose their source-specific instruction when material exists; Rewrite, Draft Reply,
Compare and Research include the user's spoken brief. Insertion leaves the draft unsent.
Empty-composer conversation prompts retain their existing submission behavior.

The existing parameterized workflow tests cover source instructions for the five preset
ChatGPT prompts. The native cursor correction was exercised across AppKit and Chromium,
including retry, literal placeholder text, attachment preservation and explicit Send.
This shared-path review is not a claim of individual physical-keypad acceptance for every
prompt. No additional plugin or profile update is needed for those commands.

The owner subsequently completed the guided two-quote Compare → Plan → Copy Reply
workflow and reported “all good”. This adds physical acceptance for spoken Compare
insertion beside file attachments and the follow-up Plan workflow. Other prompts remain
covered by shared-path tests rather than individual hardware confirmation.
