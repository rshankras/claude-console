# Vizhi Desktop test bench

A local, resumable command checklist for the MX Creative Keypad, backed by production-handler
simulation tests. You perform all real ChatGPT/Codex and keypad actions. The runner does not
inspect/control ChatGPT, record audio, read your clipboard, install a plugin, or edit profiles.

## Run everything unattended

From the **integration worktree**, run:

```sh
cd /Users/ravishankar/Work/MyApps/claude-console/.worktrees/desktop-merge
python3 tools/desktop-test/run.py auto --package VizhiDesktop_0_13_0.lplug4
```

`auto` defaults to the **full repository suite**, then builds the product, verifies the selected
archive offline, runs the controlled GUI fixture, runs fixed-file Whisper transcription, and
writes HTML/Markdown/JSON reports. A failed or blocked stage does not prevent independent later
stages from running. A source/configuration change stops the run. Ctrl+C saves an interrupted run.
The fixture may briefly take focus; it only targets its own disposable application.

Exit codes: **0** all required software stages passed; **1** a failure or changed fingerprint;
**2** blocked/incomplete software coverage (including no archive selected); **130** interrupted.
Offline external links are excluded from this gate. Exit 0 does **not** mean hardware acceptance.
Existing owner observations are never generated or overwritten by automation.

Afterward, `python3 tools/desktop-test/run.py serve` opens the latest run's dashboard server.
The printed URL shows **software coverage by profile** separately from hardware observations.
Each profile has 54 owner cases: 27 ChatGPT + 27 Codex. They reuse passing command dispatch tests;
they are not 54 extra test executions. In the default catalog, 146 of 182 checklist cases link
to 96 command/mode variants; 36 regression cases have no exact dispatch mapping. The broader
suite exercises regression logic, but cannot establish those cases' live app/keypad behavior.

Use `auto --suite desktop` for the smaller suite or pass `--wav`, `--expected`, `--whisper`,
and `--model` to select fixed-file inference inputs. `auto --run <id>` reruns an unchanged run
with its server stopped; previous software evidence is cleared before retrying. Reports and
owner history from other runs remain available.

## Start the guided hardware checklist

From the **integration worktree**:

```sh
cd /Users/ravishankar/Work/MyApps/claude-console/.worktrees/desktop-merge
python3 tools/desktop-test/run.py start --suite full --package VizhiDesktop_0_13_0.lplug4
```

This runs the repository suite and package verifier, then prints the local checklist URL.
Open that complete URL (including its authorization fragment). The server listens only on
`127.0.0.1:8765`; use `--port 8766` if the port is occupied. No Python packages or web build needed.
Prerequisites for automation: the repository's .NET SDK/Logi PluginApi, Python 3.9+, and on macOS
the Swift toolchain. Existing repository tests handle the Windows-only skips.

The first view is **Vizhi Flow → ChatGPT → Quick smoke**. Select the matching profile and
mode on your keypad/app. Follow Prepare, Do, and the two expected outcomes. Click Pass, Fail,
Blocked, or Save notes. Failed/blocked cases require a brief reason. Passing advances to the
next card; a failure can include a screenshot filename or other owner-supplied reference.
The runner does not automatically open evidence paths or upload files.

Use **Full coverage** for every binding. **Optional actions** covers registered actions absent
from both default profiles. **Regression checks** covers Voice repeat/focus/conflict, draft
recovery and long-press discard, original capture intent, approval changes and unavailable surfaces. Configure optional
keys on a spare test profile **before creating a run**; editing bindings during a run makes its
acceptance stale. Switching between existing profiles/modes does not invalidate a run.

## Run, resume, export

```sh
# Run checks without starting the browser server (creates a fresh run).
python3 tools/desktop-test/run.py check --suite full --package VizhiDesktop_0_13_0.lplug4

# Resume the most recent run; --run also accepts a run ID or absolute directory.
python3 tools/desktop-test/run.py serve

# Regenerate standalone reports without running tests or a server.
python3 tools/desktop-test/run.py report

# Inventory/coverage gate only.
python3 tools/desktop-test/run.py catalog
```

Each saved result is written atomically under `artifacts/desktop-tests/<run-id>/run.json`.
The server must be stopped with Ctrl+C before another command changes the same run.
Only one writer is allowed. Browser-local unsaved note drafts survive reloads, but they are not
in reports until Save notes or a result is clicked. Selecting **Failed — retest** shows failures;
retesting preserves earlier observations in JSON history. HTML, Markdown and JSON exports are
also available from the checklist. `report.json` includes the current stale flag and summary;
`run.json` preserves the original run and result history. Configured workflow briefs are included locally; review a
report's content before sharing it.

The default `--suite desktop` runs relevant C# tests plus Swift AX/shortcut/package staging tests.
`--suite full` runs `tests/run-all.sh`. Both also compile the product with `SkipPluginLink=true` into the run directory and fingerprint its DLL. Both require the catalog's 96 mode variants to execute
both success/refusal paths in the production command rig (192 cases), plus the broader scenario
checks. No missing/skipped catalog case can count as a passing dispatch test.

## Optional software integration

Run these with the server stopped, against the current run:

```sh
python3 tools/desktop-test/run.py fixture
python3 tools/desktop-test/run.py audio
```

