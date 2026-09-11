# Follow-up fixes for PR #95 review

Branch: `fix/pr95-review`, based on merged PR #95 (`621dd6a`). These changes are local; no package has been installed or published.

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

No fresh Windows/device pass or final package verification is claimed by the cross-builds.
