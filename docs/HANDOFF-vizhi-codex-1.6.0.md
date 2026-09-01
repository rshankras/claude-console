# Handoff — Vizhi Codex 1.6.0 on Windows

**Last updated:** 2026-09-01
**Branch:** `feat/vizhi-codex-1.6.0`
**Remote:** `origin/feat/vizhi-codex-1.6.0`
**Worktree:** `C:\Users\sahan\ravi\claude-console\.worktrees\vizhi-codex-1.6.0`

## Current outcome

Vizhi 1.6.0 is built and loaded through a development `.link`. Windows session discovery,
terminal focus/injection, voice, rollout-derived activity, and folder extraction are working.
The rollout state writer now produces both shared and keyed JSON containing `cwd`.

Official Codex Windows hooks have been enabled using `commandWindows`. The required `command`
field is also a native Windows helper command, rather than `/bin/sh`, for compatibility with
Codex builds that validate or launch `command` before applying the Windows override.

The Windows PowerShell launcher defect is fixed and real `SessionStart`, `UserPromptSubmit`,
`PreToolUse`, `PostToolUse`, and `Stop` hook invocations now reach the helper. Codex CLI 0.152.0
does not dispatch `PermissionRequest` for the nested `exec_command` approval menus created by
code mode, so Vizhi now derives that exact waiting edge from the rollout call/output pair. The
remaining release gate is the final physical Yes/No keypad retest; the latest harmless test prompt
was cancelled before that confirmation was completed.

## Commits already pushed

- `e1e127a feat(vizhi): enable Windows hooks and harden sessions`
- `ae3cbe2 fix(vizhi): recover Windows hook and state writes`

`ae3cbe2` was the remote branch head when this handoff was first written. The next commit includes
this handoff plus the PowerShell launcher and code-mode approval fixes.

## What changed

### Windows hook transport

- `CodexCliAdapter` now declares approval observation and hook trust on Windows.
- `CodexStateBridge` installs hooks on every supported OS.
- Windows hook entries contain both:
  - `command`: native `claude-console-hook.exe codex <Event>`
  - `commandWindows`: PowerShell source using the call operator:
    `& '<helper-path>' codex <Event>`
- Hook envelopes carry `"transport":"hook"`.
- Rollout envelopes carry `"transport":"rollout"`, so rollout state cannot falsely prove hook
  trust.
- `PermissionRequest` hook state is protected from ordinary rollout heartbeats. A real terminal
  lifecycle edge can still clear a stale approval if `PostToolUse` is missed.

### Codex CLI code-mode approvals

- Codex CLI 0.152.0 writes a `response_item/custom_tool_call` named `exec` before showing the
  nested `exec_command` approval menu, but no `PermissionRequest` hook or rollout approval record.
- Vizhi recognises only an outer `exec_command` argument whose top-level
  `sandbox_permissions` is exactly `require_escalated`; text inside the shell command cannot
  create a false approval.
- The synthetic `PermissionRequest` uses transport `rollout-code-mode`, carries the real command,
  and is written to both shared and correlated PID-keyed state so Allow/Yes/No use the same
  `PendingTool` contract as hook approvals.
- Only the matching `custom_tool_call_output` clears the pending call; unrelated output and
  rollout heartbeats leave it waiting.

### Rollout fallback and project names

- First sighting reads a bounded 8 MiB tail, allowing reloads in long turns to recover the most
  recent lifecycle edge.
- The immutable first `session_meta` record is read separately for `cwd` and original timestamp.
- Rollout growth refreshes transcript activity even when NTFS `LastWriteTime` does not move.
- Session state carries `transcript_activity_ts`, which prevents an active long turn from being
  rendered as permanently complete.
- Atomic state replacement uses a unique temporary sibling, retries transient Windows
  `IOException`/`UnauthorizedAccessException`, then falls back to an in-place overwrite when the
  destination can be written but not replaced.
- Windows process discovery excludes false desktop/app-server candidates more carefully.

### User-facing behavior

- A session with `cwd: C:\Users\sahan\ravi\claude-console` should display `claude-console`.
- A session started in `C:\Users\sahan` correctly displays `sahan`; that is the folder name, not a
  project-specific alias.
- “Codex” is only the fallback label when no usable `cwd` is attached to that live session.

## Current installation

- Plugin link:
  `C:\Users\sahan\AppData\Local\Logi\LogiPluginService\Plugins\VizhiCodexPlugin.link`
