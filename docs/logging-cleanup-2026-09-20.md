# Logging cleanup — 2026-09-20

Branch: `integrate/vizhi-desktop-main`. Unreleased source changes; the installed
Vizhi Desktop 0.15.13 and its profiles have not been replaced.

## Changes

- Remove Copy Reply key-event/result tracing and its temporary logging callback.
  Keep native response targeting, clipboard acknowledgement, retained replies and keypad feedback.
- Remove the AX composer's temporary metadata dump and the extra accessibility scan it performed
  after a refused draft insertion. Keep draft-preservation and insertion checks.
- Remove routine keypad, mode, recording, prompt, navigation and screenshot success traces
  across the shared terminal and desktop action sets.
- Remove dictated text from normal, failed-delivery, no-speech and project-matching logs,
  including the macOS voice helper's transcript print.
- Stop generic process runners from dumping arguments, stdout or stderr. Preserve pipe draining,
  returned output, timeout handling and concise failure reporting. Expected nonzero results
  without stderr, such as a closed desktop app, do not create a warning on every poll.
- Remove the Windows hook's per-invocation argument/environment dump. Preserve watchdogs,
  concurrency limits and failure reporting in `hook-error.log`.

Startup, setup/migration, approval delivery and actionable error messages remain. Explicit
test harnesses, self-tests, opt-in Windows focus diagnostics and bounded Whisper crash details
remain available. Historical logs and regression evidence were not deleted.

## Validation

- Full repository suite: **1,942 passed, 13 skipped**; package staging, shortcuts, AX selectors,
  harness, bridge scripts, hooks and input-lock checks passed. Live settings and IPC canary survived.
- After the final process-logging adjustment: **130 focused tests passed**.
- Release builds: Claude Console, Vizhi Codex and Vizhi Desktop; each with `SkipPluginLink=true`.
- Windows hook cross-build succeeded with five platform-analysis warnings; Windows execution
  remains untested on this Mac. The macOS voice helper compiled without launching the microphone.
- Controlled Chromium wrapped-response fixture: native Copy Reply passed.
- Broader AppKit fixture: ten checkpoints passed, including draft preservation and send-once,
  before a later search action refused with `app-not-frontmost`. This run is incomplete, not a pass.
- No new live ChatGPT or physical-keypad acceptance is claimed for this source-only cleanup.

Detailed output, the incremental cleanup diff (separate from pre-existing branch work), and
source hashes are under `artifacts/log-cleanup/`.
