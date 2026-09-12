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

## Renamed tabs and Git Bash hooks follow-up

- Manual tab renaming reproduced the navigation problem; clearing the custom names restored switching. An experimental candidate scan attempted to verify each selected tab through a console-title challenge. The renamed-tab retest below disproved its window-title assumption, and the experiment has been reverted.
- The hook failure was reproduced with Git Bash: `cmd.exe /d /c` entered interactive cmd instead of invoking the helper, and cmd tried to execute the JSON input. An encoded PowerShell launcher avoids MSYS switch conversion, quotes literal paths, preserves Unicode input, and silently skips a missing helper. Existing plugin-owned wiring migrates through the source-preserving settings writer.
- Windows strict suite: **1,037 passed, zero failed or skipped**. Standalone focus/screenshot helper smoke checks and live settings/IPC preservation checks passed. The macOS-only script suites were not run on Windows.
- Integration coverage includes execution through Git Bash and PowerShell, Unicode payloads, special characters in paths, missing helper cleanup, identity verification, failed selection restoration, and automation exceptions.
- Release plugin build passed with zero warnings/errors. Windows x64 focus publish passed with the existing COM trimming warning. Updated DLL and focus executable were copied into the local installation after backing up the prior binaries; reload was requested.
- After reload, the live statusline and five activity commands migrated successfully. Fresh per-process status files contain the correct Stage, FAQ, and Presskit project paths; the installed hook invocation log confirms execution from each project.
- Before the revert, 66 focus regression tests passed and the experimental helper passed nine direct switches with automatic tab names. These results did not validate renamed tabs and do not establish that the experiment fixed #88.

### Renamed-tab retest: failed

- User renamed Presskit's tab to `kk`. Keypad attempts at 11:52–11:53 repeatedly logged `slot 3 focus unresolved; selection unchanged`.
- A direct diagnostic run against Presskit's verified PID/start time returned exit 4. While that candidate was selected, both its tab name and window name remained `kk` through every console-title challenge. The assumption that the window title bypasses a manual tab name is false in this configuration.
- The visible cycling is the candidate scan; after failing identity verification it restores the original tab. The renamed-tab issue is **not fixed**. The nine successful direct switches above validated automatic titles only. Clearing the custom tab name remains the previously confirmed workaround; a complete fix needs an identity source independent of both displayed titles.

### Disposition

- Retain this as a known **P3** issue in [#88](https://github.com/rshankras/claude-console/issues/88), with clearing the custom tab name as the workaround.
- Revert the experimental focus helper and its scan-specific tests to `c417fe5`. This removes visible candidate cycling. Keep the previously committed focus-failure routing guard and the independently verified Git Bash hook fix.
- Post-revert validation: all 1,037 C# tests passed, standalone helper smoke checks passed, and live settings/IPC were preserved. The rebuilt x64 focus helper was installed and its hash verified. Direct checks returned exit 4 for renamed Presskit (the known limitation) and exit 0 for Stage with its automatic title. No plugin reinstall is needed; the hook-enabled DLL remains installed.
