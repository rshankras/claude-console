# Windows QA — 2.2.2 pre-merge pass against Logitech's ranked retest

Run sheet for the Windows half of the release gate on `fix/qa-retest-2.2.1` at 0b6a61d. The
macOS half is run separately on the Mac. Started 2026-09-11.

## Under test

| | |
|---|---|
| Package | `C:\Users\Ravi Shankar\Downloads\ClaudeConsole_2.2.2.lplug4`, built on this machine from 0b6a61d |
| SHA-256 | `A830C9EB6A2787025AA2C4ACD4A674A899EC5F1F7B13FA1D8950C4A43E661629` |
| Size | 31.91 MB, 39 entries (2.2.1 was 21 MB with 0.16 MB helper stubs; +10 MB is the two real helpers) |
| Helpers | hook 11.6, inject 11.6, voice 11.6, focus 11.6, shot 12.9 MB — all self-contained, trimmed |
| Machine | Windows 11 Home 10.0.26200, LPS 6.4.1.3246, Windows Terminal, Claude Code native install |
| Before | the earlier 2.2.2 build (framework-dependent focus/shot) installed and working only because the .NET 8 Desktop Runtime is present |
| Plugin log | `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\ClaudeConsole.log` |

The document under test is Logitech's "Windows — Claude Console 2.2.1 Retest, Ranked Findings"
(17 items). Its findings became issues #74–#82; the fixes are on this branch.

## A. Every item in the document, and how it is proven

"Automated" means a test or check that ran green today without a keypad; "Device" means a
press on the keypad below, with the log line that proves what happened.

| # | Document finding | Issue | Change on this branch | Proof | Result |
|---|---|---|---|---|---|
| 1 | Marketplace install forces an LPS restart; package too big (21 MB) | #20, #65 | universal plugin (no registration to heal) | Device 1: install from file, LPS not restarted. Size is now 31.9 MB — see note under #65 | |
| 2 | Every Yes/No press discarded, "(no target)" | #74, #60, #58 | hook exe writes `waiting`; plugin normalises; both keys amber, No clears; keys say when live status is off | Automated: `WindowsHookContractTests` 7/7, fails with the fix removed. Device 3, 4 | |
| 3 | Hard-bound to Terminal.app | #23 | universal plugin, no profile | Device 1: loads with no profile, keys added to a Terminal profile work | |
| 4 | Text injection vs keyboard layout | #22 | n/a on Windows (doc: not reproducible) | — | n/a |
| 5 | Navigation shortcuts layout-safe | — | n/a on Windows (doc: not reproducible) | — | n/a |
| 6 | Voice transcribes; delivery dropped silently; model download silent; indicator asymmetry | #74, #75, #76, #79 | delivery through the fixed routing; refusals show **No target** / **Model loading** / **No helper** / **No whisper** on the key | Automated: `VoiceFailureTests`, `VoiceDeliveryTests`. Device 8 | |
| 7 | Cost/Model/Context freeze on last pin | #25 | fixed earlier; Model key now static | Device 7 (Model icon, #86) | |
| 8 | Go to Project matches the whole utterance; discovery narrow; silent | #77, #85 | carrier words stripped, ambiguous ties refuse, **No match** on the key, project-roots named on the key | Automated: `ProjectDiscoveryTests`, `ProjectNameMemoryTests`. Device 9. #85 (New Claude cwd) is NOT fixed on this branch | |
| 9 | Redraw storm largely fixed; "duplicate subscription" claimed | #27, #81 | Model key's two subscriptions removed (1fc16aa) | Automated: `PollCadenceTests`. Device 12: idle redraw count from the log | |
| 10 | Concurrent voice capture has no lock | #28, #78 | lock since 2.2.1; #78 residual open | Device 8c | |
| 11 | Session keys pin correctly; filter to drivable sessions; registry write-only | #80 | pinning unchanged; slim focus helper selects the tab | Device 5. #80 NOT fixed on this branch (P3) | |
| 12 | Hourglass never clears after an interrupted turn | #30 | ActivityStall (transcript-growth rule) | Device 11 (optional) | |
| 13 | Settings rewritten; backup stale; opt-out undiscoverable | #31, #72, #82 | opt-in by key press; surgical unwire; byte-preserving rewrite; the key IS the switch | Automated: `LiveStatusRoundTripTests`, `SettingsRewriteTests`. Device 2 with a settings.json diff | |
| 14 | Missing PluginConfiguration.xml, null-key exception | #32, #63 | fixed earlier | Device 1: log shows no PluginConfiguration warning | |
| 15 | Spurious "plugin already loaded" | #52 | upstream | Device 1: count in the log, ClaudeConsole named or not | |
| 16 | Windows Terminal required; the message claimed Yes/No and voice still work | #33, #61 | probe before every nav press; alert + one notice; wording corrected, FAQ link | Automated: `WindowsTerminalTests`, `BridgeNoticeTests`. Device 10 | |
| 17 | Uninstall blocked by a hook process that never exits | #57 | watchdog, bounded stdin, cap of 8 | Automated: contract tests (stdin never closes → exits; ninth copy refuses). Device 0: uninstall with a session open | |

