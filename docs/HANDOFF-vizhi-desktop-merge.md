# Bringing `feat/vizhi-desktop` up to main — plan (v2, compiler-verified)

`feat/vizhi-desktop` branched on 2026-08-21 and is **18 commits ahead, 197 behind**. Everything the
shared engine gained since — the product-identity seam, the named voice failures (#18), the
Windows helper work, the cross-platform label fix — is missing from it, so this comes before any
further Desktop work.

**Merge, do not rebase.** Replaying 18 commits over 197 means resolving `BridgeManager` repeatedly;
the repo already uses merge commits for this (`merge: bring main 2.2.1 fixes into Vizhi`).

## How this plan was made, and why v1 was wrong

A trial merge reports **18 conflicted files**. That is the wrong number to plan from: a merge only
flags files *both* sides edited. Desktop code that main never touched merges cleanly and then fails
to compile against the API main changed underneath it.

So the trial merge was resolved naively — main wins every conflict, main's deletions accepted — and
the test project compiled (it includes `src/Core/**`, so all Desktop engine code). The compiler
reported **88 errors across 14 files**, only 2 of which were in the conflicted set. The real
surface is **four problems across ~28 files**, verified below; the 18-file list understated it by
roughly half. Everything under `src/Core/Desktop/` (11 files: automation, monitor, runtime, slot
map, adapters) compiled clean and needs nothing.

---

## 1. Registration — Desktop keeps its own copy (owner decision, 2026-09-17)

Main **deleted** `SelfRegistration`, `RegistrationHeal`, `RegistrationCleanup` and their tests when
Logitech's decision made both terminal plugins universal (#23). Desktop is deliberately app-bound —
`HasNoApplication => false`, binding the ChatGPT bundle — and its `Load()` calls all three.

- Move the three sources to `src/Products/VizhiDesktop/Registration/`, renamespaced to the product.
  They depend only on `PluginPaths.PluginDirectory`, `PluginPaths.PluginsRoot`, `IpcPaths.Root` and
  `PluginLog` — **all still present on main** (verified against the definitions, not the callers).
- **Four** test files move with them, not three: `SelfRegistrationTests`, `RegistrationHealTests`,
  `RegistrationCleanupTests` and **`DesktopRegistrationTests`** (8 errors, missed in v1). All use
  temp roots, so they respect the never-touch-the-live-install rule.
- `tests/ClaudeConsolePlugin.Tests.csproj` deliberately excludes product code (it compiles
  `src\Core\**`, `src\Agents\**`, `src\Products\*\*Application.cs`). Add
  `..\src\Products\VizhiDesktop\Registration\**\*.cs`, or the 747 lines of tests silently stop
  compiling.
- `VizhiDesktopPlugin.cs`: four namespace fixes (lines 86, 87, 92, 98) and
  `PluginLog.Init(this.Log, "Vizhi Desktop")` — main made the product name required.
- Comment at the top of each moved file saying why it lives here and not in Core.

## 2. `KeyImage` — Desktop added five things, not one; four collapse onto main's seam

Main added the product-identity seam: `IconResource()`, `UseIdentityIconFolder()`,
`UseIdentityColors()`, `RenderDecisionTile()`. The branch, independently, threaded a
`resourcePrefix = "desktop_icons."` through its own render helpers. **That is the same idea done by
hand**, and it is what most of the breakage is.

| Desktop addition (branch) | Call sites | Resolution |
|---|---|---|
| `RenderDesktop(imageSize, label, icon)` = `RenderFromResource(…, "desktop_icons.")` | 10, in 8 `DesktopActions` files | **Delete.** Replace each with `KeyImage.Render(imageSize, label, accent, icon)`; the plugin constructor calls `KeyImage.UseIdentityIconFolder("desktop_icons")` like the other two products. |
| `RenderDesktopWithApprovalBadge(…)` + private `RenderWithApprovalBadgeFromResource` | 1 (`DesktopApprovalCommand:126`) | **Keep one**, as `RenderWithApprovalBadge`, drop the prefix parameter, resolve the icon through `IconResource()`. Main has **no** generic badge-on-icon renderer, so this is a genuine addition. |
| `RenderConversationSlot` + `WrapConversationTitle` | 2 + 1 test | **Keep**, adapt to main's helpers. Genuinely Desktop's. |
| `RenderSessionSlot(imageSize, icon, ctxPercent, selected, risk)` | 3 (`AllChatsDynamicFolder`) | **This is the pre-redesign face, not a Desktop addition.** Main's face is `(name, stateWord, barColor, darkText)` — the 08-30 design Logitech reviewed. Port the three calls to it. `DesktopConversationCommand.FaceFor(state)` **already returns `(Word, Color, DarkText)`**, exactly main's inputs, so the port is short. `ctxPercent` and the `selected` bracket go away; selection is the bar colour now. |
| `resourcePrefix` parameter | everywhere above | Gone. |

**Design point to confirm:** Desktop never declared identity colours (its render helpers ignore the
accent, as main's do). With `UseIdentityColors` unset it inherits Claude Console's bracket blue and
selection orange. Decide whether Desktop gets its own, as Vizhi did.

## 3. Voice — port onto main's one-door design, adding a product-installed sink

Main rewrote voice: `VoiceCaptureState`, `VoiceFailure`, `ReportVoiceFailure`,
`ToggleVoice(VoiceIntent)`, `VoiceReadyFile`, `Voice.Finish()`. That rewrite **is the #18 fix
Logitech asked for** and carries the Windows bundle. It wins, wholesale.

The branch's `StartVoiceCapture()` (now private), `StopVoiceCaptureTo`, `VoiceRuntimeInstaller`,
`_voiceCaptureState`, `VoiceErrorFile`, `TryReserveVoiceCapture`, `ReleaseVoiceCapture`,
`VoiceCaptureActive` are all gone, and **nothing outside two Desktop voice commands and one test file
uses them** (verified). Desktop's one real need is a transcript sink that is not a terminal:
`DesktopServices.Automation.WriteComposer(text, send, out error)`.

Land it the way the engine already lets a product customise behaviour — `Notify`, `Toast` and
`Prompt` are product-installed delegates on `BridgeManager`, set in the plugin constructor:

- `VoiceCaptureState.cs`: add `VoiceIntent.Desktop` and `VoiceIntent.DesktopDraft`.
- `BridgeManager`: add `internal Action<String, Boolean> TranscriptSink` (text, submit). In
  `ToggleVoice`'s Stop switch, route both new intents to the **existing private**
  `StopVoiceCaptureThen(text => TranscriptSink?.Invoke(text, submit))`. No agent names in Core.
- `DesktopVoiceCommand` / `DesktopVoiceDraftCommand` (67 lines each, own start/stop logic) collapse
  to main's `VoiceCommand` shape: `_face` + `_fail`, subscribe `OnVoiceFailed` for their intent and
  `Voice.Changed` for recording state, `RunCommand → _fail.Clear(); ToggleVoice(intent)`. They gain
  the named failure faces for free.
- `VizhiDesktopPlugin` constructor: `TranscriptSink = (t, send) => DesktopServices.Automation.WriteComposer(t, send, out _)`.
- `BridgeManager` conflict hunk 3 (`VoiceRuntimeInstaller = …` vs `WirePlatformNotices()`): take
  main; the installer delegate belongs to the replaced design.
- `tests/BridgeManagerTests.cs` ~215–250 (10 errors): the reserve/release/active tests are
  superseded by main's `VoiceCaptureStateTests` — drop them.
  `Empty_voice_package_is_never_considered_a_valid_runtime` tests something main still has;
  rewrite it against `RuntimeTreeMatchesPackage` (internal static) rather than the removed API.

## 4. Ten mechanical conflicts

| File | Hunks | Resolution |
|---|---|---|
| `VoiceCommand.cs`, `VoiceDraftCommand.cs`, `ProjectVoiceCommand.cs` | 1 each | Take main. This also removes the last three callers of the now-private `StartVoiceCapture()`. |
| `tools/voice/pack-release.sh` | 5 | Take main's rewrite, then re-add `VizhiDesktop` to `SHIPS_VOICE` and keep the branch's Windows-helper gating |
| `tools/voice/ClaudeVoiceHelper.swift`, `tools/voice/bundle-whisper.sh` | 4, 2 | Take main |
| `tools/convert-designer-icons.swift`, `tools/generate-icons.swift` | 1 each | Take main; check whether the branch's converter fix `6726b51` is already superseded |
| `tools/windows/build-windows-payload.sh` | 1 | Merge both: Desktop adds `VizhiDesktopUia` |
| `docs/multi-agent-architecture.md` | 1 | Merge prose by hand |

Not a problem: `tests/BridgeNoticeTests.cs` appeared in the error scan but it is the pre-existing
xUnit1031 **warning**, not an error.

---

## Order of work

1. **Registration** — mechanical, and it is what lets `VizhiDesktopPlugin` compile at all.
2. **`KeyImage`** — take main, re-add the badge and conversation renderers on `IconResource()`,
   set the identity folder in the plugin constructor, port the 14 call sites.
3. **Voice** — intents, sink, `ToggleVoice` cases, collapse the two Desktop keys, fix the tests.
4. **The ten mechanical files** in one pass.
5. `bash tests/run-all.sh` green (expect it to grow by the registration and Desktop suites), and
   **all three products compile**: `dotnet build src/Products/<P> -t:Compile -p:SkipPluginLink=true`
   for ClaudeConsole, VizhiCodex, VizhiDesktop. The first two must still be at zero warnings.
6. **Device pass.** Desktop was hardware-verified on 2026-08-25 against a much older engine. The
   identity seam changes every key's icon lookup, the session face changes what conversation keys
   draw, and the voice rewrite changes what the voice keys do. None of that is covered by a test.

## Design points for the owner, none blocking

- Desktop identity colours (§2).
- Conversation and All Chats faces move to the Logitech-reviewed name-plus-state-bar design (§2).
  Confirm that is wanted for Desktop, since the layout already deviates from the Appendix-D drawing
  sent to them and that deviation is still unflagged.
- Intent naming (§3): two intents versus one intent plus a submit flag.

## Traps

- **`-t:Compile` poisons the next full build**: a resource-less ~140 KB DLL loads and renders every
  key as bare text. `rm -rf src/Products/VizhiDesktop/obj bin/VizhiDesktop` first; healthy ≈ 1 MB.
- **Reload does not replace a loaded plugin** — `killall LogiPluginService` after a build.
- An empty `…/Plugins/VizhiDesktop/` directory propped up dev on this machine. Check whether that is
  still needed now VizhiCodex has been rebuilt from current source.
- In zsh, `$B:src/…` is a history modifier, not a path. Brace the variable when scripting `git show`.
