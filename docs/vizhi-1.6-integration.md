# Vizhi for Codex 1.6.1 integration

Integration branch: `integrate/vizhi-1.6-main`. Prepared 13 September 2026.
This document supersedes the integration/release status in the older 1.6.0 handoff;
that handoff remains the record of the earlier Windows experiments.

## Inputs preserved

- Local `d9022d7`: hook setup/trust guidance, product logging, signing checks and tests.
- Remote Vizhi `ffddb7c`: voice setup work and the existing version bump to 1.6.1.
- Main `26c65a6`: Claude Console 2.2.2 fixes, shared Windows toolkit and documentation.

The original Vizhi worktree was clean and was left on its existing branch. Both divergent
Vizhi histories were merged on a new integration branch before merging main. No installed
plugin, live settings, or device configuration was changed during this integration.

## Conflict decisions

- Keep main's complete voice readiness/cancellation lifecycle. Remove the older Vizhi-only
  setup flag and nested worker: main already performs installation and launch off the key
  thread, keeps Starting visible until readiness, and permits cancellation during startup.
- Keep Codex hooks, rollout recovery, approval envelopes, trust guidance, controls, identity
  icons and platform-specific downloadable layouts. Claude settings wiring stays separate.
- Apply main's activity-word normalization inside Vizhi's generalized activity reader; retain
  Codex transcript activity/interrupt handling and failed-focus routing protection.
- Use current public vizhi.dev package/support URLs. Keep Vizhi's version at 1.6.1.
- Ship only hook + shared toolkit. Remove tracked obsolete helper binaries and preserve main's
  Win32 screenshot implementation without the Windows Desktop Runtime or single-file compression.
- Retain both the central package verifier and Vizhi's stricter Gatekeeper/stapled-ticket checks
  on the extracted helper. A failed extracted-helper check also renames the package `.rejected`.

## Validation completed here

- Full `bash tests/run-all.sh`: **1,092 passed, 11 Windows-only skipped, zero failed**;
  all shell suites passed and live settings/IPC canary checks passed.
- Both Release product builds with `-p:SkipPluginLink=true`: zero warnings/errors.
- Windows x64 hook and toolkit cross-publish/staging succeeded for Vizhi.
- Added 17 C# regression cases: nine exercise Codex envelopes through the real registry,
  routing and answer-delivery code; six cover cross-key Windows startup cancellation and
  restart; two distinguish fresh rollout approval state from actual hook trust.
- The answer tests use an internal entry point to the production answer handler and fake only
  the platform boundary. Temporarily disabling failed-focus and delivery-success guards caused
  four of those cases to fail. Both guards were restored before the green suite and builds.
- Existing tests cover hook/rollout precedence, matching call-output recovery, product path and
  capability isolation, toolkit argument boundaries, resource/layout bindings and voice failures.
- Artifact verification: version 1.6.1 matches DLL 1.6.1.0, exactly two Windows helper executables,
  five public links resolve, and extracted macOS helper/whisper signatures pass.
- `python3 tests/test-package-contract.py <candidate>`: valid artifact accepted; missing toolkit,
  missing hook, and redundant runtime artifacts rejected. These content regression checks use
  offline link mode; the separate candidate verification above checked links online.
- Shell syntax and diff-whitespace checks passed.

## Candidate and limits

Local candidate: `artifacts/VizhiCodex_1.6.1-integration-preview.lplug4`.
Size: **17,385,315 bytes (16.58 MiB)**. The package contains the fresh integrated Vizhi DLL,
hook and toolkit. Unchanged voice payloads were copied from the locally available Claude Console
2.2.2 package and the Vizhi metadata/hash were packed with LogiPluginTool. This is a test candidate,
not a published release. The full release script's Gatekeeper/stapler workflow was not rerun;
the candidate was checked by the central package verifier. Model download behavior is unchanged.

## Remaining release gates

1. Run `tests/run-all.ps1 -Strict` against this integrated source/toolkit on Windows. The earlier
   PR #97 pass concerned Claude Console, not the Codex changes on this branch.
2. On a clean Windows plugin-service instance, install the candidate, import the Windows Vizhi
   layout, trust the current hooks, record the Codex version, and test real lifecycle events.
   Verify pending approval lighting, physical Yes/No and correct targeting across two sessions;
   stale/untrusted hooks must display setup guidance and cannot send an approval.
3. Test Dictate, Draft without submit, startup cancellation/restart, Go to Project, screenshot
   capture/cancel, model/context and the Plan/Skills/Agent/Fork/Resume/Review controls.
4. On macOS, verify installation/upgrade, hooks and trust prompts, approvals, voice, screenshots,
   correct layout resources and coexistence with Claude Console. Earlier hardware evidence does
   not constitute a pass on this integrated candidate. Include supported architecture coverage.
5. Build through the final release workflow, check notarization/Gatekeeper on the extracted helper,
   and tie device evidence to the final artifact hashes. Publish only after these checks pass.

Known limitation: Windows Terminal manually renamed tabs can prevent verified focus (#88).
Clearing the custom name restores automatic titles; failed focus preserves the current routing.
