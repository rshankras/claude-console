# Vizhi Desktop: Voice Draft insertion and keypad recovery

Branch: `integrate/vizhi-desktop-main`. Release: **0.12.13**.

Logging cleanup (unreleased): the temporary composer metadata diagnostic described below
has been removed from the AX helper. Existing-draft checks, placeholder handling, insertion
verification and recovery remain in place. The diagnostic excerpts are historical evidence.

## Report and diagnosis

The owner saw **Paste Draft** after dictation and suspected text previously copied by another
application. Source inspection shows that the original insertion used AX value/selected-text
setters, not the clipboard. After failure, the recovery path copied the transcript and asked
for Cmd+V. Existing clipboard content therefore cannot explain the initial AX insertion failure.
The exact live ChatGPT failure remains unobserved; these changes fix the verified missing
insertion fallback and keypad recovery, without claiming a confirmed live-app root cause.

## Keypad workflow

1. Keep the intended conversation open with an empty composer.
2. On Controls, press **Voice Draft** (top right), speak, then press again to stop.
3. Vizhi inserts the transcript automatically without reading or replacing the system clipboard.
   The key briefly shows **Draft Ready · SEND DRAFT** when insertion is confirmed.
4. Review the composer. Press **Send Draft** (bottom middle) to submit it once.
5. If insertion fails, the Voice Draft key becomes **Insert Draft · HOLD TO DISCARD**.
   **Tap** to retry insertion into the selected composer. **Hold** until **Discarded** appears
   to forget the retained recording and reset to Voice Draft. The next fresh tap records again.
6. Discarding clears only Vizhi's retained recording. It does not clear the app's input or the
   clipboard. A different existing app draft still blocks insertion; finish or move it before
   retrying. Partial insertions are preserved too; retry never duplicates the entire transcript.

Existing profile bindings remain valid. No profile replacement is needed. The recovered
transcript is kept only in plugin memory until insertion or plugin restart. Keep the intended
conversation selected through transcription; retry is an explicit request targeting the
currently selected conversation. If the transcript needs editing, edit it in the app before Send.

## Implementation and boundaries

- Preserve direct AX setters, with exact readback instead of substring success. Try another
  insertion method only while the composer is still empty.
- If both setters fail, focus and insert in one helper invocation using targeted Unicode keyboard
  events. The helper checks the pinned window/title, focused editor, and expected text before
  each small UTF-16 payload. It waits for exact readback before continuing or reporting success.
- This uses Apple's [Unicode keyboard event API](https://developer.apple.com/documentation/coregraphics/cgevent/keyboardsetunicodestring(stringlength:unicodestring:)).
  Apple documents that frameworks may ignore those strings; readback is mandatory. An event
  being posted is never treated as proof that insertion worked.
- The callback from shared voice code retains unsuccessful desktop drafts in a desktop-owned
  service. No transcript clipboard recovery or transcript diagnostic logging is used by that path.
- Explicit recovery recognizes an exact already-inserted transcript, so delayed/uncertain
  insertion cannot duplicate it. It refuses a different existing draft and cannot request send.
- The existing Send Draft action remains the separate, guarded submission step, with duplicate
  press protection. Voice Chat, Speak Query, model files and the signed microphone helper are
  unaffected.

## Validation

The controlled fixture covers rejected AX setters with actual Unicode event delivery, multi-chunk
accented/Tamil/emoji text, no automatic send, idempotent recovery, explicit send once, partial
writes, and changing windows during insertion. C# tests exercise retention, failed retries,
exceptions, command arguments, feedback and the separate production Send handler.

Full package/software results and install hashes are recorded under
`artifacts/desktop-tests/` and `artifacts/desktop-draft-recovery/installation.json`.
A controlled AppKit fixture does not establish ChatGPT's live framework behavior. The physical
acceptance step is owner-run: copy unrelated text, dictate the customer reply, check that only
the transcript appears, review it, then press Send Draft. No ChatGPT/keypad pass is inferred
from simulated or fixture results.

## 0.12.11: empty paragraph and automatic completion feedback

The owner reported **Insert Draft / Clear Composer** and reiterated that speaking then stopping
should populate the chat input automatically. Source inspection found overly literal empty-value
and readback checks: `"\n"` counted as an occupied composer, and `"transcript\n"` counted as failed
insertion of `"transcript"`. This is a reproducible supported-editor edge case, not a confirmed
observation of the owner's live AX value.

Insertion eligibility/readback now ignores only outer whitespace. It never removes placeholder
words, internal whitespace, or different draft content, and it inserts the original transcript
without trimming its actual text. The fixture simulates a rich editor whose AX value includes a
trailing empty paragraph; automated checks cover initial insertion, idempotent retry, different
existing-draft refusal, separate send, and empty-send refusal. A confirmed normal insertion now
signals **Draft Ready / SEND DRAFT**, as a successful retry already did.

This does not enable overwriting or appending to a different existing draft. The owner still needs
to confirm whether their blocked input was empty, already contained the transcript, or contained
other text; the diagnosis of their specific live failure remains open until then.

## Live log diagnosis after 0.12.11