`fixture` compiles and signs a separate two-window test app and the actual AX helper in the run
directory. Its target is hardcoded to `com.vizhi.desktop.testfixture`. AppKit exposes controlled AXWebArea/group/text-area roles. It checks actual AX IPC/traversal,
composer write/send, preservation, window/mode targeting, simulated Voice controls and shortcut
event delivery. It never opens a microphone. A missing macOS GUI/Accessibility grant or a fixture
that cannot become foreground is **Blocked**; it never requests permission or changes TCC.
Run it from an already authorized interactive terminal and leave the fixture in front.
The fixture exits afterward. This cannot establish real ChatGPT AX compatibility. A WebKit
prototype exposed buttons but refused AX text writes on this machine; the AppKit contract fixture
therefore deliberately makes no claim about Chromium/WebKit editing behavior.

Shortcut delivery is checked across three separate helper invocations: each must deliver exactly
one key-down/key-up pair, with Control+Shift and the intended fixture window. A refused background
shortcut must deliver no additional events. This catches the one-shot helper exiting before
asynchronous event delivery. The helper now retains its source/events and runs its event loop for
200ms after posting; its response still means requested, not confirmed Voice activation.

`audio` uses the local bundled Whisper CLI/model and generates a fixed speech file using macOS
`say -o` (no playback). It transcribes the file and reports word error rate, transcript and hashes;
the threshold is 15%. A missing model/runtime is Blocked. Supply your own fixed test clip with:

```sh
python3 tools/desktop-test/run.py audio --wav /path/test.wav --expected 'The words in this clip.'
```

Use `--whisper` / `--model` to select a different local runtime/model. This verifies file inference,
not microphone capture or composer insertion. Real recording, native Voice audio, SDK folder
navigation, actual LCD rendering, and live app compatibility remain your manual checks.

## Evidence and limits

- Catalog: **48 command/parameter entries, both modes**, all **54 bindings** in the two packaged
  profiles. The default catalog produces **182 owner cases**; custom named workflows can add more.
- C# rig: **211 checks**. Calls the same press handlers and transcript sink used by the SDK,
  with inert automation/clipboard, fixed transcript text and deterministic clocks. Existing tests
  cover the helper parser, risk/state models, capture failure handling and other engine behavior.
- A passing disabled-behavior test keeps ChatGPT **Copy Answer marked unsupported**.
- The report separates automated dispatch, optional fixture/inference, archive verification and
  manual acceptance. A posted shortcut / Requested label is not proof that native Voice worked.
- Each run fingerprints source (including working changes), installed plugin/runtime, profile
  bindings, workflow/shortcut configuration, app bundle version metadata where found, and the
  chosen archive. Build/config changes make old results **STALE** and refuse further sign-off.
  Make a new run to test a new build. Profile-selection bookkeeping is ignored.
- Source and installed artifacts are recorded separately. Checking an older `.lplug4` verifies that
  archive, not that these working-tree changes are installed. **This harness does not reinstall
  the plugin/profile.** See the release installation receipt for the installed version.
- No fully green release claim: unrun, blocked, skipped Windows tests, offline URL checks, optional
  checks, unsupported capabilities and pending owner cases remain visible.

## Maintain the harness

When registering a command/parameter, update `tests/desktop/commands.json` and the corresponding
production-handler scenario in `tests/desktop/DesktopCommandRig.cs`. The catalog gate checks every
registration expression and packaged binding, failing closed on an unfamiliar registration shape.
It is a source inventory check; execution evidence comes from TRX, not source-string presence.

```sh
python3 tests/scripts/test-desktop-harness.py
python3 tools/desktop-test/run.py catalog
```

The Python self-tests cover drift, unique physical case identities, stale/revision protection,
resume/history, custom workflows, test-result ingestion, export escaping, local HTTP authorization,
profile switching and word-error scoring. They use temporary directories and synthetic acceptance.

## Find Chat coverage (0.12.6)

The query page adds Speak Query, Type Query and status actions, plus live regressions for exact
result selection, cancellation, changed targets and pagination. The default catalog now has
46 entries (92 mode variants) and 174 owner cases. Search protocol tests use only the fixture
application; they do not establish ChatGPT compatibility. The fixture verifies native search
field focus/writes, result link selection, existing-draft preservation, and stale-query/window/
mode refusal. Unsupported accessibility layouts show Use App.

The 0.12.7 search fixture adds placeholder-only fields, popup mode controls, nested web content
in a sheet, slow-open recovery without a second opener click, explicit app activation, and
background write/probe refusal. There are now 17 native steps. Specific search failure messages
and Open Search retry behavior are part of the owner's existing Find Chat acceptance cases.

## Compare Speak Query models

See [the Speak Query decision and benchmark](../../docs/vizhi-desktop-speak-query-2026-09-19.md).
`benchmark-voice.py` compares multiple models against one explicit WAV/reference manifest,
records per-sample errors and latency, and never opens the microphone. Use `--without-homebrew`
on Apple Silicon to verify bundled compute backends. For the current Speak Query model, pass
`--model ~/.claude/claude-console/whisper/ggml-large-v3-turbo-q5_0.bin` to `run.py auto` as well;
the generic fixed-file smoke test otherwise keeps its historical base.en default.

## Chromium fixture storage

`chromium-reply-fixture.py` accepts a shared Electron.app runtime, a fresh artifact directory,
a scenario, and optionally a built AX helper. It creates only the fixed disposable fixture
app. After that app exits, the runner preserves its exact generated files in `fixture-source/`
and its bundle metadata in `fixture-info.plist`, along with helper binaries, logs and JSON
results. It removes the duplicated browser bundle and its synthetic user-data cache.
The supplied Electron runtime remains available for future runs.

Set `VIZHI_KEEP_CHROMIUM_FIXTURE=1` for an investigation that needs to retain the generated
browser app and cache. This affects only fixture cleanup, not the installed plugin.
