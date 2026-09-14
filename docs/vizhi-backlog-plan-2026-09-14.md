# Vizhi backlog follow-up plan — 14 September 2026

Scope: GitHub issues #89, #88, #90, #37, and #34, plus validation reconciliation for fixes
already present. Issue #94 (test-architecture refactor) is explicitly excluded. Focused tests
using the existing suite are still required. The original plan is retained below. Implementation and validation are recorded in
[vizhi-backlog-results-2026-09-14.md](vizhi-backlog-results-2026-09-14.md); remaining device gates are explicit.

The acceptance-fixes preview and its separate profiles remain a fixed device-test baseline.
Before starting this follow-up, checkpoint the existing changes in their own commit and create a
separate follow-up branch from that checkpoint. Do not mix new fixes into the existing preview
without a new candidate name, source identity, checksum, and updated test record.

## 1. #89 — prevent interleaved Windows input (first implementation priority)

Problem: concurrent helper processes can interleave text and Return on the same console.
Affected path: src/Core/Platform/WindowsPlatformBridge.cs and
 tools/windows/ClaudeConsoleInject/Program.cs, dispatched through ClaudeConsoleTools.

- Reproduce with concurrent text-plus-Enter requests against an isolated Windows console.
- Prefer a per-target cross-process serialization guard in the helper. The guarded operation
  includes attachment/identity checks, the entire text/key sequence, and its final Enter. An
  in-memory lock alone cannot coordinate independently launched helpers or separate products.
- Key the guard by verified process identity including start time, not just a reusable PID.
  Validate named-object scope/access across normal and sandboxed callers before settling the design.
- Bound waiting; on timeout or exited/reused target, report failure instead of injecting late.
  Do not acknowledge approval unless delivery succeeds. Do not hold a global lock across sessions.
- Start with serialization, not broad debouncing: intentional repeated navigation and distinct
  prompts must not disappear. If deduplication remains useful, make it a separately tested policy.

Tests: concurrent distinct prompts remain whole, text and Return remain together, same-target
serialization, independent targets, target exit/reuse, timeout, abandoned lock, failed delivery,
Yes/No behavior, and repeated arrows. Use existing WindowsInjectionTests/WindowsHookContractTests
patterns and a disposable console integration fixture. Pure policy tests supplement the real
Windows concurrency reproduction; they do not replace it.

Done: no scrambled input in the reproduction, no lost intentional input, no wrong-target delivery,
and no meaningful added uncontended latency. Record measured latency and contention behavior.

## 2. #88 — Windows tab identity (investigate alongside preparation for #89)

Problem: safe injection can work while visual tab selection fails after tab renaming.
Affected path: tools/windows/ClaudeConsoleFocus/Program.cs, TabSelection.cs, and
src/Core/Platform/WindowsPlatformBridge.cs.

- Reproduce the exact renamed/frozen-title case from the issue on the owner's Windows machine.
- Inspect supported Windows Terminal/UI Automation identity information and existing console
  process mappings. Establish whether a stable process-to-tab mapping is actually available.
- Test manually renamed tabs, duplicate titles, multiple windows, reopened/reordered tabs,
  elevated sessions, and dead or recycled process identities.
- Implement only an identity strategy backed by a measurable match; do not guess by tab order,
  title similarity, or first matching window.
- Preserve the current mitigation: unresolved focus leaves the prior pin intact and explains why.
  If identity cannot be established, ship the honest limitation and keep #88 open rather than
  treating a successful window raise as successful tab selection.

Tests: extend WindowsTerminalTests, SessionTargetingTests, and the focus helper's existing tab
selection tests. Require the original Windows failure scenario to pass physically before closure.

Done: the intended tab is visibly selected after a manual rename and duplicate-title cases remain
correct; unresolved cases retain safe behavior. Investigation may conclude no supported full fix.

## 3. #90 — consistent readable voice failure feedback

Problem: only Go to Project now holds failures for 8 seconds; Dictate and Draft still use 2.5 seconds.
Affected path: VoiceCommand, VoiceDraftCommand, ProjectVoiceCommand, FailureFace and VoiceFailure.

- Give all three voice actions one shared voice-specific 8-second hold policy.
- Keep live-status enable/disable confirmation timing separate; do not change the global
  FailureFace default merely to fix voice.
- A fresh recording/startup supersedes an old error immediately. Keep errors scoped to the
  originating action, including cross-key stop/cancel cases.
- Verify No speech, No target, No match, Model loading, helper failure, and failed delivery.

Tests: reuse FailureFaceTests, VoiceFailureTests, VoiceCaptureStateTests and VoiceDeliveryTests.
Add timer-policy and reset tests without long sleeps. Physical check on both platforms: glance
back from the terminal and read the failure; retry immediately without a stale error covering it.

