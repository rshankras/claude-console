# Claude Console 2.2.0 — Windows QA without a keypad

This runbook tests the final `fix/qa-retest-2.2.0` package built from commit `91921cb`.

Without Logitech hardware it validates the core lifetime and pile-up safeguards added for the
release-blocking Windows hook-process defect (#57). It also checks the packaged Windows microphone
and Claude-session discovery helpers. Closing #57 end to end still requires the plugin service to
load the package and exercise its real hook wiring. The physical Yes/No faces and key behavior
(#58), and the Windows Terminal navigation notice (#61), require an MX Creative Keypad.

## Files

Put these two files together in the Windows `Downloads` folder:

- `ClaudeConsole_2.2.0-retest-91921cb.lplug4`
- `ClaudeConsole-Windows-NoKeypad-QA.ps1`

Both files are published only on the `qa/windows-no-keypad` branch. This is a temporary QA branch;
do not merge it into the release branch.

Expected package SHA-256:

```text
464b1608bc2ec2a5bbfa1ed8e6f8a95a614d57502966780d2ddd558fa300d854
```

The PowerShell script verifies this hash before running any test.

## 1. Install the package

If the Windows laptop already has Logi Options+ **and LogiPluginService**, double-click
`ClaudeConsole_2.2.0-retest-91921cb.lplug4` and let the service install it. A disconnected keypad
does not itself prevent installation; the service, not the device, owns package installation.

If the laptop has never installed the Creative Console/plugin-service components, the `.lplug4`
association or service may be absent. Do not treat that as a package failure. Continue with the
helper checks below: the harness extracts and runs the Windows executables directly from the
package. It will print a warning instead of the installed-helper identity PASS, and the result is
then a package-level #57 safeguard test only—not an installed-plugin or end-to-end pass.

## 2. Start Claude Code

Open Windows Terminal and start one native Claude Code session:

```powershell
claude
```

Leave that session running. The discovery test is read-only and uses it to confirm that the packaged
Windows helper can find a real CLI session.

## 3. Run the no-keypad QA harness

Open a second PowerShell tab and run:

```powershell
Set-Location "$env:USERPROFILE\Downloads"
powershell -NoProfile -ExecutionPolicy Bypass -File .\ClaudeConsole-Windows-NoKeypad-QA.ps1 -PackagePath .\ClaudeConsole_2.2.0-retest-91921cb.lplug4 -TestMicrophone -TestClaudeDiscovery
```

The microphone check records one second from the default microphone. Windows might ask for desktop
microphone permission. It does not retain that recording.

The harness does not edit `~/.claude/settings.json`. Its IPC files and unpacked package are placed in
an isolated temporary directory and removed after the run.

## Expected result

The last line should be:

```text
RESULT: PASS — #57 no-keypad Windows helper checks
```

The run verifies:

- The package is exactly the reviewed `91921cb` artifact.
- When LogiPluginService installed the package, its `claude-console-hook.exe` is byte-for-byte
  identical to the reviewed package. If the service is unavailable, this line is a warning.
- Status-line, permission, and Codex hook invocations exit even when their input pipe remains open.
- Twenty-four concurrent blocked-input hooks all exit within five seconds.
- No test-owned hook process remains piled up.
- The Windows microphone helper can access the default microphone.
- The injection helper can discover a native Claude Code session.

If the script reports that the installed helper was not found and LogiPluginService is present,
reinstall the `.lplug4` and rerun it. If the service is not present, retain the warning in the test
evidence. If Claude discovery fails, confirm that `claude` is still running in the other Windows
Terminal tab.

## Recorded result — 2026-09-03

**Outcome: PASS at the package level only. Installed-plugin and end-to-end #57 coverage remain
unverified. Do not close #57 from this result.**

Environment:

- Windows 10.0.26200, x64; MX Creative Keypad disconnected.
- Logi Options+ 2.7.1922.0; LogiPluginService 6.4.1.3246 on .NET 10.0.6.
- Native Claude Code 2.1.251.
- QA branch tip `e41c199`; reviewed package SHA-256 matched
  `464b1608bc2ec2a5bbfa1ed8e6f8a95a614d57502966780d2ddd558fa300d854`.

`LogiPluginTool` 6.1.4.22672 verified the package, but installation returned `Plugin installation
cannot start` while LogiPluginService was running. No installed `claude-console-hook.exe` appeared,
so the installed-helper identity assertion remained a warning. The harness then exercised the
helpers directly from the reviewed package, as permitted by this runbook.

The Codex test runner supplied duplicate `PATH` and `Path` entries. They were normalized only in the
PowerShell test process before invoking the unmodified branch harness; no persistent environment,
Claude settings, package, or repository file was changed for the run.

Repository check: the pre-commit `dotnet test` run reported 898 passed and 23 failed. The failures
were confined to the branch's live-status round-trip/action/prompt tests; the staged change was this
Markdown file only. A representative Windows failure expects the macOS scripts directory even though
`EnsureBridgeInstalled()` intentionally returns without extracting those scripts on Windows. This is
recorded as existing branch test debt, separate from the package-level helper result above.

Combined evidence:

```text
PASS  package SHA-256 matches the final 91921cb build
PASS  Windows hook, voice, and injection helpers are present
WARN  Installed helper was not found. The package stress test will still run, but the Options+ install is not verified.
PASS  statusline bounded-input check exited cleanly in 1667 ms with its input pipe deliberately left open
PASS  PermissionRequest bounded-input check exited cleanly in 285 ms with its input pipe deliberately left open
PASS  PermissionRequest helper wrote state in the isolated temp root
PASS  Codex hook bounded-input check exited cleanly in 1827 ms with its input pipe deliberately left open
PASS  Codex hook wrote state and returned its non-breaking {} contract
INFO  starting 24 concurrent hook processes with every input pipe held open
PASS  all 24 concurrent hooks exited within 5 seconds; no test-owned process piled up
INFO  running the one-second Windows microphone self-test
claude-console-voice selftest

  capture devices: 1
  recording 1s from the default mic...
  captured 32578 bytes, RMS 732.3
  mic is live
PASS  Windows microphone helper self-test completed
INFO  running Claude session discovery; keep a native Claude Code session open
claude-console-inject selftest

  claude.exe discovered with a readable start time and command line

PASS  Windows injection helper discovered at least one candidate session

RESULT: PASS — #57 no-keypad Windows helper checks
The keypad-only #58 answer faces and #61 navigation notice still require Logitech hardware.
```

## Return the evidence

Copy the complete PowerShell output back into the Claude Console review conversation. It will be
reviewed before the GitHub findings are updated. Do not close #57 based only on the word `PASS`; the
output must also show that the installed helper matched the package.

## Still requires a keypad

- #58 / item 2: Yes and No setup faces, permission-prompt behavior, and post-enable behavior.
- #60: both approval dots and clearing the pending prompt after No.
- #61 / item 16: visible and audible Windows Terminal requirement when navigation is attempted from
  classic Console Host.
