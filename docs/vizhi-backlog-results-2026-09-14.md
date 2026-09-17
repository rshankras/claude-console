# Vizhi backlog implementation and validation — 14 September 2026

Implementation branch: `fix/vizhi-backlog-followup`, based on acceptance-fix checkpoint
`fa01c07`. Runtime/profile implementation ends at `7c81d22`. The layout, session labels,
Plan toggle, project discovery/feedback, approval repaint and runtime separation from the earlier
acceptance work are included in this branch. Issue #94/test-architecture redesign is excluded.

## Implemented changes

| Issue | Change | Validation and remaining gate |
| --- | --- | --- |
| #89 | `tools/windows/ClaudeConsoleInject/SessionInputLock.cs` serializes the whole delivery by PID/start time, with a bounded wait and identity recheck. | Six C# lock cases; 12 actual competing helper processes delivered whole text/Enter pairs. Real Windows console test added, skipped here. Windows device/sandbox and contention checks remain. |
| #90 | Dictate, Draft and Project use `VoiceFailure.HoldMs = 8000`; each press clears that key’s old failure. Global `FailureFace` default remains 2500 ms. | New hold/reset test plus existing failure, routing and capture-state tests pass. Readability/retry check on each physical key remains. |
| #37 | `ProductPromptConfig` gives Vizhi `~/.codex/vizhi/prompts.json`, with atomic copy-once migration. Existing destination wins; custom data/order/encoding survives; invalid source is untouched and retryable. | Twelve migration cases pass, including UTF-16/BOM, submit=false, independent edits, concurrent creation and blocked paths. Existing prompt-seed tests pass. Upgrade check with actual user settings remains. |
| #34 | `tools/profile-update.py` backs up an exported profile, reports all differences, and optionally creates a distinct-identity default candidate. Added revision record and corrected stale Claude archive metadata. | Six Python cases pass for custom macros/keys, identity, backups, product/host mismatch, repeat preparation and revision hashes. Actual Options+ import/selection/rollback remains unverified. |
| #88 | Existing unresolved-focus guard retained; no new title/order guessing added. | Re-read issue and Windows comments: manually renamed/frozen labels defeat both nonce and window-title probes. Keep open. Clear the manual tab name as the recorded workaround. Original Windows scenario is still required for a full fix. |

The lock is serialization, not deduplication or a FIFO queue. A request that cannot acquire it
within 1.5 seconds fails visibly; it is not delivered late. Abandoned ownership detection is
best-effort OS mutex behavior, not persistent recovery after a crash. See
[Windows input validation](windows-input-serialization.md).

The profile candidate adopts new defaults; it does not merge user customizations. The exact
export remains in `backup.lp5`, and default keep-custom mode creates no replacement. See
[profile update instructions](profile-updates.md). A fixture-based candidate was prepared under
`artifacts/vizhi-profile-update-example-20260914`; it is not an export of the user's live layout.

## Reconciliation of earlier fixes

Read the current GitHub issue bodies/comments locally on 14 September; no issue state or comment
was changed. Historical Claude Windows QA is supporting evidence, not sign-off for this Vizhi
candidate.

| Issue | Current evidence | Disposition / remaining check |
| --- | --- | --- |
| #69 | `AgentBridgeNotice`, `VizhiCodexPlugin.ReportBridgeProblem`, session/action setup faces; AgentBridgeStatusTests and CodexStateBridgeTests pass. | Guidance implemented. Verify fresh untrusted hooks, visible Run /hooks guidance, trust and recovery on this candidate. |
| #66 | `sign-and-notarize.sh`, `pack-release.sh` and `verify-package.sh` hard-fail verification. This candidate’s extracted Mac helper and whisper signatures pass. Package mutation tests reject bad contracts. | Packaging gates verified locally; candidate installation and final release gate still apply. The earlier issue’s suspected bad signature was corrected in its comments. |
| #70 | `src/Core/Helpers/PluginLog.cs` prefixes messages with the product; AgentBridgeStatusTests verify both identities. Stable action namespace retained. | Product log identity implemented. SDK-owned standalone stack-frame namespace remains shared; changing it would risk profile bindings. Verify actual host log context. |
| #78 | `VoiceCaptureState` owns the guarded entry; Windows startup tracks the child process until ready/cancelled and kills an unsuccessful startup. Capture/routing tests pass. | Partial: the private low-level launch method has no independent entry guard, and there is no persistent PID owner for the whole recording after readiness. Do not claim the residual issue fully closed. |
| #79 | Starting/Cancelling states separate startup from Recording; failed startup need not flash Listening. VoiceCaptureStateTests pass. | Indicator behavior implemented. Per-key post-press `recording=False` logs can still be snapshots rather than paired lifecycle events. Recheck actual failure/retry and logs. |
| #91 | Windows helper readiness acknowledgement, 30-second startup deadline and cancellation cleanup are present. Issue comments report successful Claude Windows device checks on c417fe5. | Local code/tests pass; fresh-machine Vizhi install, first microphone readiness and Project voice need a Windows pass. No ARM64 device claim. |
| #35 | Codex Windows command override and approval readers/dispatch are present; generated-hook and injection/approval tests pass. | Actual installed Codex Windows hook delivery and idle/pending/answered key lighting remain unverified. Mac evidence does not close the Windows issue. |