The loaded DLL and helper matched the installed release hashes. At 16:55:15.324 the first
post-recording write returned `draft-exists`, and the 16:55:22.600 retry returned the same error.
This proves the refusal occurs at the initial non-empty-value guard, before any insertion method.
The outer-whitespace fix did not resolve this owner's live case. The logs did not contain the
information needed to distinguish real draft content, placeholder representation, or invisible
format characters; none is yet confirmed as the cause.

A diagnostic-only helper overlay was installed without restarting Logi, preserving the retained
transcript. It adds scalar/boolean stderr metadata only on `draft-exists`: lengths, character
categories, whether the AX value matches the placeholder/description/transcript, reported character
count, and focus/editability flags. It records no text, title, transcript, hash, or encoded content.
The plugin DLL/profile and insertion behavior remain at 0.12.11. The exact overlay hash, prior
helper backup and rollback files are in
`artifacts/desktop-draft-recovery/diagnostic-installation.json`. The standard release package does
not include this diagnostic overlay yet.

The new diagnostic branch passed its controlled native composer/word-preservation assertions;
the broader fixture later lost foreground during an unrelated search check and did not finish.
A physical **Insert Draft** retry is requested to obtain the new metadata. Until that arrives,
this is an instrumented investigation, not another claimed live fix.

### Diagnostic retry received at 17:10 on 2026-09-19

The owner pressed Insert Draft and the diagnostic helper matched both installed copies. The
retries consistently reported 12 UTF-16 units (11 after trimming), one control character, zero
format characters, `characterCount=12`, focus/enabled/value-settable all true, and a value that
matches the description but differs from the retained transcript. No separate placeholder value
was exposed. This rules out a whitespace-only value; it does not prove whether the reported
content is a visible existing draft or a label represented as the value. A screenshot of the
unchanged input box is needed to distinguish those cases. No new insertion behavior was installed
based on this incomplete classification.

## 0.12.12: short press retries, long press discards

The owner asked for a way out of the retained-draft state. The SDK's existing
`ProcessButtonEvent2` / `DeviceButtonEventType.LongPress` route is now used by Voice Draft.
A short press acts only on release. A hold consumes that release, discards only the idle pending
transcript identified at button-down, and clears the failure face across command instances.
Duplicate long/release events are inert. Holding while recording/processing cannot discard a
late result or start/stop recording. No hold duration is invented; recognition uses the SDK's
long-press event. Hold until the keypad shows **Discarded**.

The app's existing draft is preserved. This feature provides a reset; it does not establish or
claim a fix for the still-unresolved live composer classification. The owner screenshot requested
for that separate diagnosis is still outstanding.

### Live attempts at 17:38 on 2026-09-19

The owner reported two attempts followed by **Insert Draft / Hold to Discard**. Release 0.12.12
was loaded. Recording stopped at 17:38:14.548 and insertion was refused at 17:38:15.410. A physical
long press reached the discard handler at 17:38:21.278 and cleared the retained transcript. A new
recording started at 17:38:24.124, stopped at 17:38:29.441, and was refused at 17:38:30.289.

Both refusals occurred before insertion: the reported input contained 18 UTF-16 units, 17 after
trimming, matching its accessibility description but not the new transcript. No placeholder was
exposed. This confirms discard dispatch and explains why the recovery key returns after the next
recording; it does not establish that the visible input actually contains text. The visible-input
question remains open. Non-content evidence is saved in
`artifacts/desktop-draft-recovery/live-0.12.12-log-check.json`. Key label transitions and successful
live insertion are not marked as passed from these logs alone.

## 0.12.13: empty prompt mirrored into the accessibility value

The owner confirmed the message input was visibly empty during both 17:38 attempts. Their
earlier screenshot shows **Work with ChatGPT**, which is 17 characters and matches the diagnostic
length after trimming. The logged value equals its description, but the app exposes no separate
placeholder attribute. This supports placeholder misclassification; the diagnostic does not log
the actual words. The earlier insertion guard rejected every nonempty value before trying any
setter or keyboard path. Clipboard state was not involved.

The adapter now passes that observed exact prompt and the Send label into the helper. A value is
treated as an empty prompt only when it exactly matches the configured prompt and description,
the reported cursor is at position zero with no selection, and there is no enabled or unknown-state
Send control in the pinned window. A real draft with an enabled Send control is still protected,
including a draft containing exactly the prompt words. No other description is stripped.

The prompt path skips both replacement setters and uses insertion-only Unicode keyboard events.
Every chunk verifies the original window, focused editor, expected prefix and zero-length selection
at the insertion point. Exact readback is required; insertion is never automatically submitted by
Voice Draft. Missing selection metadata remains a refusal rather than an unverified write.

The native fixture reproduces the observed 18-character value/count with an empty visible editor,
matching description and absent placeholder attribute. It covers multi-chunk insertion, a transcript
equal to the prompt, exact retry, separate send, real text protection, and unknown configuration.
These tests establish the helper behavior, not a live ChatGPT pass. The next owner recording will
confirm whether this app build supplies the cursor and Send evidence required by the fix.