- Link target:
  `C:\Users\sahan\ravi\claude-console\.worktrees\vizhi-codex-1.6.0\bin\VizhiCodex\Release\`
- Hook helper:
  `...\bin\VizhiCodex\Release\bin\claude-console-hook.exe`
- Codex hook config: `C:\Users\sahan\.codex\hooks.json`
- IPC root: `%TEMP%\codex-console`
- Plugin log:
  `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\VizhiCodex.log`

The most recent observed rollout state successfully contained:

```json
{
  "transport": "rollout",
  "event": "Stop",
  "payload": {
    "cwd": "C:\\Users\\sahan"
  }
}
```

Keyed files were present for live PIDs, proving the writer permission/replacement fix is active.

## First actions for the next session

Do these in order. Do not reinstall the plugin.

1. Fully restart `LogiPluginService.exe` (or quit/reopen Options+) before final physical testing.
   The observed service process started on 2026-08-31 and has received many development hot
   reloads. Runtime evidence showed the same rollout briefly written to two PID keys, which can
   happen when stale plugin timer instances survive reloads and maintain independent claim maps.
2. In each open Codex session, run `/hooks` and trust the latest seven Vizhi definitions. The
   PowerShell call-operator change altered their hashes, so an older trust grant is insufficient.
3. Exit and start/resume Codex again after trust.
4. Confirm there is no `SessionStart hook (failed)` or `UserPromptSubmit hook (failed)` message.
5. Trigger a safe code-mode command that asks for approval. While its menu is open, confirm the
   session and Yes/No keys show `Allow?`/amber (or the appropriate risk grade), then use only the
   physical Yes or No key and verify `AnswerCommand` appears in `VizhiCodex.log`.
6. Open two Codex terminals in two different folders. Confirm each session key shows its own leaf
   folder and switches to the corresponding terminal.

## Verification commands

Run from PowerShell.

```powershell
$ipc = Join-Path $env:TEMP 'codex-console\sessions'
Get-ChildItem $ipc -File | Sort-Object LastWriteTime -Descending
Get-Content (Join-Path $ipc 'shared.json') -Raw
```

For every live PID-keyed file, verify:

- `payload.cwd` is present;
- the leaf folder is correct;
- hook-origin state has `"transport":"hook"`;
- rollout fallback state has `"transport":"rollout"`.

Check whether Codex actually started the helper:

```powershell
$bin = 'C:\Users\sahan\ravi\claude-console\.worktrees\vizhi-codex-1.6.0\bin\VizhiCodex\Release\bin'
Get-Content (Join-Path $bin 'hook-invoked.log') -Tail 30
```

The breadcrumb now contains real Codex CLI launches including `codex SessionStart`,
`codex UserPromptSubmit`, `codex PreToolUse`, `codex PostToolUse`, and `codex Stop`. Do not expect
`codex PermissionRequest` for code-mode nested `exec_command` approvals on Codex CLI 0.152.0;
those currently use the `rollout-code-mode` recovery edge described above.

If a hook reports failure:

- **No new breadcrumb:** failure is still in Codex's Windows command launcher/config selection.
  Inspect the exact reviewed command in `/hooks` and compare it with both fields in
  `~/.codex/hooks.json`.
- **Breadcrumb exists:** the helper launched. Inspect
  `%TEMP%\codex-console\hook-error.log`, the newest IPC JSON, ACLs on the helper/IPC directory,
  and whether the process exceeded the five-second hook timeout.
- The helper must print `{}` and exit `0`; nonzero hook exits are user-visible in Codex.

## Build and deploy

```powershell
dotnet test tests -c Release --no-restore --filter "FullyQualifiedName~CodexStateBridgeTests|FullyQualifiedName~CodexRolloutBridgeTests|FullyQualifiedName~CodexStateReaderTests|FullyQualifiedName~SessionRegistryTests|FullyQualifiedName~WindowsHookTests"
dotnet build src/Products/VizhiCodex/VizhiCodexPlugin.csproj -c Release --no-restore
```

The product build updates the development `.link` and requests a Vizhi reload from the Logi
service. It also rewrites plugin-owned `~/.codex/hooks.json` when the desired definition changed.

## Test status

- Latest CLI approval/hook/state/risk suite: **139 passed, 0 failed**.
- Narrow CLI approval/state/answer suite: **77 passed, 0 failed**.
- Vizhi product build: **succeeded, 0 warnings, 0 errors**; the development link reloaded version
  1.6.0 from this worktree.
- Full Windows suite: **876 passed, 24 failed**. The failures are outside this change: legacy
  Claude live-status tests that expect macOS embedded-script behavior on Windows, plus two real
  Windows process-query tests blocked by CIM/access behavior in this environment. The affected
  CLI suites above pass independently.

## Known caveats and next code work

1. **Clean service restart is still required for final evidence.** Hot reload is fast but not a
   trustworthy isolation boundary after many iterations.
2. **Resumed rollout correlation is heuristic until hooks work.** A resumed Codex process can be
   much newer than the rollout's original `session_meta` timestamp. Exact hook ancestry avoids
   this ambiguity. Do not loosen matching until reproducing after a clean service restart; loose
   matching previously caused stale/extra sessions.
3. **Do not treat a visible code-mode approval menu as hook proof.** On CLI 0.152.0 its evidence is
   a PID-keyed `transport:rollout-code-mode` envelope; a direct hook approval would still use
   `transport:hook`.
4. **Do not remove rollout fallback.** It supplies project/activity recovery when hooks are
   untrusted, missed, or delayed, but it must never overwrite a live hook approval.
5. The physical Yes/No retest is not complete. The first harmless prompt was approved without an
   `AnswerCommand` log entry, and the second prompt was cancelled; do not mark hardware approval
   control as passed until the log proves the physical action ran.

## Definition of done

- Clean Logi service process, one Vizhi plugin instance.
- Seven hooks trusted with the current hashes.
- No hook-failed messages across start, prompt, approval, tool completion, stop, and session end.
- `hook-invoked.log` proves real Codex hook launches.
- Two simultaneous sessions show distinct correct folder names.
- Session-key presses transition to the corresponding Windows Terminal tab.
- Approval menu lights `Allow?`; Yes/No affect only the targeted pending approval.
- Thinking becomes Complete when the turn stops and returns to Thinking on new transcript growth.
