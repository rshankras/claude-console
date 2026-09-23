# Copy Reply with multiple web areas — 2026-09-22

Version **0.17.8**, branch `integrate/vizhi-desktop-main`.

## Confirmed failure

The owner's 19:57:44 tap on 0.17.7 produced `key-pressed`, `requested`, then
`reply-web-area-multiple`. The request reached the native helper and was rejected before
pressing Copy. This confirms that the focused Codex window exposed multiple AXWebAreas.
Their identities were not inspected; an auxiliary diff/preview is a plausible source, not a
live observation. Earlier isolated single-area tests could not reproduce this guard.

The previous helper reproduces exactly the same refusal in an isolated Chromium conversation
with a preview iframe, saved in `artifacts/desktop-copy-webareas/before-reproduced/`.
The fixture's preview deliberately includes a misleading **Copy response** control so the
fix must distinguish the conversation from merely finding a button with that name.

## Correction

Partition the focused window's accessibility nodes by their nearest AXWebArea ancestor.
When multiple areas exist, exactly one must contain the adapter's recognized user or
assistant speaker headings. Heading text uses the same existing direct/child-text handling.
Look for the latest assistant reply and its native copy action only inside that area.

Nested areas retain a boundary node in the parent but their controls and text are excluded.
An unrelated code Copy cannot borrow a response action across that boundary. Two areas with
speaker headings remain ambiguous, including a second area containing only a new user turn.
An unidentified layout still fails closed. A single web area retains the existing behavior.

Window-wide dialog, activity, voice, approval and selected-conversation guards remain in
place. The copy operation still pins its window, conversation and button and requires the
app's acknowledgement plus fresh clipboard text. A later change to the selected conversation
or response cannot be reported as success. No message text or clipboard content is logged.
Profile bindings and other input commands are unchanged.

## Verification

Production Swift selector regressions cover sibling/nested previews, a conversation nested
inside an outer preview, two conversations, pending user turns, missing latest copy, and
cross-frame footer boundaries. Native Chromium fixtures exercise actual accessibility and a
named test clipboard, including misleading preview Copy buttons, running tasks, code-only
copies, duplicate controls, missing acknowledgement and a latest user message.

The first reproduction attempt stopped on an incorrect harness assertion: the production
`inspect` output intentionally omits non-control web-area nodes. The fixture now checks that
its auxiliary controls are accessible; the old helper's status and copy results establish
the multiple-area refusal. That harness-only attempt is retained in `before-chromium/`.

Detailed tests, build, package and installation evidence: `artifacts/desktop-copy-webareas/`.
Controlled fixture success remains separate from physical-keypad acceptance.

## Owner acceptance

The owner confirmed Copy Reply works on 0.17.8. The 20:17:14 keypad tap recorded
`key-pressed`, `requested`, then `copied` at 20:17:16, confirming native acknowledgement
and fresh response text. Temporary tracing was disabled by removing its enable file.
Only fixed result codes are retained in `artifacts/desktop-copy-webareas/owner-acceptance.json`.

## Post-verification cleanup

Removed 28 duplicate Chromium app bundles and their synthetic caches from Copy Reply test
artifacts, plus the rejected package left by the initial network timeout. Preserved generated
fixture sources, metadata, test results, logs, helper binaries, the working package and rollback
backups. The shared Electron runtime remains available. The fixture runner now performs this
cleanup after each completed run unless retention is explicitly enabled. A fresh preview-copy
scenario passed and verified automatic cleanup. The installed 0.17.8 binaries were unchanged.
Evidence: `artifacts/desktop-copy-cleanup/cleanup.json` and `cleanup-smoke/fixture-result.json`.