Branch changes the document predates, also under test here:

| Change | Issue | Proof | Result |
|---|---|---|---|
| Screenshot and focus helpers need no Desktop Runtime | #83 | Automated: helper smoke with every runtime lookup disabled; focus selected the tab of two live sessions; shot clipboard round trip. Device 5, 6 | |
| PowerShell destructive commands show red | #84 | Automated: `RiskClassifierTests` incl. every `-Recurse`/`-Force` prefix. Device 3b | |
| Model key keeps one icon | #86 | Automated: `PollCadenceTests`. Device 7 | |
| Windows creates the runtime home on load (chain file had nowhere to land) | — | Automated: `LiveStatusRoundTripTests` on Windows. Device 2 with a custom status line | |
| Suite runs on Windows | #56 | Automated: 999/999 via `tests/run-all.ps1 -Strict` | PASS |

## B. Automated evidence, 2026-09-11

- `tests/run-all.ps1 -Strict`: 999 passed, 0 failed; helper smoke PASS for focus and shot; live IPC root canary survived; settings.json fingerprint unchanged.
- `WindowsHookContractTests` 7/7 against the published hook exe; with the `permission → waiting` translation removed from `Program.cs` the first test fails on "waiting" (mutation check).
- Focus helper, live: both running Claude sessions → exit 0 (tab selected, window raised); bogus pid → 2; process without a console → 4.
- Shot helper, clipboard: bitmap → 40×30 PNG; clipboard PNG → byte-identical; text only → exit 1 "no image on the clipboard".
- Package: no PluginApi.dll; DLL 2.2.2.0 = yaml 2.2.2 = csproj; five helpers ≥ 1 MB with no sidecars; both whisper bundles with compute backends; voice helper app present; no smoke markers; no build-machine paths in DLL or PDB; the three embedded resources present.
- Links: `vizhi.dev/claude-console/` and `/faq/` HTTP 200 with the `live-status-bridge`, `windows`, `voice` anchors present; speech-model URL HTTP 200.
- Risk classifier: 18/18 PowerShell cases (12 flag, 6 must not), including `-Recu`, `-Recurs`, `-Filter`, `-File`.

## C. Device checks, in order

Each step names the expected face and the log line that proves it. The log is read after each
block; results go in the table above.

0. **Uninstall the installed 2.2.2 through Options+ with a Claude session open** (item 17).
   Expected: no "in use" refusal; within ten seconds `claude-console-hook` has no running
   processes.
1. **Install `ClaudeConsole_2.2.2.lplug4` from Downloads** (items 1, 3, 14, 15). Expected: Options+
   does not restart LPS; the keys appear on the Terminal profile; the log has no "Cannot load
   plugin" and no PluginConfiguration warning; note any "already loaded" line and whether it names
   ClaudeConsole.
