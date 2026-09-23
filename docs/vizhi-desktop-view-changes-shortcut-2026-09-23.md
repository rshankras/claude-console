# View Changes when the summary control is absent

## Owner evidence

The 2026-09-23 06:28:56 tap on 0.17.12 recorded `requested` followed by `panel-opener-missing`. The helper did not press a control. The earlier formatting fix addressed a reproduced matching bug but did not resolve this live failure.

The installed app's static command definition provides `openReviewTab`, titled Open review tab, available to local Codex with default Control–Shift–G. Its `toggleSidePanel` command (Toggle Review Panel) is a separate definition with a different shortcut and is not used.

## Version 0.17.13

The adapter declares the Open Review shortcut. In one bounded helper operation:

1. Pin the foreground window, mode and selected conversation when exposed. Reject dialogs and menus.
2. If the review panel is already visible, return Opened without sending any event.
3. If one enabled Changes summary button is available, use the existing exact semantic matcher.
4. If no summary control is exposed, use the adapter's known Open Review shortcut. A matching disabled or ambiguous control is a refusal, not a reason to send the shortcut.
5. Recheck the target immediately before the operation, send key-down/key-up to the original PID only, and confirm the review panel before reporting Opened.

No text is typed, no clipboard is used, and no draft is submitted. Repeated taps do not toggle a visible panel closed. Native keyboard events are dispatched only after an explicit keypad tap; there is no extra polling.

This uses the app's shipped default shortcut. A user-remapped or unavailable Open Review shortcut may not open the panel; the command reports `panel-shortcut-unconfirmed` instead of claiming success. Existing target, mode, obstruction and panel confirmation guards remain in effect. The current confirmation signal still depends on the review-header Show files/Hide files control.

## Verification and limits

Evidence is under `artifacts/desktop-view-changes-shortcut/`.

- With its summary button absent, the prior native helper reproduces `panel-opener-missing`; no click or key event is sent.
- The updated native helper sends one Control–Shift–G event to an isolated Chromium fixture, confirms the panel, and leaves the unsent draft and test clipboard unchanged.
- A repeated tap and an already-open panel send no extra event.
- Shortcut cases cover no acknowledgement, wrong mode, a dialog, disabled and ambiguous openers, mode change after dispatch, and an unconfigured shortcut.
- The twelve prior panel-matching/confirmation scenarios remain covered.
- Full repository checks and package verification precede installation. Runtime hashes, service load and unchanged profiles/configuration are verified.

Fixtures prove the dispatch and guards; the real app/keypad still requires the owner to tap View Changes after installation. No direct live app inspection was performed. Expiring, fixed-code-only diagnostics remain available for that tap.

Test history: two initial no-ack runs correctly refused with the coarse panel-target-changed code after dispatch, leaving the draft intact; the exact changing target was not established. Splitting that diagnostic into foreground/window/surface/conversation codes did not weaken any guard. The subsequent controlled no-ack run returned panel-shortcut-unconfirmed as intended. Initial evidence is retained separately from final verification.

A later no-ack run identified the changing target as foreground focus (`panel-foreground-changed`). That refusal is retained as guard evidence; it does not replace the separate successful no-ack assertion. The no-ack proof under `no-ack-investigation/` passed before the later dialog-guard correction. It is retained as intermediate evidence rather than final package verification.

The native dialog regression exposed a real guard omission: Chromium uses the `AXApplicationDialog` subrole, while View Changes checked only `AXDialog`. The correction includes both subroles. The unsuccessful pre-fix test is preserved under `verified/native-shortcut-dialog/`.

Current verification: 2,106 C# tests passed, 13 skipped; all repository suites and package verification passed. The final package and helper hashes match. Native reruns under `final-verification/` could not reach the fixture surface because the Mac was locked (confirmed by the OS session lock state). The test sequence was stopped, and the current installed version remains 0.17.12. Final native regression verification and installation of 0.17.13 are pending an unlocked desktop. See `verification-status.json`; intermediate passes do not establish that the final package passed all native scenarios.

Follow-up: all 21 final-helper native scenarios passed under `artifacts/desktop-home-screenshot/`. The dialog correction and fallback are included in 0.17.14 alongside the Home/Tools profile swap; 0.17.13 was not installed. See [Screenshot on Home](vizhi-desktop-home-screenshot-2026-09-23.md) for installation evidence and current acceptance limits.
