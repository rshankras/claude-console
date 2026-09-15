# Codex detached-hook routing fix — 14 September 2026

## Reproduction

The owner showed seven installed/active hooks in the stage CLI's `/hooks` view. During a live
“Check the kit” turn, stage emitted UserPromptSubmit, PreToolUse, PostToolUse and Stop, all into
`/tmp/codex-console/sessions/shared.json`. The stage key was bound to `ttys005`. That mismatch
prevented both live activity and approval state from reaching the selected keypad session.
The observation is saved locally in `artifacts/stage-hook-observation-20260914.json`.

The hook launcher is byte-identical in the earlier integration and backlog packages (SHA-256
`ab0f1f4dc616319f90e0bd564fd6afa0c464df8da449108dd52478d0b629d37e`). This is a pre-existing
assumption exposed by detached hook execution, not a new change in that script. Why the earlier
alpha/beta sessions executed differently has not been independently established. Both earlier
and current interactive sessions reported CLI 0.154.0; a version change is not established.

## Fix and Claude Console comparison

Claude Console already climbs up to six process generations in both `scripts/activity-hook.sh`
and `scripts/statusline-handler.sh`. Codex previously queried only its hook process's controlling
TTY. A detached child has none even when the parent CLI owns a terminal.

`scripts/codex-hook.sh` now uses the same bounded parent lookup. It stops at the first real TTY,
PID 1, unavailable/invalid parent information or a self-parent, with a six-generation cap.
Terminal-less processes retain the existing shared fallback. It never guesses by project name,
frontmost tab or registry order. The envelope/payload format, atomic writes and command paths
stay stable. No new background polling is added.

The old test suite exercised attached PTYs and terminal-less fallback, but not a detached hook
with an attached ancestor. The new real-process fixture reproduces that missing condition with
two separate PTY-owning parents and both direct and intermediate detached wrappers. Before the
fix, attached tests passed and both detached variants failed with shared.json. After the fix all
variants pass, preserving each session's PermissionRequest payload under its own TTY.

The existing global “state bridge active” message can still be satisfied by a shared hook event.
It is not evidence that every terminal is connected; this run uses terminal-specific files as the
acceptance criterion. This patch does not redesign that global status indicator.

## Validation

- Full suite: 1,127 C# passed, 11 Windows-only skipped; 54 bridge script cases, 27 Codex hook
  cases, 7 cleanup cases, 6 profile cases and the cross-process input-lock probe passed.
- New detached-hook suite: 2 tests, with 3 real two-terminal execution variants and 6 failed,
  invalid or cyclic parent variants. All passed outside the sandbox with process inspection.
- Vizhi Release build: zero warnings/errors, `SkipPluginLink=true`.
- Corrected preview passes package contracts, all three network links and strict extracted
  macOS voice-helper/whisper signatures. Sandboxed verification first failed due to DNS and
  signing restrictions; the authorized unrestricted verification passed.

## Deployment

Applied the corrected installed launcher atomically, preserving its permissions. Previous script
backed up to `artifacts/codex-hook-before-routing-fix.sh`. No trust entry or hooks.json was edited,
no Terminal UI was controlled, and no demo repository was reset.

Installed script SHA-256:
`d295a3338dbd4ad4c9d8c8db6348acd8f999b67f951491487249e64fe382690b`.

A persistent package update is in Downloads:
`VizhiCodex_1.6.1-hook-routing-fix-preview.lplug4` (17,389,243 bytes).
SHA-256: `ebbaafeaef78a2d1227027807bd35dfa8ad750af0f35e97dd5ff6169f7d5543e`.
The package contains the rebuilt DLL/deps/PDB plus the unchanged previously verified payload.
Source is `0f87930` plus this routing fix; provenance is in `artifacts/vizhi-hook-routing-build.json`.
The old installed plugin DLL is unchanged until that package is installed; reinstalling an older
package can restore the old launcher. No layout reimport is needed for this fix.

Live post-fix observations are recorded in `artifacts/stage-hook-after-routing-fix-20260914.json`.
Physical Thinking/Complete and actual Yes/No resolution need owner confirmation; a hook file
alone does not prove that an approval was answered on the device.

## Live result and remaining server-hosted-session limitation

The first post-fix stage test still wrote shared.json. Read-only process/log inspection then
established why: the stage thread had moved from its original CLI process to PID 30290,
`codex app-server --listen unix://`, parent PID 1, TTY `??`. Its visible terminal process was
`codex resume 01a0a07d-042c-7461-8fd8-bc60a85afb54` on ttys005. The engine and terminal client
are separate process trees; neither the original lookup nor Claude's parent-chain method can
recover a TTY from that daemon. This is distinct from a detached child beneath a terminal CLI.

A fresh FAQ thread (`01a0a0c4-9a88-7471-9dd6-fdc46fb7a618`) runs directly as PID 60017 on
ttys004, using read-only/on-request settings. Its actual PermissionRequest event appeared in
ttys004.json. The plugin logged successful keypad approval at 22:06:45.579 and rejection at
22:06:53.776. This verifies live approval delivery for that direct terminal session, not for
the old server-hosted stage session. Fresh FAQ alone is not proof that the parent-chain change
was necessary for it: the deterministic detached-process fixture establishes that regression.

To restore this demo's stage workflow, start a fresh direct CLI session in the stage folder;
resuming the old server-hosted thread may reconnect to the same daemon. Do not reset the demo
repositories or approve destructive operations merely to recover the transport.

Full support for server-hosted threads remains unresolved. It requires an explicit, verified
mapping between thread ID and terminal client, plus per-thread event storage so concurrent
shared events cannot overwrite one another. Project-directory matching or routing to the
currently pinned key is unsafe and was not added. Do not describe this patch as fixing that
separate topology. Thinking/Complete and stage approvals require a fresh-session/device retest.
