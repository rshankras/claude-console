# Vizhi 1.6.1 integration readiness — 2026-09-15

## Candidate

Integration branch: `integrate/vizhi-windows-validated`.
Combines follow-up commit `a69688ef3f14fa8250a23b335c1ed37b026e9f42`
with main `fe6ec7617580f456ede6d5e3e70e9b00560063e5`.
Merge commit: `4ce9a84ffbf364d5a2c4445e47985d6c2ccea423`.

The merge was conflict-free and changed only README and documentation. A tree comparison
of `src`, `tests`, `tools`, `scripts`, and `profiles` against the follow-up branch was
identical. Local profile edits and temporary build files from the original checkout
are excluded. This candidate contains the earlier PR #100 integration and all follow-up fixes.

## Checks on the integrated branch

- Full C# suite: 1,160 passed, zero failed or skipped. The test fixture published the
  hook from this checkout. Existing xUnit1031 analyzer warning remains.
- Both products: Release builds passed with zero warnings/errors, using
  `SkipPluginLink=true`. The installed plugin was not reloaded by these builds.
- Profile update tests: six passed.
- Input-lock test: 12 separate helper processes delivered intact text/Enter pairs.
- Fresh console-input retest: **blocked**, not passed. Publishing the toolkit succeeded
  (existing IL2026 COM trimming warning), but Windows Application Control refused to
  launch the generated executable with WinError 4551. No policy settings were changed.
- No GitHub Actions workflows are present and no remote checks were reported when preparing
  this PR. These are local test results, not GitHub CI results.

Logs and TRX are local under the parent checkout's `tmp/`; they are not release artifacts.

## Prior Windows evidence

See `windows-acceptance-fixes-2026-09-15.md`, `windows-hook-timeout-fix-2026-09-15.md`,
and `windows-manual-acceptance-2026-09-15.md` for build-specific results. The owner
installed the hook-timeout preview; executable hash matched, events reached the correct
session, and the owner saw no hook failure during the retest. Earlier real console-input
and package checks passed; they do not change the blocked result above.

## Before marking ready for main

- Complete macOS tests (`bash tests/run-all.sh` with the supported SDK and Logi dependencies),
  both product builds, and installed device checks of shared focus, voice and session behavior.
  Unix PTY/fcntl suites were not run on this Windows machine.
- Complete the fresh console-input check on a machine where the generated helper is approved
  to run, or through the project's approved signing/distribution process.
- Review the complete main-relative change, including earlier integration and follow-up work.
- Explicitly decide whether the screenshot profile behavior is acceptable as a known issue:
  the picker switches to the default keypad profile and hides Vizhi Escape. Keyboard Escape
  dismisses it and restores Vizhi automatically (owner confirmed).

Signing/notarization and release installation checks remain separate release work. This PR
is a draft integration candidate; no merge into main or release publication has occurred.
