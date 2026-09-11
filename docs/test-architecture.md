# Test architecture — from a hardware pass to a press harness

Status: proposal, 2026-09-11. Built so far (same day): M0's Windows runner (`tests/run-all.ps1`,
which also runs the helper smoke), M1's exe contract (`WindowsHookContractTests` over
`tests/payloads/`), and M4's gate (`tools/verify-package.sh`, called by `pack-release.sh`).
Everything from M2 on is still a proposal.

The question this answers: can the suite exercise every key without a finger on the keypad, and
if it does, how much of what Logitech QA keeps finding would it catch? The honest answer is
"about two thirds, and the right two thirds". This document says which two thirds, what to
build, in what order, and what stays on hardware forever.

## 1. What the suite must guarantee

Six laws from CLAUDE.md, restated as things a test can refuse:

1. A key never shows a value the agent did not report (#49, #51).
2. Injection is atomic — focus and type in one operation, or nothing (#22, #29).
3. `PluginApi.dll` is never bundled; the version agrees in csproj and package.yaml; every product
   keeps an empty `ClientApplication`; products share no IPC root, runtime home or profile GUID.
4. Nothing on load edits the user's settings.json; Enable writes exactly our entries; Disable
   takes exactly them out and leaves the rest byte-for-byte (#31, #72).
5. A hook can never block or break the user's agent session (#57).
6. A key repaints only when its face changes (#27).

Laws 3 and 4 are already executable (`UniversalPluginTests`, `ProductVersionTests`,
`ProductIsolationTests`, `LiveStatusRoundTripTests`, `SettingsRewriteTests`). Laws 1, 2 and 6 are
enforced today by reading the actions' source text, because the actions cannot be constructed
outside the Logi host. Turning those three into behavioural tests is the point of the press seam
in section 4.

## 2. Where the suite stands

| Asset | State |
|---|---|
| C# suite | 67 classes, 992 tests, ~5 s. Green on macOS and, since 21b695c, on Windows. |
| Shell suites | `test-bridge-scripts.sh`, `test-codex-hook.sh` — the real writer scripts against a temp IPC root. macOS/Linux only. |
| Helper smoke | `tests/windows/Test-StandaloneHelpers.ps1` runs the published focus/shot exes with every runtime lookup disabled. Not wired into any runner. |
| Runner | `tests/run-all.sh` (bash). No Windows equivalent; on Windows the suite is run by hand with `dotnet test`. |
| Package gates | Inline in `tools/voice/pack-release.sh` (PDB paths, voice bundle) and `tools/windows/build-windows-payload.sh` (self-contained helpers). `tools/verify-package.sh`, which two test comments cite, does not exist. |
| CI | None. No `.github/workflows`. |

Strengths: the engine is well covered, both seams have fakes (`FakePlatformBridge`, `NoAgentAdapter`
plus both real adapters), every state-carrying path drives a temp home and a temp IPC root, and
tests cite the issue they pin. The philosophy — test the contract, not the letter — is right and
this plan keeps it.

Gaps, each with the bug that got through it:

- **No test runs the Windows hook exe.** `WindowsHookTests` reads the shim's source. The shim wrote
  `permission`, the reader wanted `waiting`, both sides' tests passed, every Yes/No press on Windows
  was discarded (#74).
- **No press path.** 28 assertions in 7 files check key bodies as text. A key that discards an
  injection outcome (#75), shows no face on a refusal (#76), or launches in the wrong directory
  (#85) is invisible to a text check unless someone thinks to grep for it.
- **No repaint count.** The redraw storm (#27) is guarded by per-key compare statements that a text
  check confirms exist; nothing counts repaints.
- **No end-to-end scenario.** QA's retest items (A, 2, 16, B, C, D in `windows-qa-2.2.1.md`) are
  run by hand on hardware every release.
- **No clean-environment gate.** The helpers shipped framework-dependent for a full release before a
  clean machine showed it (#83); the fix added a build guard, and the smoke script exists but runs
  only when someone remembers.
- **No link check.** Two Options+ card links 404'd for users (#68, #71).
- **Silent platform skips.** `if (!HasUnixModes) return;` passes without asserting. The suite was
  red on Windows for weeks (#56) and nobody's run told them.

## 3. Target shape

Six layers. Each has one job and one kind of bug it exists to catch.

```
L6  Hardware checklist     what only the host shows           15 min, per release
L5  Release gates          the package as shipped             pack time, hard-fail
L4  Scenarios              a press through the whole engine   the QA items, automated
L3  Contracts              two components, one file between   hook exe ↔ reader, scripts ↔ reader
L2  Components             engine over fakes + temp home      wiring, grid, voice, routing
L1  Units                  pure logic                         classifier, matching, faces
L0  Invariants             the six laws                       refuse the build
```

L0–L2 exist and stay as they are. L3 exists for the bash scripts and the Codex hook but not for
the Windows exe. L4, L5 as a single gate, and L6 as a written list are new.

### L3 — contracts: everything that talks through a file gets a test that runs both ends

| Pair | Today | To build |
|---|---|---|
| bash scripts → C# reader | shell suite writes, C# tests read fixtures | run both in one test: script writes to temp root, `BridgeManager` reads it |
| `claude-console-hook.exe` → C# reader | source text only | build the exe once per run, feed captured payloads on stdin with a temp `%TEMP%\claude-console`, read with the real reader, assert state word, pending payload, session key |
| Codex hook → reader | shell suite | same treatment as the bash scripts |
| focus/shot exe → runtime | PowerShell smoke, unwired | wire into the Windows runner and the release gate |
| package → service rules | `UniversalPluginTests` etc. | unchanged |

The exe test is the one that would have caught #74. It also pins law 5: a payload that is not
JSON, an empty stdin, a stdin that never closes (the watchdog), and a ninth concurrent hook (the
cap) must each exit quietly.

Captured payloads live in `tests/payloads/<agent>/<version>/<event>.json`, one file per hook
event, recorded from a real session and committed. When Claude Code changes its payload shape the
fixture is re-recorded and the diff is the review. Payloads are frozen at a version on purpose:
the suite tests our reading of the contract, not Claude Code.

### L4 — scenarios: the press harness

A scenario is a script over one rig:

```
rig = ConsoleRig(agent: ClaudeCode, platform: FakePlatformBridge, home: TempHome, ipc: temp root)
rig.Agent.Event("PermissionRequest", payload: "bash-rm-rf.json", session: "ttys001")
rig.Press(Keys.Yes)
rig.Platform.Keys        == [("ttys001", Return)]
rig.Face(Keys.Yes)       == Face("Yes", Red, dot: true)
rig.Face(Keys.No)        == Face("No", Amber, dot: true)
rig.Repaints(Keys.Yes)   == 1
```

`Agent.Event` runs the REAL hook for the platform (exe on Windows, bash on macOS) so the files on
disk are what production writes. `Press` calls the key's host-free press method (section 4).
`Face` returns the key's face model without drawing it. `Repaints` counts calls that would have
reached `ActionImageChanged`.

The first scenarios are the QA retest items, one each, plus the three laws that are text checks
today:

| Scenario | Pins |
|---|---|
| Both keys amber on a permission request; No clears both; Yes red on `rm -rf` | C (#60), #74 |
| 100 identical state events → 0 repaints; one changed value → 1 | law 6 (#27) |
| A session with no state file shows dashes, never the shared file's numbers | law 1 (#49) |
| Enable with a user's status line → chained, recorded; Disable restores it | 2, B (#31, #55) |
| Windows: no Terminal window → every nav key alerts, one notice | 16 (#33, #61) |
| Dictation with no target → the key says so, nothing typed | #75, #76 |
| New Claude → launch directory is the pinned session's cwd | #85 |
| Codex product: Cost key absent, Model picker present, no `$0.00` anywhere | law 1, capability gating |

Codex runs the same scenarios where its capabilities allow; the rig takes the adapter as a
parameter and the product matrix is a `[Theory]`.

### L5 — release gates: one script, hard-fail

`tools/verify-package.sh <lplug4>` — the file two comments already believe exists. It unzips the
package and refuses it when any of these fail:

- `PluginApi.dll` present anywhere (law 3)
- version disagrees between the DLL, `LoupedeckPackage.yaml` and the CHANGELOG heading
- a Windows helper is not self-contained (`Test-StandaloneHelpers.ps1` on Windows; on macOS,
  size and the absence of `*.runtimeconfig.json` beside the exe)
- the whisper bundle is missing, or ships no compute backend (#24, #47)
- `TRANSCRIPTION_SMOKE_OK` present in either bundle (#64)
- the PDB or DLL carries a build-machine path (#62; exists inline today, moves here)
- a helper's signature fails to verify (#66; today a warning)
- any URL in `BridgeNotice` or `PluginConfiguration.xml` returns other than 200 (#68, #71)

`pack-release.sh` calls it last and propagates its exit code. It is also the one place a link check
belongs: not in the unit suite, which must run offline.

### L6 — the hardware checklist

Fifteen minutes, written down, run per release on each OS. Only things no simulation can show:

- the service loads the package (empty `ClientApplication` present), no "Cannot load plugin"
- Options+ renders the card, the badge clears on load, the undo link opens
- the ready-made layout imports and every key does something (profile prefix, #34)
- a key press focuses the real terminal tab and the text lands there, not elsewhere
- Windows Terminal absent: keys alert; present but hidden: WT's re-show behaviour
- Dictate: microphone prompt, model download face, transcription lands
- Screenshot: Screen Recording prompt, region picker, image lands with the cursor waiting
- Marketplace install and uninstall, and what each leaves behind (#20, #55)

Everything else that is on today's QA sheets moves to L4.

## 4. The press seam

The one production change. Each action's body moves into a host-free method the SDK action
forwards to, and each face is computed as a value before it is drawn.

```csharp
// today
protected override void RunCommand(String p) { ...forty lines... }
protected override BitmapImage GetCommandImage(String p, PluginImageSize s) { ...decides and draws... }

// after
protected override void RunCommand(String p) => AnswerKey.Press(_bridge, approve: true);
protected override BitmapImage GetCommandImage(String p, PluginImageSize s) => KeyImage.Render(s, this.Face());
private KeyFace Face() => AnswerKey.Face(_bridge, approve: true);
```

`KeyFace` is label, accent, icon name and indicator — the four things `KeyImage.Render` already
takes, as a record. `LiveStatusFace.Label` and `AnswerCommand.Decide` are this pattern half done.

Rules, because a seam refactor is exactly what breaks laws 2 and 6 without a test noticing:

- The seam sits at the press boundary and the render boundary. Nothing in the poll loop changes.
- Every compare-before-repaint stays where it is. `PollCadenceTests` runs after each key moves,
  and the repaint-count scenario lands before the second key does.
- No `Inject*` call is split. The press method calls the same atomic bridge method the action did.
- No logging or tracing is added for testability.

Order, by how much bug history each key carries: Answer (#21, #51, #58, #60, #74), SessionSlot
(#25, #29, #49), Status/Cost/Context (#27, #49, #58), Nav (#33, #61, #85), Voice/VoiceDraft/
ProjectVoice (#18, #48, #75, #76, #77), Prompt, Control, Screenshot, Model, Git, Scroll.

Performance: one extra call per human-rate key press and a small record per repaint. No new
timers, caches or assemblies. The only cost is suite time — scenarios that launch the real hook
exe pay process start, roughly 50–100 ms each on Windows.

## 5. Platform honesty

The suite runs green on both operating systems with no silent skips.

- A platform difference is an assertion, not a return:
  `Assert.Equal(!OperatingSystem.IsWindows(), Directory.Exists(scripts))`. The existing
  `if (!HasUnixModes) return;` sites convert as they are touched.
- `tests/run-all.ps1` mirrors `run-all.sh`: `dotnet test`, the helper smoke, the live-root canary
  and the settings.json checksum.
- The shell suites gain a Windows path or an explicit "macOS only, Windows equivalent is the exe
  contract test" line in the runner output — never silence.

## 6. CI, and the `PluginApi.dll` question

The tests reference `PluginApi.dll` from the installed Logi service, which no hosted runner has.
Three options:

1. **Self-hosted runners** with the service installed, one macOS, one Windows. Real API, real
   host DLL, and the only option that can also run L6 items like "does the service load it".
   Cost: two machines to keep alive.
2. **A reference stub**: a small `PluginApi.Reference` project declaring the types the code uses
   (`PluginDynamicCommand`, `BitmapImage`, `PluginImageSize`, enums) with no behaviour, compiled
   only when the real DLL is absent. Hosted runners run L0–L5. Risk: drift from the real surface;
   mitigated by an API-surface test that, where the real DLL is present, compares its public
   members against the stub and fails on a difference.
3. **Commit the DLL.** Only if Logitech's licence allows redistribution in a private repo. Ask.

Recommendation: 2 for the pull-request gate, 1 nightly on one machine of each OS. The stub is a
day's work; the self-hosted pair is the only way "the service loads it" ever becomes a test.

## 7. Back-test against the issue history

Of the bug issues filed since 2.0 (#18–#86, excluding design and questions), which layer would
have refused the change:

| Layer | Would have caught | Examples |
|---|---|---|
| L1 units | 12 | #48 noise-as-speech, #51 idle badge, #77 name mangling, #84 classifier, #72 re-serialisation, #62/#64 hygiene |
| L2 components | 8 | #25 pin never releases, #30 hourglass, #31 backup stale, #49 other session's numbers, #59 helper not replaced |
| L3 contracts | 6 | #74 permission/waiting, #57 hook pile-up, #47 no whisper shipped, #24 no backends, #83 runtime missing |
| L4 scenarios | 7 | #21 both keys approve, #27 redraw storm, #58 inert keys say nothing, #60 dot on No, #75/#76 silent refusals, #85 wrong cwd |
| L5 gates | 4 | #66 signature, #68/#71 dead links, #62 PDB path |
| L6 hardware only | 10 | #20 LPS restart, #32 missing xml crash, #33/#61 Terminal behaviour, #34 profile never updates, #45 orphan registration, #52 "already loaded", #63 WARN lines, #79 flicker, #81 QA claim |

Thirty-seven catchable, ten not: the two thirds. Several catchable ones already have their test
(the repo cites the issue in the test), which is why the count of NEW tests this plan adds is
smaller than the table suggests; the layers that add the most new coverage are L3 for the exe
and L4 for the presses.

## 8. Roadmap

Each milestone leaves the suite green on both OSes and is independently worth shipping.

**M0 — stop the bleeding (done or a day).** #56 fixed (21b695c). Add `run-all.ps1`, wire the helper
smoke into it. Convert silent skips met along the way.

**M1 — the exe contract (1–2 days).** `tests/payloads/` with the five Claude Code events captured
on each OS. A test that builds `claude-console-hook.exe`, runs it per payload into a temp root,
and reads back with `BridgeManager`. Watchdog and cap cases. This is the #74 test.

**M2 — the seam on the answer and live keys, first scenarios (3–4 days).** `KeyFace`, `ConsoleRig`,
seam on Answer, SessionSlot, Status, Cost, Context. Scenarios: C, repaint count, no-value. Text
checks for those keys deleted as their behavioural twin lands.

**M3 — the rest of the keys and the QA sheet (3–4 days).** Seam on the remaining ten keys.
Scenarios for 2, 16, B, D, dictation, New Claude. Codex matrix. Every remaining text check gone.

**M4 — release gate (1 day).** `verify-package.sh` with every check in section 3/L5, called from
`pack-release.sh`. Link check included.

**M5 — CI (1–2 days plus the licence answer).** Reference stub, GitHub Actions matrix on
pull requests, nightly self-hosted where available. API-surface test.

**M6 — the hardware checklist (half a day).** Written, versioned beside this file, and the QA
run sheets shrink to it.

## 9. Exit criteria

- Every bug issue closed since 2.0 names a test in this suite or a line on the hardware checklist.
- Every item on a Logitech retest sheet is a scenario or a checklist line.
- Zero source-text assertions on action bodies remain.
- The suite is green on macOS and Windows with zero silent skips, and CI says so on every pull
  request.
- `pack-release.sh` cannot produce a package that fails a gate.

## 10. Risks and non-goals

- **Stub drift** (section 6) — the surface test, and the nightly real-host run.
- **Frozen payloads** — a Claude Code release changes a hook's shape and the suite stays green.
  Mitigation: capture tooling kept in `tools/`, and "re-record payloads" is a line on the release
  checklist next to "bump the version".
- **Process tests flake** — the exe tests spawn processes; they run serially like everything else
  (the suite is serial on purpose already) with generous timeouts, and never touch the live root.
- **The engine has no injectable clock.** Cadence scenarios (backoff, arm windows) will need one;
  add it as the first scenario needs it, not speculatively.
- Not a goal: pixel-testing rendered bitmaps, testing Claude Code or Codex themselves, or
  replacing the hardware pass entirely. Ten of the forty-seven issues say why.
