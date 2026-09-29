# Prompt insertion retry investigation

The owner still sees Insert Draft / Hold to clear after tapping Explain, and a tap does
not visibly insert the instruction. Version 0.17.1 was confirmed installed by the runtime
helper hash. Its controlled screenshot-only regression passed, but that did not establish
compatibility with this live failure.

The initial delivery path discarded its native error. Retry showed a short generic error
before returning to the same pending-draft face. Version 0.17.2 preserves the failure reason
and shows a relevant cursor, focus, changed-input, or changed-chat hint. It records a fixed,
allowlisted failure code only when an explicit insertion fails; no prompt, clipboard,
screenshot, title, helper-response, or exception text is logged.

Owner taps on 2026-09-22 captured `composer-selection-changed` for initial insertion and
retries. The owner confirmed insertion succeeds without an attachment and fails with a
screenshot attached. This refusal occurs before any text is inserted. Two intervening
retries also returned `composer-target-changed`. No live success has been claimed for 0.17.2.

Verification: 2,019 C# tests passed, 13 skipped; script suites passed. A new regression
checks that an initial failure and repeated retry preserve the same instruction, target,
and fingerprint, never send, and recover to Send Draft after successful insertion. The
native helper is byte-identical to the six verified AppKit/Chromium fixture copies from
0.17.1. Profiles and workflow configuration remain unchanged.

Installed and loaded 2026-09-22 in 224 ms. Evidence and receipt:
`artifacts/desktop-prompt-retry/installation.json`.
Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260922-093020-prompt-retry`.

The captured cursor refusal led to the [0.17.3 rich-text placeholder correction](vizhi-desktop-attachment-caret-2026-09-22.md), now installed after controlled GUI verification. Owner acceptance is pending.