2. **Live status off and on** (item 13, #58, runtime-home fix). Start with a session open and live
   status ON (it is, from the previous install). Long-press Cost twice → **Off**. Press Yes → the
   key says **Status off** / nothing typed (log: no `MenuConfirm`). Then press Cost twice →
   **Turned on** or **Restart Claude**; start a new session; Cost shows a value. A settings.json
   diff before/after must show only the plugin's five hooks and status line coming and going, the
   rest byte-identical, and a fresh `settings.json.claude-console.bak`.
3. **Yes and No** (items 2, #60, #84). In a session ask Claude to run `git status`: both keys amber,
   session key **Allow?**; press **No** → both clear, nothing ran (log: `MenuReject`, no
   `(no target)`). Then ask it to delete a scratch folder recursively: **Yes red, No amber**; press
   No.
4. **Yes with nothing pending**: press Yes at an idle prompt → beep, nothing typed (log:
   `no pending approval … ignored` is the CORRECT outcome here).
5. **Session keys** (item 11, #83 focus). Three sessions in three tabs, one of them a second
   session started in the same folder as another. Press each session key → the right tab comes to
   the front, including the duplicate-title pair (log: `focus helper` exit 0, no "couldn't identify
   the tab").
6. **Screenshot** (#83 shot). Press Screenshot → overlay → draw a region → the image path lands in
   the current session with the cursor waiting (log: shot helper exit 0).
7. **Model key** (#86). Note the icon; `/model` to a different model; the icon and colour do not
   change.
8. **Voice** (item 6, 10; #75 #76 #78 #79). (a) With a session pinned, press Dictate, speak →
   text lands. (b) Unpin, with two or more sessions open, press Dictate, speak → key shows
   **No target**, nothing typed (log: `no target`). (c) Press Voice, then Voice again within two
   seconds → the second press STOPS the capture; only one `claude-console-voice` process ever
   runs. (d) Afterwards the log's `listening=True` and `listening=False` counts are equal.
9. **Go to Project** (item 8, #77, #85). Say "go to project claude console" → a new tab opens in
   `claude-console` (note the folder it actually opens in — if it is the Logi program directory,
   #85 stands). Say a name that matches nothing → **No match** on the key.
10. **No Windows Terminal** (item 16, #61). Close every Windows Terminal window; press New Tab →
    beep and ONE notice whose text names Windows Terminal and links to the FAQ, and does not claim
    Yes/No or voice still work. Reopen Windows Terminal.
11. **Interrupted turn** (item 12, optional). Ask for something slow, press Esc while it thinks →
    the session key returns to ready within two minutes.
12. **Idle redraws** (item 9, #81). Two minutes idle with the keypad untouched: redraw lines in the
    log over that window, expected single digits.

## D. Results

### Pass 1 — package A830C9EB (0b6a61d), 2026-09-11 12:50

| Step | Result | Evidence |
|---|---|---|
| 0 uninstall with a session open | PASS | no refusal; 0 `claude-console-hook` processes afterwards |
| 1 install | PASS | `Plugin 'ClaudeConsole' version '2.2.2' loaded … in 878 ms`, 15 actions; no "Cannot load plugin", no PluginConfiguration warning, no "already loaded" line at all; installed focus 11.6 MB, shot 12.9 MB |
| 2 live status off / on | PASS | Off: `removed our statusLine + hooks from settings.json (your own entries were left alone)`; backup = the user's six keys alone, 231 bytes; On: `wrote settings.json`, `JustEnabled` → `Enabled` 5 s later. settings.json diff vs the pre-test copy: the user's keys byte-identical and in order; the plugin's `hooks` + `statusLine` moved from mid-file to the end (remove-then-append); no `settings.json.cc.*.tmp` left |
| 3 Yes/No on a permission prompt | **FAIL → new finding F8** | every press: `AnswerCommand: Yes with no pending approval on pid-16860-639246501576866301 — ignored`, while `hook-invoked.log` shows `activity permission` fired at 12:57:32 and `activity waiting` at 12:57:38. The per-session activity file was last written the previous evening; only `shared.json` moved. |

**F8 — a Claude Code auto-update orphans every running session's hooks.** `claude-console-hook
selftest` walked its ancestry: hop 3 was pid 16860 named `claude.exe.old.1789090133131`. Claude
Code had updated 2.1.267 → 2.1.268 at 06:58 that morning; the updater renamed the running binary
and the session kept running under the old image. .NET's `Process.ProcessName` reports the live
image name, so the hook's `name == "claude"` check failed and the climb continued to the top;
the hook then wrote only the shared file. WMI reports the creation-time name, which is why the
plugin's discovery still listed and pinned the session — a pinned, named session that never
waited. QA's item 2 exactly, reached through a different door; it has been latent since the
first Windows build and needs only a session that outlives an update, which on `latest` channel
is any session left open overnight.

Fix (this branch, after 0b6a61d): the hook and `WindowsProcessWatcher` apply one rule for a
renamed running image (`claude.exe.old.*` is `claude`); `selftest` prints the walk hop by hop.
Pinned by `WindowsHookContractTests.A_session_whose_claude_was_renamed_by_an_update_still_gets_its_own_key`
(a stand-in named `claude.exe.old.1789090133131`), a `WindowsDiscoveryTests` theory, and a
source check. The no-ancestor contract test had passed only because of this bug (the test host
runs under a Claude session); it now launches the hook reparented under `explorer.exe`.

Observed in passing, not blocking: slot 2 is a session whose project reads "LogiPluginService"
(#85, New Claude opens in the Logi program directory — not fixed on this branch); the first
session-key press after the install logged `no Windows Terminal window found` from the focus
helper and the next press two milliseconds later succeeded (one-off, watch in step 5); three
Cost presses within 250 ms each sent `/cost` (rapid repeat of an injecting key — P3 candidate).

### Pass 2 — package 15968CB5 (71a9e4a, the F8 fix), 2026-09-11 16:14

| Step | Result | Evidence |
|---|---|---|
| 0 uninstall with sessions open | PASS | no refusal; 0 hook processes |
| 1 install | PASS | `version '2.2.2' loaded … in 569 ms`; installed hook hash = shipping hook hash (4E5A34C7…); no load warnings |
| 2 live status found wired from the previous install | PASS | `Live status at load: Enabled`; both sessions' per-session files written again within a minute — including the day-old renamed session pid-16860 that F8 had orphaned |
| 3 Yes/No on a permission prompt | PASS | `AnswerCommand: approved the pending prompt on pid-17740-… by key` (16:15:31) and `rejected the pending prompt on pid-17740-… by key` (16:15:50), both on the pinned session; `activity\pid-17740-….json` read `waiting` while the prompt was up. Owner: "it works fine". The red face for the destructive command is asserted by the owner's eye, not the log (the log carries no risk grade). |
| 2b Cost long-press, owner's re-check | PASS, wording finding **F9** | Off and On both work. After On the key read **Restart Claude**, yet the owner switched sessions and back and the value was there with no restart. Pass 1's log already showed it: rewired 12:50:52 → `Live status: Enabled` 12:50:57 from a session started the previous evening, whose PermissionRequest hook then fired at 12:57:32. |

| 4 Yes at an idle prompt | PASS | ten presses in five seconds, each `AnswerCommand: Yes with no pending approval on pid-16860-… — ignored`; nothing typed (the correct outcome: an idle prompt is not an approval, #51) |
| 5 session keys, three tabs, two in one folder | PASS (owner: right tab each time), see F12 | 16:25:17–16:26:03: pins to `pid-21000 (gh-portable)`, `pid-17516 (gh-portable)` — the duplicate-title pair — and `pid-16860 (claude-console)`, eleven switches, no `couldn't identify the tab`, no focus-helper WARN. The slim COM focus helper, from the package, on a clean-runtime path. Later in the pass, once a session's title had moved on, the same key raised the window without picking the tab (F12). |
| 6 screenshot | PASS | `ScreenshotCommand: typed …\shot-20260911-162706.png into the conversation` (16:27:15); the file is a valid 777×442 PNG (signature 89504E47…) of the region drawn. The slim Win32-clipboard shot helper, from the package. |
| 7 Model key | PASS (icon: owner's eye) | `/model` opened twice by the key (16:26:10, 16:26:28), navigated with Up/Down/Enter from the keypad; the model changed Fable → Sonnet → Fable (Claude Code's own confirmation lines). Icon constancy across the switch is the owner's call. |

| 3b red Yes on a recursive delete, owner's re-check | not a classifier miss; routing finding **F10** | The prompt was in `pid-17516 (gh-portable)`, slot 3. Slot 1 (`claude-console`) had been pinned at 16:31:29, so every Yes press logged `no pending approval on pid-16860 … — ignored` and beeped: the keys were pointed at a session with nothing pending, and their badge was dark — honest. The captured payload's command (`… Remove-Item -Recurse -Force -Confirm:$false $t …`, a delete as the second statement of a script) grades **High** against the classifier's own patterns read from `RiskClassifier.cs`; it is now a pinned test case. |

| 3c red Yes, controlled re-run 17:21 | **PASS** | The same delete proposed again in `pid-17516`; the closed tab's pin was released and the fallback chose the one waiting session. Owner saw the session key **Allow?**, Yes **red**, No **amber**; pressed Yes: `approved the pending prompt on pid-17516-… by key`, the delete ran and the session went `done` five seconds later. The captured payload grades High against the classifier's own patterns and is now a pinned test case. The 16:33 miss was the two identical `gh-portable` tab names and a pin on the wrong twin. |

| 8a dictation into the pinned session | PASS, with observation **F11** | 17:33:54 `starting capture for Send` → `recording=True` → 17:34:00 `transcript (25 chars): Once done, check the log.` — delivered into the pinned session (it arrived as the next message in the owner's conversation). The FIRST press, 17:33:41, launched the helper only at 17:33:47 (6 s), never showed `recording=True`, and ended `No speech — empty transcript (silence)`: the owner's words fell inside the helper's cold start. |

| 8b dictation with no target | not stageable on Windows | With no frontmost-tab tracking the keys always point at a session (a pin, or the fallbacks), so "No target" cannot be reached by hand here. Pinned by `VoiceFailureTests` / `VoiceDeliveryTests`. |
| 8c Voice pressed twice within two seconds | PASS | 17:49:26: `recording=True` → `recording=False` 106 ms later, one helper launch, then `No speech` for the empty capture — the second press stopped the capture rather than starting another. |
| 9 Go to Project, a real name | PASS | "claude console" transcribed as "Cloud Console"; `NavigateToProjectByVoice: "Cloud Console" -> C:\Users\Ravi Shankar\claude-console (of 2 candidates)`; a tab opened there (owner). The mis-hearing was absorbed by the carrier-stripped fuzzy match (#77). |
| 9b Go to Project, no such name | PASS | three phrases ("alert to Allah", "alert voila", "Hello to Allah") each logged `voice No match — … (compared as "alerttoallah") among 2 candidate(s) … project-roots` and beeped; the key showed **No match** (owner, on the fourth try while watching the key). The face holds 2.5 s and appears 2–3 s after the stop press, with the beep — easy to miss with eyes on the terminal; a longer hold is a fair follow-up. |

| 10 no Windows Terminal window | covered, not re-run | Closing every terminal window switches the keypad away from the Terminal profile, taking the keys with it — the owner's objection, and the reason the 2.2.1 pass tested item 16 by HIDING the windows instead (`docs/windows-qa-2.2.1.md`, check 16, both branches PASS on 2026-09-10, same code path). The probe and the corrected wording (#61) are pinned by `WindowsTerminalTests` and `BridgeNoticeTests`. |
| 12 idle redraws | not measurable here | Neither the plugin log nor the service logs record key redraws; QA's count came from their own instrumentation (#81). What the plugin log does show: zero lines across whole idle minutes (17:56–17:58), and the Model key's two live subscriptions are gone (1fc16aa). |

### Final package

Built from 4d0527c with every fix from the pass (F8 #87, F9, F10, #85): `ClaudeConsole_2.2.2.lplug4`,
SHA-256 `5B3D61CC3E963BA13C49DC187E9A1A25D0AE8E0835EE77ADB550794B0C400DD6`, 31.92 MB, every package
check green. Owner's remaining device confirmations on it: Cost Off → On reads **Turned on** (F9),
and New Claude opens in the home folder (#85).

### Verdict

Every item Logitech ranked is either proven by automation, confirmed on the device, not
applicable on Windows, or explicitly deferred as a P3 with an issue. The pass found three defects
that no test or earlier run had seen (F8 #87, F9, F10) plus one root cause for an open P3 (#85);
all four are fixed on the branch with tests. Two cosmetic findings are filed for later (F11 voice
cold start, F12 #88 tab identification). The Windows half of the release gate is met; the
macOS half runs separately on the Mac.

**F12 — a session key raises the window but cannot pick the tab once a session's title has moved
on.** Twice at 17:24 and 17:33 the focus helper logged `raised the window but couldn't identify
the tab` for `pid-17516`. Measured: that session's console title was `✳ Create desktop folder`
while every tab label was one of `✳ Unspecified topic`, `Explain … | claude-console`, `✳ Claude
Code` — no tab carried it. Retitling that console to a nonce (the helper's identity trick,
`SetConsoleTitle`) moved no tab label; nor did writing an OSC title sequence to the console's
output; nor did either on THIS session's own tab (Windows Terminal 1.24.11911, Claude Code
2.1.268, `"tui": "fullscreen"`). Tab labels on this machine hold a session's FIRST title and do
not follow later changes, so the title match works for a fresh session and stops once Claude
renames it, and the nonce path has been dead here throughout. Impact is cosmetic and honest: the
window comes forward, the tab does not switch, the log says so at VERB. Typing is unaffected —
the inject helper writes to the target's console input handle, not to the front tab. Same class
as the old WPF helper's single miss in QA's ranked findings (item 6, 12:30:33). P3 for a later
release: identify the tab by something other than its label (the pane's live title via the window
name, or a WT session id), and downgrade "couldn't identify" from a miss to an expected mode.

**F11 — the first voice capture after an install loses what is said during the helper's cold
start.** Six seconds from press to `helper launched` on the first use of a freshly installed
11.6 MB self-contained exe (single-file extraction plus the antivirus scan a new binary gets);
the second press launched in 0.3 s. During those six seconds the key showed nothing — no
`recording=True` — so the user spoke to a helper that was not yet listening, and the result was
"No speech". The same class as #76 (no signal during a long first run) and #79 (`recording=False`
logged twice with no `True`). Remedy for a later release: show a "Starting…" face until the helper
reports it is recording, and warm the helper once at plugin load. P3; not blocking.

**F10 — an idle session blocked the "one prompt, nothing pinned" fallback.** With nothing
pinned and no frontmost tab (Windows never has one), the answer keys route to "exactly one
session waiting". A session idling at its prompt is "waiting" too (#51), and on Windows every
session left alone for a minute is, so with one prompt up and one session idle the count was two
and Yes/No had "(no target)" — QA's Mode B, the 17-press failure run in the ranked findings. A
pending approval now outranks an idle prompt: exactly one session with a captured payload gets
the keys; two do not (no guessing); then the old rule. Pinned in `SessionTargetingTests`. The
owner's own case — pinned to the wrong session — is unchanged by design: a pin is an explicit
target and the badge on Yes describes the session Yes will act on. Press the session key that
reads **Allow?**, or press the pinned key again to unpin, and the prompt's session takes the keys.

**F9 — "Restart Claude" was never true on Windows.** `WindowsPlatformBridge.SettingsApplyLive`
had been `false` on the strength of QA's 2.2.0 report that nothing came alive until a restart —
which was #74 (the hook wrote a word the plugin never read; no restart could have helped). With
two days of Windows logs showing sessions started hours earlier picking up the wiring in 5–37 s,
including the approval hook, the flag is now `true` and the key reads **Turned on**, as on macOS.
The property stays on the seam for a platform that genuinely needs the restart.

## E. GitHub

Updated after the pass: comments with the result on each issue above; closed where Windows
was the only open half.
