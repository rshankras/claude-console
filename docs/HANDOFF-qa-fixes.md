# Handoff — the 2.0.1 QA retest fixes

State as of **2026-08-28**, end of day. Branch `fix/qa-retest`, branched from `main` (2.1.0), pushed.
Read this before touching the QA work; the issue tracker carries the detail, this carries the shape.

## START HERE (fresh session)

**Every fix below is on `fix/qa-retest` and pushed. Nothing is merged, so `Closes #NN` has not fired and
the issues still show OPEN on GitHub — the tracker understates what is done. This file is the
authority on status, not the issue state.**

**Done and pushed:** #20 #21 #22 (P0s) · #24 whisper backends · #25 pin split · #26 project roots +
leaked build path · #27 redraw storm · #28 voice lock · #48 noise annotations · #49 idle-session
state + project-name memory · #51 badge inflation · #46 subprocess timeouts · #39 icon converter ·
#30 interrupted-turn hourglass · #45 recovery scripts reach users · #18 silent voice failure ·
**#23 universal plugin (verified on device)** · #31 settings.json rewrite · #29 undrivable sessions · #32 null-key traces.
#50 closed wontfix.

**Every Mac-side bug on the retest list is DONE.** What is left, in the order it matters:

1. **REPACK before anyone sees a package.** `ClaudeConsole_2.2.0.lplug4` in the worktree root (28 Aug
   15:29) predates the universal change, #31, #29 and #32 — it is the app-bound build from the voice
   round. `DOTNET_ROLL_FORWARD=LatestMajor bash tools/voice/pack-release.sh 2.2.0` (the notarized voice
   payload in the runtime home is still valid; it refuses without it). Then the clean-machine pass per
   `docs/clean-install-test.md` (rewritten for universal: NOTHING on the keypad after install is correct;
   import the `.lp5`).
2. **The draft PR** `fix/qa-retest` → `main`, so the 25 `Closes #NN` lines finally reach the tracker.
   **Merge BEFORE the package reaches QA.** The #31 notice's button opens the README on `main`; until the
   merge it lands on the old section ("no action needed", no `--unwire`) and contradicts the card — the
   owner tapped it on 2026-08-29 and got exactly that. A version-pinned link would fix it for good, but
   tags are not reliable here (2.1.0 has none), so the order is the fix.
3. **The PM reply** — every answer is in this file: universal done and verified; Mac voice verified on a
   packaged install; Windows voice = #47 (laptop); labels Yes/No; colour + placement agreed WITH THE
   CONDITION that the approval row never lands on the Esc/Voice slots (#42 — "locked in" could quietly
   ship the layout we objected to); icons: 9 missing, 7 of them exist only at 32px in `Icons_old`, ask
   for SVG on one safe-area grid (glyph extents measured 28–84% of the frame); #37 deferred to Vizhi's
   retest; retest item 15 (already-loaded) = #52, LPS-side — reproduces on Logitech's own Spotify/Zoom, evidence on the issue; the contract they asked for. Then close #34 (moot), #40 and #42 (decided) with a line each.
4. **#40's palette swap is UNCOMMITTED in the OTHER worktree**: `src/Core/Helpers/KeyImage.cs` (and
   `GitCommand.cs`) modified on `feat/vizhi-desktop` at `~/Work/MyApps/claude-console`. The decision it
   was waiting for landed on 2026-08-28. Commit it there, or port it here.
5. **Device checks still owed, none blocking:** #28 on the package (Voice, then Voice Draft
   mid-recording → stops); #30's KEYBOARD Esc (90s rule; the keypad Esc is verified); Ctrl+C vs Esc;
   #25 (pin one session, look at another: Cost follows your eyes, Yes stays with the pin); #27's
   zero-session idle CPU (`spikes/redraw-27/measure-27.sh 120`, all sessions closed).
6. **Windows, later by the owner's choice:** #47 (P1, packaged voice never shipped — cherry-pick the
   `WINDOWS_WHISPER_DIR` section from `feat/vizhi-desktop`), #33, #36, and one import of
   `profiles/ClaudeConsole-Windows.lp5` to confirm the `windowsterminal` / `DefaultWin` names chosen by
   analogy.
