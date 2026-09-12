# Follow-up fixes for PR #95 review

Branch: `fix/pr95-review`, based on merged PR #95 (`621dd6a`). Updated Windows binaries have been loaded for local device testing; no final release package has been verified or published.

## Changes

- **Settings writes:** capture the original JSON source and node identities before mutation. Untouched values retain their exact source, including inline objects/arrays, foreign nested hooks, comments, escape spelling, and whitespace. New/replaced values use the detected layout. The rewritten document is parsed and checked against the intended values before the existing backup/replace transaction.
- **macOS cleanup:** the self-contained uninstall script performs equivalent source-preserving edits, keeping UTF-8 BOMs, CRLF/LF and the presence or absence of the final newline. It accepts comments and trailing commas like the plugin reader. Existing locking, rolling backup and conflict detection remain in place.
- **Windows focus:** a duplicate title is selected only if the nonce identifies the target. Failure raises the window and returns the unresolved-focus outcome; it never selects the first matching tab as a guess. This prevents wrong-tab selection but does not add a new Windows Terminal identity API.
- **Windows voice:** the key shows Starting while a background worker launches the helper. The helper writes a readiness acknowledgement only after `waveInStart` succeeds; only then does the key animate Listening. A second voice-key press during startup requests cancellation. The worker stops its helper before releasing capture ownership, and a late readiness acknowledgement cannot revive a cancelled start. Startup errors/timeouts release the state and display a failure. macOS retains its existing capture flow.

## Validation on 11 September 2026

- `bash tests/run-all.sh`: 1,018 C# passed, 8 Windows executable-contract tests skipped; 54 bridge-script checks, 27 Codex-hook checks and 7 cleanup tests passed. Live settings/opt-out and IPC canary checks passed.
- Regression coverage includes inline settings, comments/trailing commas, mixed foreign/owned hook groups, CRLF, tabs, UTF-8 BOM, missing final newline, ambiguous tab titles, cold-start display, and cancellation with late acknowledgement.
- Claude Console Release build with `SkipPluginLink=true`: success, no warnings/errors.
- Focus and voice helpers published for `win-x64` and `win-arm64`. Focus emits the COM trimming warning already associated with its existing publish configuration; native behavior still needs Windows validation.
- `git diff --check`: clean.

## Final-package checks still required

1. Build the release package with both updated Windows helpers and the updated plugin DLL, then run the package verifier. An old voice helper cannot acknowledge readiness; it must not be paired with this DLL.
2. On Windows, cold-start Dictate: Starting must appear immediately, then Listening only when capture is ready. Stop during Starting and verify no helper remains, then start again. Repeat with Voice Draft and Go to Project.
3. On Windows, use two equal-title tabs with title propagation disabled: unresolved focus must not select an arbitrary tab. Unique-title and successful nonce selection should still work.
4. On macOS, enable/disable and uninstall/reinstall through Options+ using settings containing foreign hooks and custom formatting. Verify the packaged script preserves the same content as the automated tests.

## Windows device follow-up on 12 September 2026

- Failed Windows tab focus now leaves the pin, active session, and persisted selection unchanged and reports a warning. Only focus-helper exit 0 permits the selection to change. Other supported backends retain their existing focus flow.
- The focus helper also attempts nonce identity after unmatched-title retries, rather than only for duplicate titles.
- Eight regression cases cover focus outcomes and preservation of routing and pins. The focused suite passed 102 tests. The full Windows run passed 1,033 tests with one hook-fixture timeout; all eight hook contract tests passed on retry. Standalone helper smoke checks and live settings/IPC preservation checks passed.
- Release DLL and Windows x64 focus helper builds passed; the existing COM trimming warning remains.
- Physical testing confirmed Dictate delivery, Voice Draft, voice startup cancellation/restart, and keypad startup/listening/cancellation labels. Logs showed no remaining voice helper after cancellation testing.
- A live session's console title matched no visible tab, and its nonce did not propagate to a tab label. The new pin guard was verified against that failure. Restarting that session restored navigation; subsequent switching among all three sessions passed user observation and log checks, including after voice interactions.
- **Known limitation:** the original title mismatch's cause remains unconfirmed. The unmatched-title fallback did not resolve that original session. Successful switching after restart is not proof that the mismatch cannot recur.

Final release-package verification, packaged macOS checks, and Windows ARM64 device validation remain outstanding. The updated Go to Project voice flow was not separately validated on the physical device.
