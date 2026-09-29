# Windows helper health — implementation and validation

Branch: `fix/windows-helper-health`, based on `origin/main` (`f6fcf2b`).

The Windows hook can disappear or fail while historical IPC data still makes the plugin look
healthy. This change makes that failure visible, prevents approval actions using invalid state,
and recovers when the matching helper/session delivers fresh information.

## Implementation plan

1. Add a shared, product/path-scoped hook health monitor. Check the expected executable and
   durable failure/success evidence at load, on polling and on affected key presses. Absence of
   recent activity alone is never a failure.
2. Have the existing Claude PowerShell launcher report missing/failed execution, and have the
   helper record success only after completing keyed observation writes. Keep Codex's installed
   command unchanged for this release stage.
3. Overlay Blocked on affected live/approval keys, invalidate cached Active, and publish an SDK
   Warning without repeatedly posting the same notice. Preserve other subsystem notices.
4. Require matching-session delivery and invocation metadata in the actual approval source newer
   than the failure/binary watermark. Recheck the exact parsed source at press time. Recovery in
   another session, a status-line update, or an older hook finishing late cannot authorize stale data.
   Preserve Codex code-mode approvals only with fresh source-event and scan timestamps, confirmed
   through a complete transcript read.
5. Add behavioral regression tests for loss after Active, startup absence, blocked execution,
   recovery, multiple sessions, warning lifecycle and normal approval gating. Build both products
   without altering the live Logi installation.

## Implementation and validation result — 2026-09-26

The five implementation steps above are complete in this worktree. Codex uses independent fresh
hook receipts to retain execution proof when its rollout reader replaces a state envelope, while
still invalidating missing helpers and requiring new proof after bridge installation changes.

- Complete repository suite passed: C# plus shell writers, uninstall concurrency, detached hook
  routing, profile updates and cross-process input locks. The final C# rerun passed **1,279 tests**;
  **24 Windows-only tests skipped** on macOS.
- Claude Console and Vizhi for Codex Release builds passed with zero warnings/errors using
  `-p:SkipPluginLink=true`; no live plugin link was installed or service reloaded.
- The trimmed, self-contained Windows helper cross-published successfully for `win-x64` and
  `win-arm64`. Its five existing CA1416 platform-analysis warnings remain. Cross-publishing does
  not execute the helper on Windows, and these artifacts are unsigned.
- The suite confirmed the live Claude settings, opt-out marker and IPC root were preserved.
  `git diff --check` passed. No dependency versions changed; NuGet vulnerability auditing was
  disabled for restore during local verification because the advisory feed was unavailable.

Actual Windows execution, Git Bash/PowerShell compatibility, Logitech CrowdStrike acceptance,
certificate signing and the signed-package retest remain release gates. The unchanged launcher
choice and exact device checklist are documented in [the protocol notes](windows-hook-health-protocol.md).

## Review follow-up — 2026-09-26

The review on #120 found that `AwaitingFresh` (no receipts yet) was rendered as Blocked with an
Options+ warning — every new day, every fresh tab, and whenever no session was open — and that one
failure marker of any kind blocked every session. Fixed in the same branch:

- Unavailable, Blocked and the warning now require an observed **helper** failure newer than every
  success ever seen (a `last-success.json` watermark survives pruning) and newer than the installed
  file. AwaitingFresh keeps the neutral faces; per-session gating still refuses quietly.
- Failure records carry a `scope`. Exe-reported and input-timeout failures are `delivery` scope:
  logged once, receipt withheld, no barrier.
- The launcher carries the health directory as a literal and no cleanup code (the plugin trims on
  every read): 3,874 characters encoded, down from ~4,900.

## Release boundaries

- The helper remains an out-of-process program using the existing file IPC architecture.
- A Bash/direct-execution launcher is not shipped without Windows compatibility evidence.
  Preserve UTF-8 input, bounded execution, exit behavior and chained user status-line output.
- Signing (#110), Codex launcher marker changes and final signed-package acceptance follow this
  work. Pinning latency (#112) is a separate change; no process probes are added to key handlers.
- No automated check on macOS establishes Windows runtime compatibility or Falcon acceptance.
  Keep #111 open until Logitech records the protected-machine Yes/No result.

## Device acceptance

Use an identified test build and preserve the initial logs. With normal antivirus protection
enabled, establish fresh hooks and a pinned session, temporarily rename the hook EXE, then press
a live key. Verify Blocked, one SDK warning and no Yes/No injection. Repeat with absence at load.
Restore the EXE: old files must not restore health. A new successful hook should recover without
a restart. Verify Yes on one visible prompt and No on a separate fresh prompt. Repeat for both
products, including two sessions where only one recovers; the other must remain unverified.

Renaming tests absence only. Test present-but-denied execution and marker-write failure separately.
An endpoint product that prevents the shell itself from starting cannot produce a launcher marker;
the absence of a marker is not proof of success. Existing evidence cannot diagnose an unobserved
block during an idle session. Capture actual antivirus detections and the process tree for QA.

After signing, Logitech must repeat the checks on the exact final packages with CrowdStrike
enabled and record policy/exceptions, timestamps and package hashes. A local Defender/McAfee pass
does not establish Falcon acceptance.