7. **Next release, with Logitech:** #41 icons (SVGs), #43 Codex answer row (two spikes first), #44 slot
   order key (recency never default), iTerm2 as a second session driver (#29's seam), #37, #38.

**Suggested next: item 1, then 2.** The per-issue sections below carry the reasoning; the class
comments in the code carry the traps.

**An earlier version of this file said "#18 is superseded by #24 — close as duplicate". That was
wrong**, and it was checked before being acted on: #24 added the `.error` sidecar for WHISPER failures,
but the helper's denied-microphone path exited before the sidecar writer was even defined. The field
report behind #18 was exactly that case, and it was still silent. Read the class comment before
closing an issue on the strength of a note.

**Machine state right now (end of 2026-08-28):** the keypad runs the UNIVERSAL dev build via the
`.link` → `claude-console-p0/bin/ClaudeConsole/Debug` (DLL 1.04 MB — ~140 KB means a `-t:Compile`
poisoned `obj/`; `rm -rf src/Products/ClaudeConsole/obj bin/ClaudeConsole` and rebuild). **No package
is installed** (the voice-round package was uninstalled for the universal test) and **no
`@_claudeconsole` registration exists** — correct. Options+ has its own `com.apple.terminal` entry with
the imported "Claude Console — Keypad" profile; a stale old-layout import may still be listed — delete
it. `~/.claude/settings.json` is WIRED (the #31 round trip ended wired); the Options+ "!" notice from
the last wiring clears on the next no-change load. Mic grant: allowed. `tools/dev-reload.sh` is now
just build + size check — a reload loses nothing since universal. **LogiPluginService is NOT
supervised on this Mac**: after `killall`, start it with `open -a /Applications/Utilities/LogiPluginService.app`.
The worktree directory is still called `claude-console-p0` (branch `fix/qa-retest`) — deliberate; the
`.link` names the directory.

## Where it stands

| | |
|---|---|
| Branch | `fix/qa-retest`, 51 commits, pushed, **no PR opened** |
| Suite | 785 C# + 33 shell + 27 codex-hook, green |
| Issues | 25 filed (#20–#44) plus #45, #46, #47, #48, #49, #50, #51 found while working. Milestone `QA fixes — CC 2.2.0 / Vizhi 1.5.4` |
| Blocked on Logitech | #23, #35, #40–#43 (label `blocked:logitech`) |

### The four P0s

| Issue | Reproduced | Fixed | Hardware-verified |
|---|---|---|---|
| **#20** install forces an LPS restart | yes | `655f98d` + `4f2438e` | dev-link path both ways; **packaged install still unproven** |
| **#21** No key approves the action | yes, on the keypad | `592097d` | yes, on the keypad |
| **#22** injection breaks on non-US layouts | yes, on the keypad | `c5f6932` | yes, on the keypad |
| **#23** universal plugin vs packaged profile | n/a, a decision | — | blocked on Logitech |

## What is NOT done

- **#25 sticky pin: DONE (code), hardware pass outstanding.** One resolver answered two questions;
  it is now `RoutingTty()` (pin first — injection, answer keys, approval badge, slot highlight) and
  `DisplayTty()` (frontmost live session, falling back to routing — Cost/Model/Context).
  **The rule that decided the split: a key's badge must describe the session that key acts on**, or
  an amber Yes describes one session while answering another — exactly Logitech's question.
  Option (b) from the issue was rejected on the evidence already in the code: a decaying pin was
  tried before and "made a selection decay within ~2.5s" (comment at the top of the cascade).
  Windows needs no branch: `_activeTty` is null there, so display falls through to the pin.
  Plus the thing none of the three options offered and QA actually asked for: **pressing the pinned
  slot again releases it.**
  Hardware check owed: QA's reproduction — two sessions, pin one, look at the other, confirm Cost
  follows your eyes while Yes still answers the pin.
- **#28 concurrent voice capture: DONE (code), hardware pass rides with #24.** One `VoiceCaptureState`
  in the engine replaces three private per-key flags. The duplicate helper was the lesser half: because
  each key both started AND routed, the destination was decided by whichever key you pressed *second*
  — dictate a prompt, stop with Go to Project, and your prompt was fuzzy-matched to a project and
  opened. Rule now: **intent is fixed at start; any voice key stops; a press while transcribing is
  refused.** Every exit path clears the state via `finally`, and a capture older than 90s is treated
  as dead — a flag that could not expire would leave the voice keys permanently dead after one
  crashed helper, which is worse than the bug.
- **#26 project roots + leaked build path: DONE, hardware-verified 2026-08-27** — "Life" opened
  `~/Life` from the keypad, a project the hardcoded roots could never reach. **Still unverified on
  hardware: the inferred-roots case** — say "Sailor" (a folder under `~/Work/MyApps` with no `.git`),
  which is the half that would have silently regressed. Discovery replaced the hardcoded `~/Work` pair;
  Release builds set `PathMap` so no DLL names the build machine, gated in `pack-release.sh`.
  **The trap worth keeping:** requiring `.git` on every candidate was a silent regression — 38
  folders under the author's own roots have no `.git` and were matchable in 2.0.1. Hence inferred
  roots (a folder holding 2+ repos is where projects live, and so is one level above it, never
  home). Verified against the real home: 0 lost against the old behaviour, 41 gained, 11 ms.
- **#27 redraw storm: fixed and measured, one number still owed.** Change-driven state event, guarded
  Status/Model repaints, idle backoff (500 ms → 2 s → 5 s, only with no live session). Every fixed CPU
  sample beat every pre-fix sample, but all were taken with a busy session on the machine (the one
  doing the measuring). **The zero-session idle figure — QA's 8.1% — is unmeasured**: close every
  Claude session and run `spikes/redraw-27/measure-27.sh 120` twice, Terminal frontmost and not. Two
  gaps stated in the commit: no "keys off-screen" signal found in the SDK, and QA's duplicated-
  subscription diagnosis is unconfirmed (handlers register once; likely SDK double-render).
- **#24 whisper: code complete, RELEASE STEP OUTSTANDING.** The bundle now carries ggml's compute
  backends and proves it (below). What remains is not code: `sign-and-notarize.sh` has to rebuild the
  helper and re-sign the bundle with the Developer ID, and voice must be pressed on the keypad.
  **Rebuilding the helper resets its Microphone grant** — expect empty transcripts and no re-prompt
  until `tccutil reset Microphone com.rshankar.claudeconsole.voicehelper`.
- **#25 needs a design decision before code.** The Session keys mean "the one I selected", the display
  keys mean "the one I'm looking at", and they cannot share one value. Three options in the issue.
  It is also the second half of the answer promised to Logitech about approval targeting, so it is the
  next thing a reader should pick up.
- **Two behaviours claimed but not proven on hardware**: that pasting a slash command still selects the
  right autocomplete entry (#22 — paste arrives at once where typing filtered progressively), and #20
  on a real package install.
- **No PR.** The base-branch question was never formally settled — see below.

## The base-branch question, and why `main` won

A second review recommended branching from `v2.0.1` rather than `main`, on the grounds that 2.1.0 has
never been hardware-tested. That is a fair objection and it was under-weighted at first.

`main` won on two facts: `v2.0.1` is the **pre-multi-agent tree** (flat `src/`, no `src/Core`,
`src/Products`, `src/Agents`), so every engine fix would have to be written twice and Vizhi for Codex
would get none of them — and **11 of the 14 QA issues are shared-engine defects that the released
1.5.3 already carries**.

The objection is now largely answered in passing: the dev build driven all afternoon *is* 2.1.0 plus
these fixes, and it behaved. That is not a full pass, but it is no longer untested.

## #24, and the trap underneath it

The defect: ggml 0.15 is a **dynamic-backend** build. `libggml.0.dylib` is a 78 KB registry that
does no arithmetic; the CPU/Metal/BLAS engines are separate `.so` files it `dlopen`s at runtime.
`dlopen` leaves nothing in the Mach-O load commands, so `bundle-whisper.sh` — which builds the
payload by walking the link-time closure — could not see them and never shipped one. With no engine
registered, whisper aborts on `GGML_ASSERT(device)` before reading a sample of audio.

**The trap, and it is the whole reason this shipped green: on a dev machine the broken bundle works.**
libggml has Homebrew's `libexec` compiled in as a fallback search path, and that directory exists
here. Every voice test that ever passed on this Mac was borrowing Homebrew's engines — the bundle
has never once loaded its own. So:

- **A transcription smoke test is not enough.** QA's suggested fix (transcribe a fixture WAV) passes
  on this machine with zero backends bundled. It has teeth only with the Homebrew prefix made
  unreachable, which `bundle-whisper.sh` now does with `sandbox-exec`, asserting that a backend was
  loaded **from `$OUT`**. Verified both ways: a backend-less bundle now exits 1, and `--help`
  (the old smoke test) sails through it.
- **Reproduce it with `spikes/whisper-24/repro-24.sh`** (gitignored). It runs the same binary twice,
  once sandboxed, and prints the verdict.
- **A file-existence guard cannot repair a broken install.** `~/.claude/claude-console/` outlives an
  uninstall, so `!Directory.Exists(WhisperBinDir)` meant corrected files would have fixed nobody who
  had ever pressed Voice. `RuntimeTreeMatchesPackage` compares every packaged file by size and
  SHA-256 instead. Same fix applied to the Windows branch, which had the same shape.
- **The `.error` sidecar is a contract between Swift and C#** that neither compiler checks, so
  `VoiceRuntimeInstallTests` reads both source files and pins it. Reintroducing
  `p.standardError = FileHandle.nullDevice` fails the suite — verified by doing it.

## Windows voice: one gap closed, one still open (found while fixing #24)

**Closed.** `tools/windows/ClaudeConsoleVoice/Program.cs` had the identical silent-failure defect to
the macOS helper — every failure path returned `""` and exited 0, with stderr explicitly dropped
(`content irrelevant on success`). It now checks `ExitCode`, keeps whisper's diagnosis, and writes
the same `<transcript>.error` sidecar. The plugin's reader was already platform-neutral, so the
Windows half had been inert. The sidecar must be written BEFORE the transcript: the poll loop checks
for it first, and an empty transcript arriving earlier would be read as silence.

**STILL OPEN — issue #47, a laptop task, not a Mac one.** `pack-release.sh` on this branch packs only the macOS
`whisper-bin`. `EnsureVoiceRuntimeInstalledWindows` looks for `voice/whisper-bin-win/`, which no
package has ever contained, so **voice on a packaged Windows install fails with "whisper-cli.exe
missing"** and has only ever worked where whisper was placed by hand (this laptop).
`docs/windows-debug-handoff.md` flagged it in capitals and it was never done.

`feat/vizhi-desktop` already implements it — `WINDOWS_WHISPER_DIR`, a Windows-side
`TRANSCRIPTION_SMOKE_OK` marker, and a refusal to pack without one. **Cherry-pick that section
rather than rewriting it.** It needs a Windows bundle that has actually transcribed on Windows,
which cannot be produced or proven from the Mac.

Worth knowing: #24's root cause is probably macOS-only. The Windows payload is whisper.cpp's own
prebuilt `whisper-bin-x64.zip`, which ships its ggml backend DLLs beside the exe, rather than being
hand-assembled from Homebrew the way the macOS bundle is. Probably — unverified from here, and
there is no sandbox equivalent for a Windows bundle on this machine.

## #27: measuring a CPU fix from inside the thing being measured

- **The measurer is a session.** Anything Claude Code runs is a live, busy session, so the idle
  backoff never engages during a measurement it takes, and both busy animations run throughout.
  60 s windows swung 6.8%–11.4% on the same build from tool-call noise alone; 120 s windows with
  no tool calls in flight settled to ±0.3. Launch measurements in the background and stay silent.
- **A service restart drops the device to `@_defaultmac`** until the user focuses an app with a
  profile, so a before/after pair taken across a rebuild lands in different display states unless
  you re-focus Terminal first. `measure-27.sh` records the state from `LoupedeckSettings.ini` for
  exactly this reason; the void rows in `results.tsv` are the ones it caught.
- **`lsof` cannot see the plugin DLL** — the host loads assemblies from bytes. A dead-looking
  plugin with a lovely CPU number is the trap; the `osascript` child the poll loop spawns is the
  liveness signal. Sampling children every 500 ms also misses most ~200 ms probes, so don't infer
  cadence from it (a claim made and retracted the same evening).
- **QA's redraw counts come from LoupedeckService's own verbose log** (`SetDisplayImage` lives in
  `LoupedeckService.dll`), which this Mac no longer captures — the Logi launch agents were removed
  during 2.0.0 debugging and the service's stdout goes nowhere. Their headline CPU figure is the
  comparable metric.

## #32: the null-parameter trace, fixed without a reproduction

`PromptCommand` looked its parameter up in a Dictionary three times; `TryGetValue(null)` throws, and
the SDK does call a parameterised command's display-name/image hooks with a NULL parameter in some
Options+ context. QA saw "dozens of ArgumentNullException traces per load" on 2.0.0 and again on
2.0.1. **Not reproduced here**: twelve reloads today with Options+ open on the plugin's action panel,
plus the owner dragging a key, produced zero traces — the trigger is some other Options+ interaction
(the "PluginConfiguration.xml not found" half of QA's line is a service-side note about an OPTIONAL
file no plugin on this Mac ships; it is noise, not the cause). The fix is unambiguous regardless: a
null-safe static `Find` + `NameFor` ("Prompt" when there is nothing to go on), tested directly since
a PluginDynamicCommand cannot be constructed outside the host. `PromptCommand` was the only
dictionary-indexed action; the rest `switch`, which tolerates null.

**Hardware-verified 2026-08-29, and the "not reproduced" above is explained.** A temporary log line
inside `Find` (one INFO per null ask, hook named; reverted afterwards, not committed) showed the host
calling `GetCommandDisplayName` with a NULL parameter **11 times on every load** — build reload and
`loupedeck:plugin/ClaudeConsole/reload` alike — within ~1 ms of `PromptCommand` being registered, on
several threads. `prompts.json` here has 10 entries: one ask per `AddParameter` plus one for the command
itself fits. The pre-fix code did `_prompts.TryGetValue(null)` at exactly that site, which throws
unconditionally — QA's "dozens of traces per load in `GetCommandDisplayName`". So the traces were
happening on this Mac all along; they never reached the PLUGIN log — presumably the SDK catches a hook's exception and reports it in
LoupedeckService's own log, which this Mac does not capture (see #27); where they went was NOT observed
directly, only that the plugin log stayed clean before and after. The
fixed build answers all 11 without throwing; zero exceptions in the plugin log across three reloads.
Also confirmed while re-checking: `PluginConfiguration.xml` is a real SDK mechanism, not a phantom — an
EMBEDDED resource `<Namespace>.PluginConfiguration.xml` (+ `…2.xml`: plugin_info and a default
round-page layout) loaded by `TryLoadPluginConfiguration2`; Logitech's DefaultMac and Spotify embed
both, Zoom embeds neither. v1 is a static declaration of actions with displayNames; a dynamic-actions
plugin (us, Zoom) has no use for it, so the "file not found" line is benign for us — but the reply to
Logitech should say that, not call the file fictional. And `GitCommand` returns null (not "Git") for
a null parameter — no throw (`KeyImage.Render` does `label ?? ""`), just inconsistent with "Prompt".

## #29: a session the keys cannot reach no longer takes a key

`AgentProcessWatcher.Discover` walks each candidate's parent chain in the SAME `ps` listing (no
extra process) to the first `.app` binary — `claude → zsh → login → Terminal.app` on this Mac — and
`MacPlatformBridge.DrivableOwner` decides. Today that is Terminal.app only, because every action key
finds a tab by TTY through Terminal's AppleScript. Skipped sessions are logged ONCE each with the
owning app's name; a chain that reaches launchd without an app (tmux, screen, ssh) is skipped too, and
said so — Terminal's `tty of tab` is the outer pty, not the one claude has, so the keys could not
find it anyway. Windows is untouched (it never used this path). `TtysFrom` keeps its old meaning.

**Design decision, made with the owner:** not "all terminals". iTerm2 is feasible (AppleScript
exposes `tty` per session) and is the next-release driver — the predicate is the seam, and a second
driver is a second predicate plus its own focus/type AppleScript. Ghostty, Warp, VS Code and cmux
expose nothing addressable by TTY; a session in them can only ever be a key that does nothing, which
is the bug. The README and listing now say "sessions in other terminals are not shown".

**Verified on the device 2026-08-28 22:22** — the owner ran claude in iTerm: no Session key for it,
the Terminal.app sessions kept theirs, and the plugin log carried the one WARN line naming the app.

## #31: the settings.json edit is now reversible, disclosed, and backed up honestly

QA's three complaints and the comment's fourth, and what each got:

- **Backup goes stale** → the backup is ROLLING: `WriteSettings` copies `settings.json` to
  `settings.json.claude-console.bak` immediately before EVERY write (overwrite), so it is always the
  state one change ago. The old "once, on first load" snapshot was a month stale on QA's machine and
  on this one (26 June vs 28 August) — restoring it would have rolled back everything since. The README
  now says plainly it is not a pre-install snapshot.
- **No way to undo / uninstall leaves the hooks** → `BridgeWiring.Unwire(root, chained)` — pure,
  surgical, unit-tested: removes only hook entries whose command is ours (same `IsOurs`/`IsOurHook`
  markers as wiring), collapses only containers it emptied, restores a chained status line from the
  chain file or removes ours if nothing was chained. The same rule is implemented in python inside
  `uninstall.sh` (the plugin is gone by the time it runs), which now unwires BEFORE deleting the
  runtime home (the chain file lives there) and reports it in `--dry-run`. Shell tests pin the script.
- **Hidden opt-out** → it is now a two-way switch: with `no-autowire` present the plugin UNWIRES on
  load if it finds its entries. `uninstall.sh --unwire` = remove the wiring + set the opt-out, and
  the README's bridge section leads with "this edits your settings" and how to reverse it.
- **"Prompt before modifying"** → the platform's nearest thing, done: `Plugin.OnPluginStatusChanged`
  (reflected from PluginApi.dll — Spotify and Zoom both use it) posts to Options+'s message centre with
  a link. The engine composes it (`BridgeNotice`, pure, tested) and fires `BridgeManager.Notify` on the
  load that WRITES the wiring (Warning → the "!" badge), on an opt-out unwire (Normal + message), and
  clears to Normal on any load that changes nothing. A modal dialog is not available; true consent
  would be opt-IN via a key, which ends zero-setup — still the PM's call, but "no prompt at all" is no
  longer the honest description. **An earlier note here said the plugin could only show key faces and
  a beep. That was wrong; the owner asked "are you sure?" and reflection said no.**
  **Seen on the device 2026-08-28 18:05 (owner's screenshot):** the "!" on ALL ACTIONS, the full
  message as an amber card under "Claude Console Actions", and the button "WHAT WAS CHANGED, AND HOW
  TO UNDO IT". Clears on the next no-change load.
  Unknown still: whether Options+ renders a plugin SETTINGS page (`PluginSettingRequest` exists in
  the SDK; no shipped plugin uses it). If it does, the opt-out belongs there as a toggle.

**Verified on this Mac 2026-08-28 17:40, on the owner's real settings.json** (5 KB, 12 unrelated
top-level keys, backup dated 26 June — the exact stale-snapshot case QA described):
`uninstall.sh --unwire` removed the 5 hooks + statusLine and NOTHING else — the "before" snapshot with
our entries stripped was byte-for-byte equal to the result — and the fresh backup held the
pre-unwire state. Then the two-way switch via `open loupedeck:plugin/ClaudeConsole/reload`: opt-out
removed → wired (6 refs); opt-out created → the plugin unwired ITSELF (0 refs, log "removed our
statusLine + hooks"); removed again → wired. Final file semantically identical to the snapshot.
Note: the script's backup is `copy2`, so its mtime is the source's last-write time, not "now" —
correct for a backup, but do not read that date as when the backup was taken.
Still owed: the "keys go dark in a new session" observation (left WIRED so the owner's keys keep
working). Codex (Vizhi) is a separate issue: its hooks are trusted by hash, so the same edit has a
user-visible cost there.

**2026-08-29 — the settings-UI spike ran, on the device, and the answer changes the plan.** QA's third
ask ("opt-out as a normal plugin setting") was blocked on "does the SDK have a settings UI at all?".
Reflection over `PluginApi.dll` + the docs' *Managing Plugin Settings* page: `Set/TryGet/Delete/List
PluginSetting` is an ENCRYPTED KEY/VALUE STORE (optionally cloud-backed), no UI; `PluginSettingRequest`
is the plugin→service storage channel (`Get/Set/Delete/List`), not a UI event; `PluginPreferenceType`
is `{None, Account}` — the ONLY plugin-level control Options+ renders is a sign-in/sign-out button.
Two probes were built into the dev plugin (stripped afterwards, never committed) and the owner
clicked through them while the log was watched:

- **`PluginPreferenceAccount` relabelled Enable/Disable: renders, both buttons fire
  (`LoginRequested`/`LogoutRequested`), state persists in `PluginSettings/ClaudeConsole.dat` across
  reloads — and it is UNUSABLE as a switch: while the preference is "signed out" (IsValid=false,
  even with IsRequired=false) Options+ stamps an amber "!" on EVERY key of the plugin (owner's
  11:45 screenshot); Enable cleared them (11:47). "Disabled" would look like an error forever.**
- **Action Editor (`ActionEditorCommand`): the real answer, with limits.** The panel shows the
  action's one-line Description, a Checkbox (label CLIPS — keep it short), and Buttons that fire
  `ControlValueChanged` to the plugin WITHOUT a keypress; the checkbox value persists with the key
  and arrives in `RunCommand` (`consent=True`). `Label` and `Hyperlink` render as hover-only ⓘ
  icons — no readable explanation, no visible link. Appears only when an assigned action's editor
  is open. `INativeGui.OpenConfigurationWindow()` opens Options+ itself (not a settings page).

**Decision (owner + second reviewer, 2026-08-29): go OPT-IN.** No settings.json write on load.
Two actions, "Enable Live Status" / "Disable Live Status" (group *Setup & Privacy*; separate so
Disable can never enable), each editor = one sentence of Description + its one button; the key
press does the same verb. No consent checkbox (the press/click IS the consent; the label clips).
The message-centre card moves from "on load" to "on Enable" and carries the README link the editor
cannot. Live keys show "Setup required" until enabled; a press on one flashes "Add the Enable Live
Status key" (the #18 face pattern) and never enables. State = our hooks present in settings.json
(existing installs land Enabled, no card, no re-consent); `no-autowire` stays one release as a
never-auto-wire guard. The layout download places the two keys. Codex gains most: its hash-trusted
hooks stop re-prompting on every update.

**BUILT 2026-08-29 (six commits after `79a1bf8`), suite 843 C# + 33 + 27 green, HARDWARE PASS PENDING.**
What landed, in order: the settings.json seam + one write door (`BridgeManager.HomeOverride`,
`RewriteSettings` — fingerprinted read, refused write on a moved file, unique temp, rolling backup;
`tests/TempHome.cs`; a settings.json canary in `run-all.sh`) · the hook table + detector
(`BridgeWiring.HookSpecs` / `Inspect` / `DisplayState`, `LiveStatusFace`) · the engine flip (load
installs scripts and READS; `EnableLiveStatus` / `DisableLiveStatus` write; `OnLiveStatusChanged`;
external edits picked up by a ~10 s stat) · the two actions + `LiveStatusGate` on Cost/Context/
Activity + `SettingsFileWiring` capability (Claude true, Codex false) + `setup`/`off` icons · the
profiles (Enable at page 2 key 9, Disable at page 4 key 9; Codex drops both) · the docs.
**Traps met:** a parameterless `base(displayName…)` command registers unconditionally — gating needs
`base()` + `AddParameter`, so the bindings are `…LiveStatusCommand___enable/___disable`; the
Codex profile tool strips BEFORE it rearranges, so its `PLACE[(1,8)]` must keep expecting `None`;
xUnit theories need public enum parameters, so `LiveStatusWiring`/`LiveStatusState` are public.
**Device findings 2026-08-29 13:08–13:12:** `uninstall.sh --unwire` reached the keys in ~10 s (`Off`); a
fresh-state load wrote nothing; a live key press before setup wrote nothing (cksum unchanged); Enable
wrote exactly the five events + our statusLine with 12 unrelated keys intact, backup = the pre-Enable
file, no temp; `JustEnabled` → `Enabled` in 1.2 s because this Mac's running session still reports.
**`PluginStatus.Normal` + message renders NOTHING** — every card is now posted at Warning (the level
that renders and badges the All Actions tile) and every load clears it.
**Owner's call 2026-08-29 13:20 — the two-step press.** Dragging a key just to give permission was the
friction the owner refused; the reviewer's objection (a face cannot carry the disclosure) is answered
by the card, which does render at Warning. So a live key pressed once ARMS itself for 15 s, flashes
`Press again`, and posts `BridgeNotice.PressAgain(key, 10)` — the prompt with the change in it; a
second press on the SAME key inside the window runs `EnableLiveStatus`; a late press only arms again.
The Enable/Disable keys stay for the layout and for anyone who prefers a labelled key.
**Still owed on the device:** the Warning cards actually appearing (Disable, Enable, Press again);
the `Restart Claude` label on 60/90 px keys; a #27 repaint check.

## #23: universal plugin — what changed, what it removed, what is unverified

Logitech's PM decided it by email on 2026-08-28: no app binding, no packaged profile, users drag
actions onto their own Terminal profile; the redesigned layout is delivered separately as a download.

**Removed** (11 files): `SelfRegistration.cs`, `RegistrationHeal.cs`, `RegistrationCleanup.cs`, both
`package/profiles/DefaultProfile70.lp5`, `uninstall-registration.sh`, `repair-registration.sh`, and
four test files (44 tests, all pinning behaviour that no longer exists). Both yamls:
`HasApplication` → `HasNoApplication`. Both `Load()`s lose their registration block. The plugin class
had declared `HasNoApplication => true` since the first commit — the yaml capability is what the
service acts on.

**THE TRAP, and it cost the afternoon: the `ClientApplication` subclasses must EXIST.** The first
cut deleted `ClaudeConsoleApplication.cs`/`VizhiCodexApplication.cs` with the binding they carried,
and the service refused the assembly: `Cannot load plugin from …ClaudeConsolePlugin.dll` then
`added to disabled plugins list` — no crash marker, no reason in any log (the service's stdout goes
nowhere on this Mac and the literal is in no readable binary), while the same DLL loaded in a plain
.NET host with all 108 types. What found it: E1 (empty capability list) still failed; E3 (the
previous commit rebuilt into the same link) LOADED; probing `SpotifyPlugin.dll` — QA's own universal
example — showed a `SpotifyApplication : ClientApplication` that overrides nothing. Both classes are
back as empty subclasses; `UniversalPluginTests` pins that they exist and override none of
`GetProcessName`/`GetBundleName`/`GetApplicationStatus` (overriding with "" under HasApplication was
the 1.5-era crash; under HasNoApplication the base's "" is correct). Side effect of E3 to know about:
the old code re-wrote `@_claudeconsole` on load; it was removed again with the service stopped.

**The downloads are the product now — and the first cut shipped the WRONG one.**
`profiles/ClaudeConsole-Keypad.lp5` was already the universal SHAPE (bound to `com.apple.terminal`,
`hasNativePlugin: false`) but it carried the pre-1.7.1 LAYOUT: page 1 was `Context | Status | Plan …`,
no Session keys. The owner imported it and saw the old keypad. The current layout (Sessions ×3 /
Clear / Voice / Esc / Yes / No / Tab, pages 2–5 unchanged) lived only in the packaged
`DefaultProfile70.lp5` that was deleted. It was recovered from git (`4fb3554`) and converted: Terminal's
own `ApplicationInfo`, `ProfileInfo` rebound to `com.apple.terminal` with `ClaudeConsole` in
`additionalNativePluginNames`, GUID kept at `4146981D…` (the 2.0.x identity; the old download's
`B399E0AB…` is what a stale import on this Mac carries — delete that one in Options+). **From here the
download is canonical**: change the layout in Options+, export, replace the file, rerun
`tools/make-codex-profile.py` and `tools/windows/make-windows-profile.sh`. Both derive from it and
keep Terminal's binding, rewriting only the plugin prefix, the plugin list, the GUID and the dropped
keys; the Codex `PLACE` table was written against exactly this layout and asserts it.

**What this made moot:** #34, #45's orphan, the reinstall icon loss, the `IsApplicationActive` spike,
the "install one console per machine" warning (they can coexist now), `dev-reload.sh`'s service
restart (a reload loses nothing), and the whole `@_` clause of CLAUDE.md's namespace rule.

**Verified on this Mac 2026-08-28 17:05:** package uninstalled, `@_claudeconsole` removed with the
service stopped, universal build loaded from the dev link, and 20 s later still no registration on
disk — the build writes nothing. Keypad on `@_defaultmac`, as it should be before an import.

**Import verified on this Mac 2026-08-28 ~17:15:** the owner deleted the stale old-layout import,
imported the regenerated `profiles/ClaudeConsole-Keypad.lp5`, and page 1 showed the Session keys with
Terminal frontmost. The import created Options+'s own `com.apple.terminal` entry (this Mac never had
one) with "Claude Console — Keypad" as its profile — the exact shape a Marketplace user ends up with.

**Unverified, in order of risk:**
2. **`ClaudeConsole-Windows.lp5`** — the entry name `windowsterminal` and the default plugin
   `DefaultWin` follow the mac pattern by analogy; one import on the laptop confirms or corrects.
3. **Two products together** — claimed possible, never tried since the change.
4. **Vizhi Desktop** (`feat/vizhi-desktop`) still uses `SelfRegistration` and binds the Claude
   Desktop APP, where a binding may be the point. The PM's decision covers the terminal plugins;
   Desktop needs its own answer before that branch merges onto this.

**The onboarding cost is real**: a fresh install shows nothing until the user imports or drags. The
README, the listing copy and the clean-install doc now lead with that. If QA files "installed and
nothing happened", the answer is the first line of the install section.

## The 2.2.0 voice release round, and what it confirmed on the way

- **The signing script's last check false-failed** on `libggml-blas.so` after every artifact was
  correct (both notarizations Accepted, helper stapled, every file Developer-ID and valid by hand).
  Cause unconfirmed — a pipefail + `grep -q` race fits but did not reproduce in 30 tries. The check now
  captures before grepping and says WHICH half failed (`4fb3554`). Do not re-run the whole round on
  that error: verify by hand, as done here.
- **Package install over the dev registration reproduced the reinstall symptom exactly**: 2.2.0 loaded
  from the package, `@_claudeconsole` on disk, live list without it, no profile in Options+. The
  shipped `~/.claude/claude-console/scripts/repair-registration.sh` fixed it end to end, including
  starting the service itself — the #45 deliverable, exercised as a user would. This is what QA will see
  installing 2.2.0 over 2.0.1; the universal change (next) removes the failure entirely.
- `logiplugintool` still needs `DOTNET_ROLL_FORWARD=LatestMajor`. `pack-release.sh` removes the dev
  `.link`; restart the service BEFORE the Options+ install click or the package double-loads.
- **Machine state after this round: the PACKAGE is installed and the dev `.link` is gone.** A bare
  `dotnet build` (or `dev-reload.sh`) would write the link back and double-load. To return to the dev
  loop, uninstall Claude Console in Options+ first.

## #18: a failed dictation now says so — reproduced on the device first

Reproduced 2026-08-28 with `tccutil reset Microphone com.rshankar.claudeconsole.voicehelper` and
**Don't Allow** on the prompt. What the log showed, against the handoff's claim that #24 had covered it:

| | |
|---|---|
| 14:08:54 | helper launched; denied; exited with status 2 having written **nothing** |
| 14:09:07 | the STOP press was refused — "a Send transcript is still in flight" — so the key was dead for 20s |
| 14:09:14 | `transcript not produced within 20s` — the only evidence, 20s later |
| 14:09:16 → 14:09:46 | third press: no prompt (TCC remembers the denial), instant silent failure, same 20s |

`/tmp/claude-console/voice/` held one empty `stop` flag. No transcript, no `.error` sidecar: the
denial path ran BEFORE the sidecar writer #24 added was defined. And the helper's own
"microphone permission DENIED" went to **stderr of a process launched detached via `open`** — nowhere.
The one program that knew why it failed said so to nobody.

- **Swift:** `fail()` and `surfacedErrorPath` now sit above the permission check; denial and both
  recorder failures write the sidecar. No raw `exit(2)`/`exit(3)` remains (pinned by test). A stale
  sidecar is cleared at start alongside the stale transcript.
- **C#:** one `ReportVoiceFailure(intent, keyText, detail)` — log, beep, then `OnVoiceFailed`. Reached
  by every way a wait can end without text: sidecar (mapped by `VoiceFailure.FromSidecar`), noise-only,
  empty, timeout — plus the two START failures (helper missing, model downloading). The intent is read
  from `Voice.Intent` before anything can `Finish()` it, so the face lands on the key that was pressed.
- **Keys:** each voice key owns a `FailureFace` (companion to `ListeningFace`): red, the key's own icon,
  two words chosen by what the user should DO — "Mic denied", "No speech", "Model loading",
  "No helper", "No response" — held 2.5s, then gone. Listening wins over a stale failure.
- **Not changed:** the helper still logs to stderr. Its sidecar is now the record; a log file would be
  a separate, small change.
- **The 20s dead-key window is NOT fixed by this.** With a sidecar the wait ends in ~1s, so the window
  closes for the denied case in practice. A helper that dies without writing still costs 20s.

**Hardware-verified 2026-08-28 14:42, denied path:** Voice → Don't Allow → stop press at 14:42:32.531,
`voice Mic denied — microphone permission denied…` logged at 14:42:32.691. **160 ms**, against 20 s of
nothing the same morning. Owner confirmed the red "Mic denied" face on the Voice key. Still
owed on the device: "No speech" (record silence) and the post-Allow round-trip, and the RELEASE half —
the shipped helper must be rebuilt by `sign-and-notarize.sh` (Developer-ID, stable hash) and packed;
this verification was against an ad-hoc dev build of the helper, which is why `tccutil reset` was
needed around it. Rides with the #24/#28 pass.

**Bonus from the repro: #46's slow line fired for real.** `osascript took 1271ms of its 2000ms budget`
at 14:09:07 — during the TCC permission dialog. A system modal inflates the frontmost probe ~9x. That
is the first observed member of the class of event that pushes it toward the budget, which the four
load experiments could not produce. Still not an overrun; still consistent with "rare stall, not creep".

## #30: two thresholds for one question, and the one in the report was never consulted

The issue's comment asked why the existing 45s `StalledBusyAfter` never resolved the stuck hourglass,
and guessed the state file was being rewritten. **Neither was true.** There were TWO thresholds:
`SessionRegistry` expired busy after 45s (the session-slot keys) while `BridgeManager` used a bare
300s literal (the Status key — the one in the report). QA's stuck session sat at 114s: past the first,
nowhere near the second. The refresh theory is ruled out by QA's own 114s-old state write.

Both now ask one rule, `ActivityStall`, so the two keys cannot disagree about a session again.

- **Age alone must not decide it.** A slow tool call and a dead turn are identical by age. The
  transcript is the discriminator: Claude appends to it all turn and it stops when the turn dies.
  Device numbers: busy 1s, stuck 113s. Live here: ~9s lag while working. Window: **90s**.
- **`transcript_path` needed no script change** — the statusline handler writes Claude's payload
  verbatim, so it was on disk all along. It reaches Core through `AgentSessionState`, not by parsing
  Claude's JSON in the engine; the grid carries it so nothing re-reads a file the poll loop just parsed.
- **The Status key reads the ROUTING session's transcript, not `CurrentState`'s.** With a pin set
  those are different sessions (#25). Pairing one session's activity with another's transcript would
  decide the hourglass from a tab nobody asked about.
- **The 300s no-transcript fallback is BridgeManager's own number.** A 10-minute first attempt was
  rejected by `A_session_stuck_on_busy_settles_back_to_ready` (600s must settle). That test's intent
  is sound and it passes unmodified. In practice the path is near-unreachable: Claude always ships a
  transcript, and Codex reports its own activity so it never enters `ReadActivityState`.
- **Keypad Esc clears in ~5s, not 90.** Reported from the device the same hour: the transcript rule
  was working (73s into the window) but making someone watch an hourglass over a turn the plugin
  ended ITSELF is the same complaint with a timer on it. `ControlCommand`'s Esc case records the
  interrupt against the routing session. Deliberately not inside `InjectKey`: `AnswerCommand` also
  sends Escape, to REJECT a tool, after which the turn continues.
- **Review found a real bug in that hint (fixed as `a7c4b51`).** The map is keyed by tty and macOS
  recycles tty names. An Escape recorded against a tab's previous occupant survived into a new
  session; on the no-transcript path it read as idle on every poll. The hint must now be at least as
  new as the busy write it claims to end — which also expires it naturally when Esc only dismissed a
  menu, since `PostToolUse` rewrites busy with a newer stamp. Reaping drops the entry too.
- **Ready, not the bell.** The bell means a tool is blocked on approval; an interrupted turn is idle.
  Badging idle sessions amber is what #51 just removed.

## The #20 trade: the reinstall story is now worse, and the README lied about it

> **SUPERSEDED the same day by #23** — there is no registration to lose any more. Kept because the
> reasoning is the evidence for why universal was the right call, and because the package-install
> reproduction below is exactly what a 2.2.0-over-2.0.1 QA install WOULD have shown.

Found because the icon vanished twice after rebuilds on 2026-08-28. `dotnet build` sends a plugin
RELOAD; the service's live application list loses `@_claudeconsole`; the disk entry is still valid so
`RegisterIfMissing` finds nothing missing; the list is only rebuilt from disk **at service startup**.
`RegistrationHeal` exists for this and schedules a restart — but on a machine with no Logi launch
agents nothing brings the service back, so the heal takes the keypad down and leaves it there.

**It matters for Marketplace because #20 removed the automatic restart for packages** (correctly: the
installer writes the registration before it finishes copying the payload, so the timestamp check fires
on every healthy install). Consequence: **install-over-existing — QA's retest path — now leaves the
icon missing until the service restarts**, and `README.md` promised "1.8.9 and later heal this
automatically". Corrected in `a84336d`. A first install on a clean machine is unaffected (the entry is
genuinely absent, so the plugin writes it and restarts once). The built package contains only `bin`,
`metadata`, `profiles` — **`repair-registration.sh` does not reach users** (#45), which is now
load-bearing rather than cosmetic. `repair-registration.sh` itself used to exit with an error if the
service did not return on its own; it now starts it.

Nothing here hard-blocks a submission (PluginApi not bundled, versions agree, no new data access — the
transcript is `stat()`ed for mtime, never read). Routes, in order:
1. **DONE — the recovery scripts reach users (#45).** Not via the package: the package directory is
   DELETED on uninstall, so a script shipped there is gone at the exact moment the orphan sweep is
   needed. They are embedded in the DLL and extracted to `~/.claude/claude-console/scripts/` on every
   load — the same route as the bridge scripts, but deliberately BEFORE the bridge opt-out check, so
   declining settings.json wiring cannot cost anyone the uninstall remedy (pinned by
   `RecoveryScriptsTests`). `uninstall.sh` now runs the orphan sweep itself, before deleting the home
   the sweep lives in (also pinned). `repair-registration.sh` takes the registration name so Vizhi can
   use it. README and SUBMISSION.md point at the installed paths. **Vizhi does not embed them yet** —
   `uninstall-registration.sh` is product-neutral and its csproj needs the same three resources plus
   a call into whatever its runtime home is; not done here because that home's layout was not checked.
   **Clean-machine verification owed**: install, confirm the three files in the runtime home, uninstall
   in Options+, run `uninstall.sh --dry-run`, confirm it reports the orphan. Note the dev registration
   on THIS Mac carries no `selfRegisteredBy` stamp, so the sweep correctly ignores it here — that is
   why a dry run on this machine says "no orphaned registrations", not evidence the sweep is inert.
2. **Replace the timestamp guess with a real signal.** `PluginApi.dll` exports `IsApplicationActive`,
   `get_ApplicationActive`, `get_ApplicationRunning` — the plugin uses none of them. Only the names are
   confirmed; semantics need a spike before any heal is built on them.
3. **A universal plugin (#23) has no application registration to lose** — this whole failure class
   disappears. That is a reliability argument for QA's proposal, and belongs in the reply owed to Logitech.

## #39: cherry-picked from `feat/vizhi-desktop`, and reproduced afterwards

`6726b51` picked clean as `e5e882e` — one file, `tools/convert-designer-icons.swift`, and the bug was
present here too (`src/Core/Resources/icons` exists on this branch, `src/Resources/icons` does not).

**Reproduced after the fact, which is the wrong order but worth having done.** The pre-fix script run
against today's layout prints `OK(44)` / `FAIL(0)` / exit 0 and writes **zero files**. That single line
of output is the whole defect: the wrong output path was invisible because `try? png.write(...)` was
followed by an unconditional `return true`. Both runs used a throwaway root with `assets/` symlinked
in — never regenerate into the working tree to test this, as the real folder holds 62 committed icons
and the converter owns only 44 of them.

| | pre-fix | post-fix |
|---|---|---|
| output dir missing | `OK(44)`, exit 0, 0 files | names the directory, exit 1 |
| output dir present | writes to the old `src/Resources/icons` | 44 PNGs in `src/Core/Resources/icons`, exit 0 |

**Nothing shipped was ever wrong** — the committed PNGs predate the refactor. What was broken is
REGENERATING them, which is exactly what the new designer pack invites.

**The new pack does not feed this pipeline, and that is the next surprise.** `~/Downloads/icons/Icons`
(delivered 2026-08-27) is **38 PNGs at 192px, pure white** — but the converter consumes **SVG**, and not
incidentally: `gauge_warn`/`gauge_crit` and `brain_haiku`/`sonnet`/`opus` are produced by swapping the
fill hex IN THE SVG, so the glyph stays single-source. A flat PNG cannot do that. The names are also a
different language — roughly four overlap; the pack adds `allow`, `alwaysallow`, `deny`, `needsinput`,
`agent`, `skill`, `usage`, `fork` and drops `chevron-small-down/up`, `gitcommit`, `gitpush`,
`nexttab(right)`, `voicedictation`, `smartactions`. 38 glyphs against 62 embedded icons. Ingesting it
needs a name→key map and preferably the SVGs, which is **#41** (blocked on Logitech), and the four
approval glyphs are aimed at **#40/#43**, also blocked.

## #46: measure-before-coding paid, and then disproved everything

**The instruction in this file was to measure first, and it half-closed the issue on its own.** The
`ps` half is gone: #46's evidence was 7 x `/bin/ps` + 3 x `osascript` in 53 minutes; the same log on
the post-#27 dev build showed **0 x ps + 3 x osascript in 43 minutes**. #27's adaptive cadence cured
the `ps` overruns and left the frontmost probe untouched. Per-executable counters are now in the
overrun message precisely so that split stays visible — one shared counter would have hidden it.

**Then every candidate cause failed under measurement** (`spikes/subproc-46/`, gitignored):

| Hypothesis | Result |
|---|---|
| The 2000ms budget is too tight | **No.** p50 = 140ms, p99 = 166ms. 13x headroom. |
| Terminal's renderer blocks the Apple Event (the issue's own suspect) | **No.** Heavy output in a second window: 138ms idle vs 135ms busy. **1.0x.** |
| The machine is CPU-starved | **No.** 16 spinners on 8 cores: p50 213ms, worst 515ms — 26% of budget. |
| Apple Events serialise per target app | Real but mild. 16 concurrent probes: worst 596ms. |

Nothing reachable by load gets within 1400ms of the budget, yet overruns happen ~3/hour. **So this is
a rare episodic stall, not degradation** — which is why the fix is instrumentation and backoff, and
explicitly **not** a bigger budget. If a future reader is tempted to widen it, those numbers are the
thing to re-read first; they are recorded in `BoundedProcess`'s class comment for that reason.

- **The old message named three causes and all three were false**, which is worse than saying less —
  it sends the next reader after the wrong thing. Replaced by two facts: the overrun COUNT for that
  executable since load (rate is the real question), and, for calls that survive, the real duration
  whenever one eats half its budget. That is the cliff-or-creep test the issue asked for.
- **Long budgets are exempt from the slow line.** `screencapture -i` waits on a human dragging a
  selection and the whisper helper transcribes audio: both are MEANT to spend most of their budget,
  and warning about them would bury the poll-path calls the line exists to surface.
- **Backoff is bounded, and the bound is the point.** While skipping, the plugin holds a stale idea of
  which tab is frontmost — the very harm #46 reports. An uncapped exponential would turn a momentary
  stall into minutes of it, so it skips 1, 2, then 4 and clears on the first good answer.
- **An empty probe result is not a failure.** It means Terminal isn't frontmost, which is routine.
  Only a real overrun backs off — and separating the two needed the timeout signal plumbed out of
  `BoundedProcess`, because both previously arrived as `null`.

**NOT hardware-verified, and it cannot be today.** Normal is 140ms and the slow line fires at 1000ms,
so neither new message appears on a healthy machine — confirming them means catching a real stall,
which happens ~3 times an hour at random. The dev build has not been rebuilt with this change either
(see the `-t:Compile` trap in CLAUDE.md before doing so). What IS verified: 748 C# + 47 shell green,
and the measurements above, taken on this Mac with the plugin running.

## #48: hardware testing found what the suite could not

**Fixed and hardware-verified 2026-08-27**: after the fix, the same key correctly picked `~/Life`.

Testing #26 by voice from across the room, whisper emitted `(gunshot)` and `(static)` — its labels
for a noise — and the plugin fuzzy-matched them to SafeShot and StatementSense and **opened both**.
A failed dictation did the wrong thing rather than nothing.

- The two helpers disagreed and macOS was wrong: Windows stripped bracketed runs, macOS matched
  three exact literals (`[BLANK_AUDIO]`, `(silence)`, `[ Silence ]`) and let everything else pass.
- Fixed in the ENGINE, not the helper — one rule for both platforms and every key that consumes a
  transcript, and it avoids re-signing ClaudeVoiceHelper, which would reset its Microphone grant.
- The Voice/Voice Draft keys had the same exposure: `(gunshot)` would have been typed into a session.
- Fuzzy threshold raised from a 4- to a 5-character overlap. Four let `gunshot`→`SafeShot` through on
  "shot" and `static`→`StatementSense` on "stat".
- **The lesson: this was invisible to 669 green tests.** It needed a real microphone, a real room,
  and standing too far away. Test voice features at the distance a user actually sits.

## #49: the plugin was deleting its own memory

Reported from the device: an idle session's keys start showing ANOTHER session's cost, and its label
changes to "Claude Code". Both from one cause — `PruneStaleIpcFiles` deleted any state file older
than 10 minutes with no liveness check, and a session's file is only rewritten when it does
something. Lose the file and the display falls back to `shared.json` (the last writer), while the
grid recreates the session as provisional with no project name.

- **The age-based prune was redundant as well as wrong.** `Refresh` already reaps closed tabs from
  `ps` and calls `ReapFiles(dead)` within ~2s. Exempting live sessions therefore cannot leak files:
  a closed tab is cleaned on the authoritative path, not by mtime.
- **The second half is CLAUDE.md's own law.** "A key must never show a value the agent did not
  report" — so `PerTty` returns null for a KNOWN session with no file, the engine raises
  `OnStateUnavailable` once (not per poll — #27), and the display keys show a dash. `shared.json`
  survives only for the genuinely-unknown target, which is the single-session path.
- Cost: a brand-new session shows dashes until its first turn instead of a plausible wrong number.

## #50: read the class comment before "fixing" the slot grid

Observed on the device: with three Session keys placed and four sessions running, exiting one leaves
a blank key while the fourth session stays in slot 4, unreachable. Upward compaction was proposed,
agreed, implemented — and it broke two existing tests, one of them commented **"THE important one"**:

    Assert.Null(registry.SlotSession(2));                   // freed key stays empty
    Assert.Equal("gamma", registry.SlotSession(3).Project); // gamma did NOT slide down

Stable slots are a documented guarantee in `SessionRegistry`'s own class comment, ported deliberately
from Vizhi, and they apply to visible keys too — shuffling a project out from under your fingers is
worse than leaving a gap. A freed key is meant to be filled by the NEXT NEW session, which is the
mechanism the companion test pins. Reverted; closed as wontfix with the reasoning on the issue.

**Resolution taken (a): it is a layout matter.** Six slots and six Session actions exist; the shipped
profile places three. Run more than three sessions and you place more keys. Documented in the README.

The alternative, if it ever comes up repeatedly: make `SlotCount` match the number of keys placed, so
a fourth session stays *unslotted* and the existing "newcomers fill the lowest free slot" path fills
the gap — no shuffling, no new mechanism. Needs a setting, since the engine cannot see the profile.

## #51: the amber badge meant "some session might want something"

From a photo of the device: three sessions, all three keys badged amber, Yes/No both lit. Not a
spill — each key reads its own session. The cause is that Claude Code's `Notification` hook fires for
an IDLE PROMPT as well as an approval, the activity hook maps it to `waiting`, and
`ApplyPendingApproval` badged any waiting session even with no payload.

- **The discriminator existed all along**: `permission` mode writes the pending payload AND the
  state; `Notification` writes only the state. No payload = Claude wants input, not a blocked tool.
- **A timeout on `waiting` is the wrong fix** (and was rejected): a real approval can sit for hours.
  Note `busy` does expire after 45s — the asymmetry is deliberate now, not an oversight.
- The old behaviour was pinned by a test whose comment justified it ("older Claude Code has no
  PermissionRequest hook"). Rewritten to the new intent, with the accepted cost in the test body:
  on a Claude Code too old for the hook, a real approval shows the waiting face without the badge.
  The plugin wires that hook itself, so this only affects hand-configured installs.

## How this session worked, and what it cost

Four of the nine fixes came from **using the plugin on real hardware**, not from QA's report: #48,
#49, #51 and the (rejected) #50. None was visible to a green suite. Voice needed a real room and
standing too far from the mic; the badge inflation needed three sessions left idle; the state loss
needed ten minutes of not typing. Budget for driving the device, not just running tests.

**Twice the existing tests out-argued a proposed fix, and both times the test was right:**

- **#50 slot compaction** — implemented, then broke a test commented "THE important one". Stable
  slots are a documented guarantee in `SessionRegistry`'s class comment. Reverted.
- **#51 badge rule** — the old behaviour was pinned by a test whose comment justified it. Here the
  reversal WAS correct (hardware evidence, owner's call, trade-off stated up front), so the test was
  rewritten to the new intent rather than deleted, with the accepted cost recorded in its body.

The lesson for both: **read the class comment and the existing test before changing a behaviour that
looks wrong.** If a test asserts the opposite of your fix, find out why it was written before you
decide which of you is mistaken.

**Scripts left behind** (gitignored, recreate from the docs if lost):
`tools/dev-reload.sh` is IN the repo (build + real service restart + Options+ restart), as are the
`spikes/subproc-46` findings in `BoundedProcess`'s class comment. Gitignored:
`spikes/whisper-24/repro-24.sh` (proves #24 both ways, sandboxing Homebrew away),
`spikes/redraw-27/measure-27.sh` + `results.tsv` (CPU baselines, with the state check that catches a
run taken in the wrong display state), and `spikes/subproc-46/` — `measure-46.py` (probe duration
distribution), `stress-46.py` (opens a heavy Terminal window and closes it again), `cpu-46.py`
(saturates every core). All three print a verdict line. Time them in ONE process: an earlier shell
version called `python3 -c perf_counter()` twice per sample, and perf_counter's origin is per
process, so it produced negative durations that looked like a working measurement.

## Traps and findings worth not rediscovering

**Never run `tests/run-all.sh` in the same shell invocation as a heredoc.** The bridge-script tests
read JSON from stdin; a preceding `python3 - <<'PY'` consumes it, and the suite silently reports
13 passed instead of 20 with a stray `^D` in the output. It looks exactly like tests disappearing.


**The #20 fix was wrong once, and the second version matters.** Gating the heal per PRODUCT also
silenced it for dev builds, where the desync is real. The Options+ icon vanished within the hour.
It is now gated on install SOURCE: a package install lives under the service's `Plugins/` directory,
a dev build is reached through a `.link` pointing outside it. Packages skip the restart, dev builds
keep it. The suite was green through both versions — only hardware caught it.

**A file-existence check is not sufficient evidence for #21.** Both answer keys get exercised in the
same minute during a test, so the victim file's absence can mean "Yes approved it" rather than "No
failed". Read the plugin log timestamps against the transcript, or you will conclude the fix failed
when it worked.

**`auto` permission mode masks #21 entirely.** With `defaultMode: auto` and `skipAutoPermissionPrompt`
in `~/.claude/settings.json`, routine commands never prompt — two reproduction attempts produced false
positives before this was noticed. Use `Shift+Tab` in the session to reach ask-every-time.

**The plugin already knew.** #21's fix needed no new detection: `session.State == "waiting"` is what
draws the amber badge on the very key that was misbehaving. The key drew a badge about a pending menu
and then typed a word at it.

**`keystroke` breaks more than text.** The reproduction harness itself failed under Cyrillic because
it used `keystroke "d"` for Ctrl-D. Any character-based shortcut is affected — hence three
`keystroke "t"` sites fixed, not the one the report cites.

## Reproduction tooling (scratchpad, not in the repo)

Written this session and worth recreating if useful:

- `input-source.swift` — list/enable/select/disable keyboard layouts through Text Input Services
  rather than editing `com.apple.HIToolbox`. Takes effect immediately, cleanly reversible.
- `repro-22.sh` + `inject.applescript` / `inject-fixed.applescript` — runs the plugin's exact
  injection into a Terminal window that captures stdin, prints what actually arrived. Honours
  `INJECT_SCRIPT` so old and new mechanisms can be compared side by side under one layout.
- `recolour.py` — recolours flat single-colour PNGs, preserving alpha.

## What Logitech owes us

An email is drafted and, at time of writing, unsent. The blocking item is **#23**: QA wants a
universal plugin (no app binding, no packaged profile) while their design team is commissioning a
redesign of that same profile. Answering it also resolves #34 and #45 as side effects.

Also outstanding from them: the 19 missing icon glyphs at 192px or SVG in white, the colour semantics
decision (#40), whether these fixes go into the existing 2.0.1 submission or a new one, and whether
key labels follow each agent's own wording ("Yes/No" on screen for Claude Code) or stay consistent
across the family.
