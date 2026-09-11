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

### Pass 2 — package rebuilt with the F8 fix

Steps 0–2 repeated on reinstall, then 3 onward.

## E. GitHub

Updated after the pass: comments with the result on each issue above; closed where Windows
was the only open half.
