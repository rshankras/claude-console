# Claude Console 2.2.0 — Windows QA without a keypad

This runbook tests the final `fix/qa-retest-2.2.0` package built from commit `91921cb`.

It can validate the release-blocking Windows hook-process fix (#57) without Logitech hardware. It
also checks the packaged Windows microphone and Claude-session discovery helpers. The physical
Yes/No faces and key behavior (#58), and the Windows Terminal navigation notice (#61), still require
an MX Creative Keypad.

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

Double-click `ClaudeConsole_2.2.0-retest-91921cb.lplug4` and let Logi Options+ install it. Wait until
the installation finishes.

A keypad does not need to be connected for the helper and package checks below.

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
- The installed `claude-console-hook.exe` is byte-for-byte identical to the package.
- Status-line, permission, and Codex hook invocations exit even when their input pipe remains open.
- Twenty-four concurrent blocked-input hooks all exit within five seconds.
- No test-owned hook process remains piled up.
- The Windows microphone helper can access the default microphone.
- The injection helper can discover a native Claude Code session.

If the script reports that the installed helper was not found, reinstall the `.lplug4` and rerun it.
If Claude discovery fails, confirm that `claude` is still running in the other Windows Terminal tab.

## Return the evidence

Copy the complete PowerShell output back into the Claude Console review conversation. It will be
reviewed before the GitHub findings are updated. Do not close #57 based only on the word `PASS`; the
output must also show that the installed helper matched the package.

## Still requires a keypad

- #58 / item 2: Yes and No setup faces, permission-prompt behavior, and post-enable behavior.
- #60: both approval dots and clearing the pending prompt after No.
- #61 / item 16: visible and audible Windows Terminal requirement when navigation is attempted from
  classic Console Host.
