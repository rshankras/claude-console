# Windows Codex: hook-free Logitech state bridge

> Historical decision for Codex 0.148. Superseded on 2026-09-01: current Codex documents and
> implements Windows command hooks through `commandWindows`. Vizhi now uses hooks for exact
> lifecycle/approval state and retains this rollout bridge as a recovery fallback.

## Decision

Do not use Codex lifecycle hooks as the Windows state transport. On the tested Windows setup,
Codex reports `hook exited with code 1` even for the trusted no-op command `cmd /c exit 0`.
The hook process is therefore not a viable dependency for the Logitech plugin on Windows.

Keep the existing hook bridge on macOS. Implement the Windows bridge using Codex-owned data that
is available outside the failing hook runner.

## Evidence

- Codex CLI tested: `0.148.0-alpha.15`.
- A trusted `UserPromptSubmit` hook set to `cmd /c exit 0` still failed with code 1.
- The real hook executable works when invoked through `codex sandbox` after repairing the local
  sandbox-helper placement and allowing `CodexSandboxUsers` read/execute access to the Logi
  install directory.
- With those prerequisites in place, lifecycle hooks still fail before the hook process is
  created, in both elevated and unelevated Windows sandbox modes.
- Codex rollout JSONL contains `task_started` and `task_complete` events. Codex `notify` invokes
  its target outside this failing hook-sandbox path and carries completion data including thread
  id and cwd.

The detailed experiment log is in [spike-windows-codex-hooks.md](spike-windows-codex-hooks.md).

## Replacement transport

### 1. Rollout reader: the activity source

Add a `CodexRolloutBridge` that observes `%USERPROFILE%\\.codex\\sessions\\**\\rollout-*.jsonl`.
It should tail files defensively, tolerate partial JSON lines and format changes, and write the
existing Codex session IPC envelope atomically.

| Rollout event | Plugin activity |
|---|---|
| `task_started` | `busy` |
| `task_complete` | `done` |
| `turn_aborted` | `done` |

Use the rollout thread id and cwd to identify the session. A short debounce may transition a
completed session from `done` to `ready`; never infer an approval from silence.

`CodexContextReader` already implements safe, bounded-tail JSONL parsing for context usage. The
new bridge should reuse its defensive approach, but must discover the rollout path itself rather
than receiving it from a hook payload.

### 2. Notify bridge: completion confirmation

Add a stable `CodexNotifyBridge.exe` installed with the plugin. Codex invokes the configured
notifier outside the hook sandbox when a turn completes. The bridge must:

1. Parse the completion event.
2. Write the matching `done` IPC state atomically.
3. Chain to the notifier command that was configured before installation.

`notify` is a single global Codex config slot. Its installation must be opt-in, preserve the
previous command and arguments exactly, and offer an uninstall path that restores them only when
the configuration is still ours. Do not overwrite another application's notifier.

### 3. Capabilities and product behavior

On Windows:

- Support ready, busy, done, project and best-effort context through the hook-free bridge.
- Do not install lifecycle hooks or request `/hooks` trust for this feature.
- Set `ApprovalSignal` to false until an approval event is observed and safely parsed from
  rollout data.
- Surface an honest "approval state unavailable on Windows Codex" state rather than guessing.

On macOS, retain `CodexStateBridge` and its lifecycle hooks as the preferred full-fidelity path.

## Implementation seams

- `src/Agents/CodexCli/CodexContextReader.cs`: extract/reuse bounded JSONL tail and parsing
  utilities.
- `src/Agents/CodexCli/CodexCliAdapter.cs`: select hook-free capabilities on Windows and parse
  the shared session envelope.
- `src/Agents/CodexCli/CodexStateBridge.cs`: do not install `hooks.json` on Windows once the
  rollout bridge is enabled; preserve its current macOS behavior.
- `src/Core/SessionRegistry.cs`: continue consuming the normal IPC session files; it should not
  need to understand Codex rollout JSON.

## Acceptance checks

1. Two simultaneous Codex sessions produce separate IPC files and never overwrite one another.
2. `task_started` renders busy within the poll interval; completion renders done then ready.
3. Truncated, malformed or changed rollout lines result in no state update, never a guessed one.
4. Installing the notify bridge preserves and chains the pre-existing notifier; uninstall restores
   only plugin-owned configuration.
5. No hook trust prompt appears on Windows for the hook-free bridge.
6. macOS lifecycle hooks and approval indication remain unchanged.

## Known limitation

Risk-graded approval lights are not implemented by this design unless Codex exposes an approval
event in a reliable, user-readable stream. This is a deliberate capability gap, not a fallback
to the broken hook runner.
