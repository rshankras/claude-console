# Vizhi follow-up plan and coverage gaps — 2026-09-14

Scope: finish Vizhi integration PR #100 while preserving Claude Console's tested shared behavior.
Sources: `vizhi-acceptance-2026-09-14.md`, `vizhi-shared-compatibility-review-2026-09-14.md`, existing
Claude QA notes and test suite. This plan does not implement fixes or authorize production release.
Issue #94's broad test-architecture refactor remains deferred.

## Work items, existing coverage, and missing evidence

| Priority / item | Existing evidence or reusable tests | Work and acceptance criterion |
| --- | --- | --- |
| P1 Shared voice-helper ownership | VoiceHelperInstallTests, VoiceRuntimeInstallTests, VoiceCaptureStateTests; single-product voice passed | Prevent incompatible helper replacement across installed Claude/Vizhi versions while retaining shared model reuse. Test alternating products and failed installation recovery; repeat real mic/TCC smoke. |
| P1 Claude approval compatibility | AnswerKeyTests, PendingApprovalTests, SessionRegistryTests, SessionTargetingTests; new VizhiIntegrationTests cover Codex | Add Claude behavioral counterparts for Yes/No, failed delivery, clear+refresh, next request, and disappearing pending file. Keep no-pending no-op and pinned targeting. |
| P1 Startup/fork identity | CodexRolloutBridgeTests, IdleSessionStateTests, SessionRegistryTests; child creation proven | Reproduce initial labels and stale parent state; update from available trustworthy metadata without a first prompt. Test reused tty, missing metadata, and fork identity changes; never invent a project/state. |
| P1 Approval indicator visibility | Risk/answer policies covered; owner saw Allow but amber only while pressing | Reproduce press/release vs pending repaint behavior. Badge must persist while pending and clear only on successful answer/new event. Validate physical device rendering, including Claude. |
| P2 Plan repeat press | AgentVocabularyTests cover /plan mapping; entry manually passed | Verify supported Codex exit mechanism, then implement toggle if reliable. Handle external mode changes; do not infer mode solely from previous keypad press. Test both transitions and unknown state. |
| P2 Profile ergonomics | KeypadLayoutTests, IconResourceTests, package profile checks | Apply page-1 order below and finalize picker/navigation layout. Update both Vizhi platform profiles, generator, previews, and binding assertions together. Preserve Claude profile bindings. |
| P2 Project lookup setup/feedback | ProjectDiscoveryTests already cover configured override, inferred roots, worktrees, arbitrary user paths; VoiceFailureTests cover no-match | Preserve shared algorithm. Address local demo-only override separately without shipping personal paths. Explain effective search mode and improve notice visibility; test arbitrary home paths and existing customization preservation. Retest AlertWala and nonexistent name. |
| P2 Agent command-center targeting | SessionTargetingTests cover pin precedence and release; unpin workaround passed | Make target state/unpin guidance clear without silently breaking session pins. Test a separate command-center terminal while a session is pinned, then unpinned. Update Agent description to match observed workflow. |
| P2 SessionEnd timeout | CodexStateBridgeTests, WindowsHookTests; CLI clamp warning reproduced | Use appropriate event-specific timeout and update config assertions; verify no clamp on new session exit and no needless hook rewrite/retrust on unchanged config. |
| P2 Shared prompt migration | PromptSeedTests protect edited files and migrate exact generated seeds | Explicitly choose shared versus product-specific labels. Add sequential Claude/Vizhi load coverage, preserving custom text/icons/submit choices and stable action IDs. |
| P2 Claude Context guidance | LiveStatusActionsTests cover gate; CodexContextTests cover context reader | Restore Claude setup/disable description; keep Codex details/compact help separate. Verify short press, long press, release suppression in both products. |
| P3 Documentation accuracy | Source review | Remove obsolete Windows approval comments; clarify Fork stays in the same terminal and Context percentage semantics. |

P1 marks release-confidence work, not a claim that every investigation is a reproduced regression.

## Proposed profile layout

Page 1 (requested consistency with Claude Console):

| Left | Middle | Right |
| --- | --- | --- |
| Session 1 | Session 2 | Session 3 |
| Esc | No | Yes |
| Screenshot | Dictate | Draft |

Page 2 proposal: Model / Plan / Skills; Review / Context / Compact; Up / Enter / Down.
Page 4 proposal: Go to Project / New Tab / New Codex; Prev Tab / Next Tab / Exit;
Agent / Fork / Resume. This still leaves Agent and Resume away from navigation: finalize this
tradeoff in a concrete profile preview before treating the layout as approved. Page 3 prompts and
page 5 Git can retain current positions. No profile has been changed yet.

## Outstanding acceptance (not all are product defects)

- Resume, Fork, Model, Skills navigation, screenshots, Dictate/Draft, Context/Compact, terminal
  navigation, approvals, all prompt delivery, and all Git delivery have existing Mac evidence.
- Skills enable/disable mutation, context long-press compact, interrupted-turn recovery timing,
  and physical voice cancel/restart/cross-key cancellation need explicit results.
- Prompt tasks Document/Optimize/Refactor/Fix Bug/Code Audit/Write Tests/Security need individual
  completion if task-output coverage is required; alpha contains only README. Use a disposable
  fixture with small real source files, a known bug, and tests for meaningful exercise.
- Git actual commit, successful push and PR need a disposable repository/remote. Keep actual
  publishing separate from verified command delivery; never use active project remotes as fixtures.
- Agent new task lifecycle (submit → working → complete) remains untested.
- Options+/plugin restart, profile auto-switching, persistence, fresh permissions/model download,
  upgrade/uninstall/reinstall, and two-product coexistence remain pending.
