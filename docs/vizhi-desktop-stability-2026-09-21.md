# Vizhi Desktop responsiveness and lifecycle fixes

Branch: `integrate/vizhi-desktop-main`. Patch: 0.16.1.

## Why this change

The user reported a keypad startup problem and laptop-wide typing/app lag that required a
reboot. The pre-reboot Logitech log contained 74 one-second button-callback timeouts, 74
DesktopControlCommand callback failures, one RunCommand timeout, and six AX helper overruns.
Slow native work was running inside SDK callbacks. Rendering also waited for locks held by
native draft/search operations. Voice animation callbacks could overlap when rendering stalled.
Some singleton event subscriptions were not removed when the plugin unloaded.

This establishes responsiveness and retention defects, not the cause of the laptop-wide
freeze. Post-reboot Logitech RSS changed from 236400 to 236736 KiB over 106 seconds, while CPU
dropped from 9.1% to 1.9%. Other processes also consumed significant CPU. No before-reboot
heap capture exists. The diagnosis is recorded in `artifacts/performance-review-2026-09-21`.

## Implementation

- Desktop gestures dispatch native work to one background operation. Additional taps while
  busy are refused, so stale commands cannot accumulate. Send/Stop intent, conversation slot,
  and approval-card state are captured before dispatch. Repeat and long-press events cannot
  accidentally run ordinary taps; existing hold-to-discard gestures remain available.
- Status polling, Find Chat refresh, and listening animation use serial one-shot timers.
  Generation checks prevent stopped timers from rearming a replacement. Status scans yield
  to commands and transcription, reduce background scans to at most once per 15 seconds,
  and back off after slow reads. A cheap foreground probe does not traverse the AX tree.
- Draft recovery and search display reads no longer wait for native operations. Closing
  search immediately invalidates its session; late results remain hidden. Closing search
  can stop its recording, but cannot toggle a fresh recording on.
- A plugin lifetime owns event subscriptions and timer cleanup. Unload detaches subscribers,
  disables further native helper launches, stops queued work, clears transcript routes, and
  cancels speech-model preparation. Reload reattaches once and can resume preparation.
  A native operation already executing finishes within its existing process timeout.

The keypad layout, profile identifiers, Paste into Chat behavior, automatic dictation
insertion, explicit Send, and guarded Copy Reply are preserved.

## Validation and evidence

`tests/DesktopStabilityTests.cs` covers blocked native work, 10,000 refused duplicate
dispatches, the actual SDK command entry, 500 reload cycles, collection of 1,000 detached
subscribers, foreground/background cadence, latency backoff, timer restart races, nonblocking
draft/search reads, and repeated search closure. Model tests cover cancellation and reuse.

Final C# and script results, controlled native fixtures, package verification, installation
receipt, and resource samples are stored in `artifacts/desktop-stability`. Native fixtures use
the disposable test application, not the user's conversations. Hardware taps and long sessions
remain separate acceptance evidence.

For a longer observation during ordinary use:

```sh
python3 tools/desktop-test/resource-watch.py --seconds 1800 --interval 5 \
  --output artifacts/desktop-stability/ordinary-use.json
```

This records only CPU/RSS/process metadata for the Logitech service and AX helpers. It does
not collect UI content, clipboard text, or audio. RSS belongs to the shared Logitech host;
a short stable sample cannot establish that the plugin is free of long-term leaks. A repeat
slowdown should be correlated with this report and timestamped callback/helper errors before
attributing it to Vizhi.

## Installed verification result

0.16.1 loaded successfully in 185 ms. Installation preserved the selected profile
`A8B982E4103C4F99A4C75070AF60A6E4`, all profile contents, and desktop configuration.
The full suite passed: 1,968 C# tests, 13 existing skips, and all script checks. The final
harness rerun passed 22 tests. AppKit append passed eight checkpoints; Chromium append
and wrapped Copy Reply passed, including the new foreground probe.

The five-minute process sample contained 61 observations of one Logitech host PID.
RSS ranged from 398.6 to 424.6 MiB; the first and last five-sample
medians were 424.4 and 401.5 MiB. Reported CPU had
a 5.0% median and 27.0% maximum. These figures cover the
whole host and its startup caches, and are not a comparison of plugin-only allocation.
No button-callback, desktop-callback, or AX-helper timeouts appeared in the new host log.
Logitech refused one duplicate load attempt; only one successful Vizhi load was recorded.
This does not establish duplicate running plugin instances.

Detailed evidence: `artifacts/desktop-stability/verification.json`,
`installation.json`, `after-install-resources.json`, and `post-install-log-summary.json`.
