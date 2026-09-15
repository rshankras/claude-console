# Windows console tests and renamed-tab selection

## Plan

1. Make the real console-input fixture run on Windows and expose setup errors.
2. Identify renamed tabs from their console identity rather than their displayed label.
3. Exercise rollback and stale-process guards, then test the actual published helper.
4. Prepare a separately identified Windows preview after runtime verification.

## Console-input fixture

The fixture opened `CONIN$` read-only, and `SetConsoleMode` returned Win32 error 5.
Opening the input buffer with read/write access fixes setup. The next run exposed an
independent interop error: `ReadConsoleW` returns a character count, not a promised
NUL-terminated string. The fixture now uses a character array and the returned count.
Setup failures are written beside the readiness file, since allocating a console can
replace standard handles. The Python harness includes that error in its failure.

The integration test verifies concurrent complete text/Enter deliveries into two
disposable consoles and rejects mismatched process generations both before and after exit.
None of these fixture changes alter the installed input helper.

## Renamed tabs (#88)

A manually renamed Windows Terminal tab does not follow `SetConsoleTitleW`. However,
the terminal pane exposes its underlying title through UI Automation `HelpText`:
[Microsoft Terminal's TermControlAutomationPeer](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalControl/TermControlAutomationPeer.cpp).

The helper briefly sets an unpredictable marker on the verified target console. A tab
whose label follows that marker can be selected directly. For renamed tabs, the helper
probes the terminal panes, which are exposed only for the selected tab. It temporarily
selects candidate tabs without raising their windows and accepts only the pane whose
`HelpText` exactly equals the marker. It focuses that pane, preserving custom tab names.

Unsuccessful probes restore the original tab. The probe refuses to start without a
single original selection and stops at a five-second search budget. A cross-process
mutex serializes these focus operations across products. Unique labels also require
console identity verification, since a custom title can impersonate another session.

This fallback can briefly display candidate tabs in a visible terminal window. A missing
identity signal, overwritten marker, disappearing tab, or unsupported terminal remains
an unresolved selection, never permission to guess. The outer plugin helper deadline
still applies; a forcibly terminated helper cannot guarantee its cleanup ran.

## Validation

- C#: **1,146 passed**, zero failed/skipped; includes rollback, failed selection,
  ambiguous selection, timeout, and unique-label identity regressions.
- Real console input: two disposable consoles received three concurrent complete
  prompts each; wrong generations of live processes and exited targets were rejected.
- Shared input lock: 12 independent processes delivered complete text/Enter pairs.
- Published toolkit startup: all five self-contained dispatch/runtime checks passed.
- Pane identity: **eight live checks passed**, two rounds across four Codex processes,
  using `focus tab --pid N --start-ticks T --probe-panes true`. This forces the new
  fallback even when the tab label follows the console title. Optional
  `--diagnostics PATH` records the candidate/identity trace locally.
- Both product Release builds: zero warnings/errors. The Windows toolkit publish
  retains its existing COM trimming warning; tests retain the existing xUnit warning.
- Profile tests: six passed. Package regression cases: four passed. Package verifier:
  passed, including all three support/download URLs.
- Live settings and the IPC canary survived the suite.

The initial 2.5-second pane search had one unresolved live check. Diagnostic retry
succeeded; refreshing the nonce during probes and allowing five seconds then passed
both complete rounds. A build-runner SDK lookup and sandboxed URL checks also failed
before correcting the runner and rerunning those steps; these were not product failures.

Preview: `artifacts/VizhiCodex-1.6.1-windows-fixes-20260915/` contains the package,
SHA-256, uncommitted source patch/additions, provenance, and logs. The plugin and toolkit
were rebuilt; hook and voice payloads were preserved from the earlier Windows package.
Mac signing/notarization and Mac-only suites were not rerun on this Windows laptop.

The custom-label physical keypad check remains outstanding: rename one tab to
`anything`, select another tab, and press the renamed session key. It must select the
correct session while retaining `anything`. Forced fallback checks do not replace that
acceptance step; do not close #88 or claim complete device/release sign-off yet.
