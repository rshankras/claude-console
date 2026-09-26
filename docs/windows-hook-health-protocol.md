# Windows hook failure reporting and launcher validation

The Claude launcher remains the encoded PowerShell command. Replacing it with Bash has not been
validated on Windows and is not part of this change. The current form already has Windows contract
tests for Git Bash, direct PowerShell, Unicode input and paths containing spaces/apostrophes. Those
tests must run on the Windows laptop before release; a passing Mac test run is not that evidence.

## Failure and recovery records

Records live under `<product IPC root>/hook-health/<helper path hash>/`. The hash is lowercase SHA-256
of UTF-8 `Path.GetFullPath(helperPath).ToUpperInvariant()`. Product IPC roots remain separate:
`%TEMP%\claude-console` and `%TEMP%\codex-console`. Moving or replacing a helper must require fresh
delivery; records from another installation path must not make this installation healthy.

- `failure-<observedUtcTicks>-<guid>.json`: immutable object containing `schema: 1`,
  `observedUtcTicks` (UTC `DateTime` ticks), and `reason`. Writers retain the newest 16 failures.
  Concurrent writers cannot overwrite a later failure with an earlier one.
- `success-<sessionKey>.json`: atomic object containing `schema: 1`, `sessionKey`, `event`,
  `startedUtcTicks`, and `completedUtcTicks`. The helper captures start time immediately after
  arming its watchdog and writes the record only after successful keyed state/pending writes.
  Shared fallback writes and an invocation log are not recovery evidence.

Success does not delete failure history. A hook that started before the latest failure cannot recover
the bridge by finishing afterwards. A fresh observation from one session does not validate another
session's cached approval. A fresh statusline or Notification also does not make an older permission
payload current. The monitor and approval gate enforce those separate checks.

Windows Claude statusline, activity and pending JSON and Codex hook envelopes contain `hookStartedUtcTicks`.
This is transport metadata supplied by the helper, bound to the actual permission payload. The
approval gate requires that invocation to start after the recovery boundary. A file modification
time alone cannot provide this guarantee: a permission hook started before the failure could finish
late while a newer statusline hook restores general health. Older helpers and ordinary rollout
approval snapshots do not provide this proof. Codex code-mode approvals remain supported when
the exact `rollout-code-mode` transport supplies both `observationStartedUtcTicks` (scan start)
and `rolloutEventUtcTicks` (the original request event), both after the recovery boundary.
The rollout reader publishes that authority only after reading through a complete end of the
transcript; partial reads, unresolved trailing lines and truncation remove it. A fresh helper receipt
for that same session is still required. Re-reading an old request does not create a new approval.
The same start-time check applies when publishing live values and activity. Stamping IPC leaves
the original stdin unchanged for the user's chained statusline, including its formatting and Unicode.

The Claude launcher records missing files, native launch exceptions, nonzero exits and input timeouts.
It preserves UTF-8 stdin, stdout and native exit codes, and remains silent for a missing helper. The
helper records failed required keyed writes, including failures inside the Codex path that must keep
returning exit 0 to the CLI. Missing permission input clears any old Claude pending payload and does
not publish a success receipt. Diagnostic writes are best effort and must never break a user's hook.

These records describe observed failures, not antivirus diagnosis. If endpoint protection blocks the
PowerShell process before it can write a marker while the helper still exists, there is no new signal
to distinguish that from an idle session. A marker also cannot be guaranteed when its directory is
unwritable. Do not infer CrowdStrike's detection reason from either case; collect its detection logs.

The Codex installed command is unchanged because changing it re-prompts hook trust. Its helper can
publish these receipts without altering that command. Any future Codex launcher guard is coordinated
with the signing release.

## Windows laptop gate

Run with normal Defender/McAfee protection enabled, in an isolated test installation:

1. Run `dotnet test tests/ClaudeConsolePlugin.Tests.csproj --filter FullyQualifiedName~WindowsHookContractTests`.
   Confirm both ordinary Windows tests and Git Bash tests actually execute rather than skip. Include
   Unicode payloads and a helper path with spaces and an apostrophe. Keep the output with the build ID.
2. In a real Claude session, configure a disposable user statusline that reads input and prints a
   recognizable string. Confirm the existing chain still receives the input and displays its output.
   Restore the user's original statusline afterwards. Do not use production settings for automated tests.
3. Establish live status and a pinned session, then temporarily rename the hook executable. Press a
   live key and Yes/No. Expect Blocked, an Options+ status warning, and no input sent to the terminal.
4. Restore the helper and trigger a fresh hook. Expect automatic recovery. In two sessions, recovery
   of session A must not enable an old pending approval in session B. A fresh statusline alone must
   not enable an approval observed before the failure.
5. Test Yes on one visible fresh permission prompt and No on a separate fresh prompt. Repeat after
   restarting the plugin while the helper is unavailable to verify the failure survives a reload.
6. Exercise a present but unlaunchable helper in the isolated contract fixture. Renaming a file tests
   absence, not prevention of execution. Confirm a launch failure marker and no false recovery.
7. In Codex, test a code-mode approval separately from a normal hook-reported permission. After a
   helper failure, an old code-mode request must remain disabled; a new request with fresh helper
   delivery must allow Yes and No on separate prompts. Keep #111 open if this fails on the protected
   Windows setup.

Only reconsider a Bash launcher after establishing the actual shell used by each supported Claude
Code version on the Windows laptop, including when Git Bash is absent. Require the same input,
quoting, exit-code, chaining, timeout and failure-reporting results before changing the command.

The final signed package still requires Logitech QA to repeat the Yes/No and recovery checks with
their CrowdStrike policy enabled. Defender/McAfee results do not establish CrowdStrike acceptance.