- Context screenshot's 100% left versus raw-ratio approximately 97% should be checked against
  Codex's actual percentage semantics before promising identical keypad and terminal values.
- Windows strict runner and physical checklist remain assigned to the owner on Windows. Include
  hook trust, approvals, Windows Terminal targeting (including known renamed-tab limitation), voice
  cancellation, screenshots, profiles, startup/exit, and restart.
- No full performance benchmark has been run. Preserve bounded discovery and poll cadence; measure
  idle/repaint activity and action latency when modifying session refresh or discovery. The observed
  291 ms load is baseline evidence only, not a performance guarantee.

## Execution order and release gates

1. Reproduce P1 issues and add focused failing regression tests for actual defects. Reuse the
   established suite; do not rewrite it or introduce broad production abstractions for this task.
2. Implement compatible shared fixes in small reviewable changes, then Codex-specific behavior
   and profile changes. Keep machine-local setup edits distinct from shipped defaults.
3. Run full C# suite, relevant shell/hook suites, both Release builds with SkipPluginLink=true,
   and package/profile verification. Repeat targeted Claude physical workflows affected by changes.
4. Build a clearly versioned candidate with checksum and tested-source identity; run affected Mac
   acceptance. Prepare the Windows checklist and record the owner's results against that candidate.
5. Close or explicitly defer every unresolved item; only then merge and package for final release.

Latest review baseline: 1,092 C# passed, 11 skipped, 0 failed; Claude Release build 0 warnings/errors.
Those checks predate the planned fixes and must be rerun after implementation. Historical Claude
Windows QA is reusable context, not a Windows pass for the new Vizhi candidate.

## Implementation checkpoint — acceptance-fixes preview

Implemented in the working tree (not committed or installed):

- Both Vizhi profiles and first-page previews use Sessions; Esc/No/Yes; Screenshot/Dictate/Draft.
  Page 2 now places Up/Enter/Down alongside Model/Plan/Skills and Review/Context/Compact.
  Agent/Fork/Resume moved to page 4; their menus still use page-2 navigation.
- macOS discovers Codex cwd using libproc during the existing process scan, allowing project
  labels before the first hook. Authoritative hook directories win. Codex provisional keys say
  Ready. A recycled provisional tty loses its previous project name. Claude's original provisional
  wording remains unchanged (the existing SetupFaceTests caught and prevented a regression here).
- Ended Codex parent conversations no longer retain their context/session id during Fork/Resume.
  A live process retains its project and becomes provisional until new authoritative hook data.
  This clears misleading stale state; it does NOT infer a new child id before Codex reports it.
- Yes/No faces receive change notifications on pin/frontmost changes, even without a session-file
  write. This addresses a source-level repaint gap consistent with the reported amber symptom;
  physical indicator timing must still be retested.
- Claude approval transport is explicit rather than inferred from whether a pending file exists.
  Added executable clear/refresh/failed-delivery/new-request/disappearing-file regression cases.
- Plan sends native Shift+Tab rather than repeated /plan. Tests verify two targeted key deliveries
  without text submission; actual enter/leave behavior needs device retesting, especially with
  custom CLI keymaps. Current official /plan docs only establish entry, not a toggle.
- SessionEnd requests 3 seconds; other hooks retain 5. Unchanged install idempotence tests pass.
- Project no-match face lasts 8 seconds and explains that configured roots override discovery.
  Shared discovery algorithm and the user's local demo-only roots file are unchanged. No personal
  path was added to shipped code. Local search configuration still needs user-specific resolution.
- Vizhi executable voice runtime uses ~/.codex/vizhi-runtime; Claude's runtime and the shared
  model location are unchanged. This prevents cross-product executable replacement. New-path
  microphone consent and actual alternating-product capture need hardware verification.
- Context help remains product-specific. Code Audit is now a Vizhi display label; loading Vizhi
  no longer rewrites the previous unedited shared Review seed. Existing customized files survive.
- Agent help explains releasing a session pin before navigating a separate command-center tab.

Validation of this checkpoint:

- C# suite: 1,108 passed, 0 failed, 11 skipped (16 more passing cases than the review baseline).
- Both Release builds: 0 warnings/errors with SkipPluginLink=true. The test project reports its
  pre-existing xUnit1031 warning in BridgeNoticeTests; production builds have no warnings.
- Claude bridge scripts: 54 passed; Codex hook scripts: 27 passed.
- Preview package accepted by verifier; all three deliberate missing/duplicate-helper mutations
  rejected by tests/test-package-contract.py. An initial invocation omitted the required package
  argument, then was corrected and passed against the new preview.
- First-page profile contact sheet inspected. Claude profile files were not modified.
- libproc cwd micro-check: about 0.002 ms/call over 1,000 local calls; no extra poll timer or external
  process. This is not an end-to-end performance or Windows benchmark.

Preview: artifacts/VizhiCodex_1.6.1-acceptance-fixes-preview.lplug4 (17,387,001 bytes).
SHA-256: e1bfc3c79c3dd5e6f7394522ff43805a2c7d48a9dbceef7b48ab96a8d339a3a6.
Built from updated managed binaries plus the prior verified signed helper payloads. It is a
working-tree preview, not a release. Profiles are separate downloads and must also be reimported.
Source/file hashes are recorded in artifacts/vizhi-acceptance-fixes-manifest.json.

Before sign-off: retest the changed keypad workflows on Mac, Windows strict/device checks,
voice permission/coexistence, and the remaining acceptance items above. No physical pass from
before these changes is being represented as a pass for this preview.

## Additional GitHub backlog

See `vizhi-backlog-plan-2026-09-14.md` for the separate #89/#88/#90/#37/#34 plan.
It preserves this preview as the device-test baseline and explicitly excludes #94.