## Automated results

- C#: **1,127 passed, 11 skipped, 0 failed** (1,138 total). Nineteen new C# cases versus
  the acceptance checkpoint. Existing xUnit1031 warning in BridgeNoticeTests remains.
- Bridge shell tests: **54 passed**; concurrent cleanup tests: **7 passed**; Codex hook tests:
  **27 passed**. Live Claude settings, opt-out marker and IPC canary survived the suite.
- Profile updater: **6 passed**. Cross-process lock probe: **12 successful whole deliveries**.
  Real Windows input script reports **SKIP on macOS**, not a pass.
- Both product Release builds: **0 warnings, 0 errors**, with `SkipPluginLink=true`.
- Windows x64 toolkit publish succeeds. Existing built-in COM trimming warning IL2026 remains;
  this build result is not Windows runtime validation. Isolated Windows test-console fixture
  compiles with zero warnings/errors.
- Package verification: passes product/version/payload contracts, three support links,
  strict signatures of extracted ClaudeVoiceHelper.app and whisper-cli.
- Package contract tests: **4 passed** (original accepted; missing toolkit, missing hook and redundant runtime rejected).

Uncontended named-mutex acquire/release averaged **0.589 ms on this Mac over 10,000 iterations**.
This does not measure Windows helper launch or injection latency. No new background polling was
added; prompt migration runs when prompts load, profile preparation is explicit, and the existing
350 ms text-to-Return delay is unchanged. Live idle CPU and Windows latency comparison still
require the installed candidate/device pass.

## Candidate identity

- File: `artifacts/VizhiCodex_1.6.1-backlog-7c81d22-preview.lplug4`
- Size: **17,389,144 bytes**
- SHA-256: `b28910f3052ab2b50204f0a2d73052e2ecda576a209fdbaecf49d626569362c0`
- Source: `7c81d22f94561d7179d6ef46d58b4a1e56c9723a`
- DLL SHA-256: `a3da77b5870164758c0011766f4baa2d9ff9f446da0c0e54a602d255d2e03d65`
- Windows toolkit SHA-256: `3d95837994d9940dc80df72be2747ee24d365a012993c68f5e1f7e7f8055cefe`

The preview was assembled from the preserved acceptance preview's signed voice payload, replacing
the rebuilt Vizhi DLL/deps/PDB and Windows toolkit. It is not a fresh full release-pipeline build.
Build provenance is also in `artifacts/vizhi-backlog-preview-build.json`. Profiles remain separate
in `profiles/`; revision `2026-09-14.1` is not the plugin version.

The previous preview is unchanged (SHA-256
`e1bfc3c79c3dd5e6f7394522ff43805a2c7d48a9dbceef7b48ab96a8d339a3a6`).
No candidate has been installed, pushed, merged or released during this follow-up.

## Next device pass

1. macOS: verify updated page 1/page 2 layout, startup alpha/beta labels, Plan toggle twice,
   project search roots/setup guidance, and immediate Yes/No repaint between sessions.
2. All three voice keys: provoke a readable error, retry immediately, and confirm an old error
   does not return. Repeat across products without replacing each other's voice runtime.
3. Export a customized profile and perform keep-custom/adopt-defaults and rollback checks.
   Confirm the candidate appears separately; do not delete the original to make import work.
4. On Windows run `python tests/scripts/test-windows-input.py` against the candidate’s extracted
   toolkit, then the physical rapid-press, navigation and approval checks in the linked run sheet.
5. Windows fresh install: wait for Listening before speaking, test cancellation while Starting,
   Dictate/Draft/Project, hook trust, approval lighting and both session targets. Retest renamed
   tabs as a known limitation, keeping the previous pin when focus is unresolved.

Test architecture #94 remains deferred. No claim of full hardware coverage or release readiness
is made by these automated results.