Done: equivalent failure visibility across voice keys with unchanged capture/routing behavior.

## 4. #37 — independent prompt configuration with safe migration

Decision proposed for implementation: each product owns future prompt edits; existing settings
remain usable through a one-time copy. Claude retains its current config path. Vizhi gets a
product-owned config file (proposed ~/.codex/vizhi/prompts.json; confirm naming against other
product configuration before implementation).

- If Vizhi's file exists, it wins and is never overwritten by migration.
- Otherwise copy the valid existing shared prompt list, preserving custom IDs, labels, text,
  icons, order, and submit flags. Do not rewrite, move, or delete Claude's original file.
- If no source exists, seed Vizhi defaults. If a source is malformed/unreadable, preserve it,
  explain the problem, and use documented fallback behavior without claiming migration succeeded.
- Use an atomic create/update so concurrent startup cannot clobber a new user edit; define
  behavior for interrupted migrations and permission failures.
- Retain the existing product-display Code Audit distinction; changing presentation must not
  rewrite user content. Existing users who intentionally share prompts need clear migration notes.

Tests: extend PromptSeedTests and PromptNullParameterTests with fresh installs, customized lists,
submit=false, existing destination precedence, malformed source, denied writes, interrupted and
repeated migration, sequential Claude/Vizhi loading, and concurrent creation. Verify source bytes
are unchanged and edits to either product no longer affect the other after migration.

Done: independent customization on both products with no lost settings and repeatable upgrades.

## 5. #34 — explicit profile update preserving customization

Problem: updated downloadable profiles do not establish that existing imports receive changes.
The historical issue mentions registration healing; these products are now universal, so do not
reintroduce application registration solely to implement the old proposed solution.

- Inventory what the supported Options+/Logi import/export workflow exposes for identifying an
  existing profile, backing it up, importing an update, and handling GUID collisions. Confirm actual
  host behavior before promising automatic merge or replacement.
- Add a profile revision and migration record distinct from plugin/package version. The record
  describes changed actions, slots, and stable action IDs.
- Prepare a reviewable old/new layout and an explicit choice between adopting the new default
  and keeping the customized layout. Plugin upgrade alone must not silently rearrange keys.
- Back up the current profile using supported export facilities where available. Prefer a
  supported side-by-side import if safe replacement/merge cannot be verified. Never write directly
  into undocumented Options+ storage or assume importing the same GUID refreshes it.
- For preserved custom layouts, give a precise list of moved/new actions to apply manually.
  Automate merging only if supported APIs expose enough identity and customization information.

Tests: existing KeypadLayoutTests, IconResourceTests and package/profile verification; revision
metadata checks and migration fixtures for untouched defaults, moved keys, custom bindings,
missing actions, repeat import, platform host identity, and backup preservation. Verify actual
import/update outcomes in Options+ on macOS and Windows; rollback must restore the prior layout.

Done: existing users have a verified, documented update path that preserves their own work.
A supported guided update is acceptable; silent automatic migration is not a requirement.

## 6. Reconcile implemented issues without overstating closure

Revisit #69 hook-trust guidance, #66 signature gates, #70 product diagnostics, and #78/#79/#91
voice startup/readiness against current code, issue comments, package checks, and platform evidence.
For each, record: exact acceptance criterion, implementation location, test, candidate checksum,
remaining physical check, and whether it is implemented/partial/verified/deferred. Include #35
Windows approvals where relevant. Do not close merely because code exists or old Claude Windows
QA passed. Updating GitHub comments/issue state is a separate external action; this plan records
results locally first.

## Delivery sequence and validation gates

1. Preserve the current acceptance-fixes checkpoint and create the follow-up branch.
2. Implement #89; investigate #88 with Windows evidence. #90 can follow independently.
3. Implement #37 migration, then the supported #34 profile-update workflow.
4. Keep each issue's code, tests and documentation in its own reviewable commit. Build a new
   candidate after a coherent batch; record exactly which issue criteria it addresses.
5. Run full existing C# suite and affected shell/hook suites, both product Release builds with
   SkipPluginLink=true, package verification and profile checks. Reuse historical Claude QA while
   rerunning changed approval/injection/voice/configuration workflows.
6. Measure idle CPU/poll behavior, uncontended injection latency, and contention handling where
   changed. Add no polling for prompt/profile migration; perform migration at load/update time.
7. Owner executes Windows strict/device checks; repeat affected Mac checks. Fresh permissions,
   runtime coexistence, upgrade/rollback and customized settings must have explicit evidence.
8. Update the issue matrix and release notes. Merge/release only when required criteria pass or
   limitations are explicitly deferred. #94 remains excluded throughout.

Implementation started after plan approval. See the linked result record for commits, candidate identity and remaining checks.
