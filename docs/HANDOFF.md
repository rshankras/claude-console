# Handoff — Windows port, as of 2026-08-08

Everything lives on **`feat/windows-port-phase0`** (28 commits, all pushed to origin).
`main` is untouched at 1.7.1. Current version on the branch: **1.8.7**.
Suite: **382 C# + 20 bash, green** — `bash tests/run-all.sh`.

Design doc: `docs/windows-port-2.0-plan.md`. Windows-machine notes:
`docs/windows-debug-handoff.md`.

---

## What is done

**The Windows port is functionally complete and verified on real hardware.** All seven phases
landed: platform seam, session discovery, console injection, terminal control, hooks/IPC, voice,
packaging. On the Windows laptop, **voice and Tab both fire from the keypad** — voice being the
longest chain in the product (record → transcribe → resolve target session → spawn helper →
attach to console → type), so everything under it is proven.

Architecture in one line: everything OS-specific sits behind `IPlatformBridge`
(`src/Platform/`), `BridgeManager` keeps the platform-neutral half, and the action classes name
intents (`InjectKey(KeyStroke.ArrowUp)`) rather than passing AppleScript through. macOS behaviour
is unchanged throughout.

Windows ships four helper executables in the package: `claude-console-inject` (types into a
session's console), `-hook` (statusline + activity), `-focus` (selects the Windows Terminal tab),
`-voice` (mic + whisper).

---

## Outstanding, in order

### 1. ~~The packaged profile ships the wrong top row~~ FIXED 2026-08-08
Turned out to be preview-only: the packaged `.lp5`'s **`ProfileInfo.json` (the layout that
actually imports) already had Session 1/2/3** on the top row — only `metadata/ProfilePreview.json`
(the Options+ thumbnail strip) still showed Context / Working / Mode. The three preview entries
were copied verbatim from the hand-edited installed profile (displayName, actionName,
description, image) into `src/package/profiles/DefaultProfile70.lp5`, and
`profiles/ClaudeConsole-Windows.lp5` was regenerated from it via
`tools/windows/make-windows-profile.sh`. Both verified: preview and layout top rows agree
(SessionSlotCommand 1/2/3), bindings and GUIDs unchanged (`com.apple.Terminal`/…0B58,
`WindowsTerminal`/…0B59).

~~Still open from this item: confirm on the hardware that the installed profile renders live
session keys on the top row.~~ **Confirmed 2026-08-08**: Options+ device view shows slot 1
live-painting a real session (context %, project name) with slots 2/3 idle — SessionSlotCommand
repaints as designed.

Related, ~~still open~~ **fixed 2026-08-08**: with the Claude Console profile selected, the
Options+ actions sidebar defaulted to System Actions instead of the plugin's actions. Cause: the
profile's `ProfileInfo.json` had `nativePluginName: null` / `hasNativePlugin: false`
(ClaudeConsole only in `additionalNativePluginNames`) — an artifact of the profile's origin as a
com.apple.terminal export; working plugins (Vizhi, Figma) stamp their own plugin as the
profile's native plugin. Stamped `nativePluginName: ClaudeConsole` / `hasNativePlugin: true`
(ClaudeConsole removed from additional) in the packaged profile, the installed profile
(pre-edit backup: `~/Desktop/ProfileInfo-before-nativeplugin.json`), and the regenerated
Windows profile; `ClaudeConsole_1.8.8.lplug4` repacked with the fix.

### 2. Clean-install verification for 1.8.x — the real release gate
Does a **first-ever** install create the `@_claudeconsole` application entry and auto-import the
layout? Unknown for 1.8.x. 1.7.1 demonstrably did this on 2026-08-06.

This Mac can no longer answer it: its Logi state was disturbed (below). Needs a different Mac or a
fresh user account — note Options+ does not tolerate fast user switching, so it means a full
logout into a clean account. Five minutes there settles it.

### 3. ~~CHANGELOG is stale~~ DONE 2026-08-08
1.8.6/1.8.7 folded into one honest zero-net entry (payload split + revert), and 1.8.8 added
(session-keys preview fix + native-plugin stamp). README's Windows section was already complete
and accurate.

### 4. Merge to main and release
Only after 1–3. `.lplug4` files are gitignored; releases go to GitHub Releases.

### ~~Post-release candidate: auto-heal the reinstall registration desync~~ SHIPPED in 1.8.9
`src/Platform/RegistrationHeal.cs`, wired at the end of `ClaudeConsolePlugin.Load`, exactly per
the three-signal design (install-event load + registration older than payload + one-shot
marker; the cold-start gate alone makes a loop impossible). Seven unit tests in
`tests/RegistrationHealTests.cs`. macOS only — Windows reinstall behaviour unverified.
Refined through field testing to 1.8.11: the gate is payload-newer-than-service-start (so
back-to-back reinstalls each heal), a 10 s settle delay before the restart, and an explicit
reopen of the Options+ window (launchd respawns the agent windowless).

**Windows: a DIFFERENT failure with the same symptom (settled 2026-08-08 on the laptop).**
The heal was ported in 1.8.13 (plain-process restart via a .cmd — `LogiPluginService.exe` is
NOT an SCM service and nothing respawns it after a kill), but it correctly never fires there,
because Windows' uninstall **deletes the application data outright** — after a reinstall the
machine had only `@_defaultwin`: no `@_claudeconsole`, no Windows Terminal entry, no Claude
Console profile anywhere. Nothing on disk to re-adopt, so no restart can help. Same as macOS,
a reinstall of a known package never re-runs registration. **Windows recovery = re-import
`profiles/ClaudeConsole-Windows.lp5`** (one step: recreates the application entry + layout),
which is already the documented Windows install path. Plugin log:
`%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\ClaudeConsole.log`.
Trap found during recovery: **importing the .lp5 while no Claude Console application is
registered makes Options+ bind the profile to an application it invents from context** (it
landed under `powershell_ise`). Recovery, validated end-to-end on 2026-08-08: stop the
service, write `@_claudeconsole/ApplicationInfo.json` by hand (Mac/Vizhi schema;
`processOrBundleName: WindowsTerminal`, `nativePluginName: ClaudeConsole`), move the imported
profile instance under it, rebind its `applicationName`, delete the bogus app dir, restart
the service — it adopts the entry from the disk scan. Working icon + live keys confirmed.

Possible future work (now DE-RISKED by the validated manual procedure above): Windows
self-registration — at load, when no application data exists, the plugin writes those same
registration files itself and restarts the service to adopt them.

**Corrected root-cause model** (settled 2026-08-08 after four failed package-shape fixes): a
reinstall of an already-known package is a pure payload swap — the service consults NOTHING in
the package (not the profile, not ApplicationInfo; `@_claudeconsole` timestamps stay untouched
through every reinstall). Only a first-ever install of a package name runs the full
registration+import path. Vizhi never looked affected only because its 08:13 install WAS its
first package install (it ran as a dev build before) — and Vizhi lives on the dialpad
(Loupedeck71/72), not the keypad strip we were watching. No package shape can fix the desync;
the self-heal restart is the fix.

---

## Build, test, release

```bash
# tests (both suites)
export PATH=/opt/homebrew/Cellar/dotnet/10.0.302/bin:$PATH
export DOTNET_ROOT=/opt/homebrew/Cellar/dotnet/10.0.302/libexec
bash tests/run-all.sh

# compile-check WITHOUT touching the live Logi install
dotnet build src/ClaudeConsolePlugin.csproj -t:Compile -p:SkipPluginLink=true

# full release build (plugin + Windows helpers + notarized voice payload)
bash tools/voice/pack-release.sh <version>

# pack — needs the .NET 8 runtime; logiplugintool DIES under .NET 10
export DOTNET_ROOT=/opt/homebrew/Cellar/dotnet@8/8.0.124/libexec
export PATH=/opt/homebrew/Cellar/dotnet@8/8.0.124/bin:$PATH
logiplugintool pack ./bin/Release ./ClaudeConsole_<v>.lplug4
```

**The default `dotnet` on this machine is now 10.0.302**, so the pack step needs the explicit
.NET 8 override shown above — it used to work in the bare default environment and no longer does.

Package is ~26 MB. Most of that is three self-contained, trimmed Windows helpers at ~12 MB each.
**Do not "fix" this by making them framework-dependent**: LogiPluginService carries its own
*private* .NET runtime, so a machine running Options+ may have no machine-wide .NET, and typing
would silently die there. NativeAOT would cut it to ~3 MB each but needs the MSVC toolchain.

---

## Traps that cost real time

- **Any reinstall makes the app icon vanish from Options+ — nothing is broken on disk.** This
  includes installing straight over an existing installation with no explicit uninstall
  (confirmed 2026-08-08): the install runs an uninstall step internally. That step drops
  `@_claudeconsole` from the service's in-memory application list but keeps the directory
  (profiles survive); the install step sees the directory and silently skips re-registration. The plugin still loads; only the registration is gone from the live list.
  Recovery (proven 2026-08-08): `killall LogiPluginService` — it rebuilds the list from disk at
  startup — then `killall logioptionsplus_agent` so the UI reconnects. Restarting only Options+
  does nothing. Corollary: a reinstall on a machine with prior registration proves nothing about
  install-time registration — only a clean account exercises that path.
- **Never `rm` an `@_<app>` directory under `Applications/Loupedeck70/`.** It desyncs the service
  so that subsequent installs *silently* skip registration and profile import — including
  known-good packages. This was already in the notes; doing it anyway burned an evening and made
  three different versions appear broken when none were.
- **Registration happens at INSTALL time, not load time.** Editing an installed package's metadata
  cannot re-trigger it. Only a fresh install of a corrected package proves anything.
- **Options+ lags the filesystem.** Profile data can be complete and correct on disk while the UI
  shows nothing. Trust the disk; a restart or a manual import eventually refreshes the UI.
- **To check whether the plugin is actually live**, look at `/tmp/claude-console/registry.json` —
  only the plugin writes it. `"already loaded"` in the plugin log is often a benign duplicate
  load attempt, not a failure.
- **A dev `.link` and an installed `.lplug4` collide.** `pack-release.sh` now passes
  `SkipPluginLink=true` and clears stale links, but a bare `dotnet build` still writes one.
- **`system_profiler` returns empty output in the agent sandbox** regardless of what is connected.
  Use `ioreg` to check for hardware. A wrong "the keypad isn't plugged in" claim came from this.
- **Test fixtures that supply data the real code path fetches itself prove nothing.** Two Windows
  bugs shipped green because fixtures handed `SessionsFrom` a populated command line while the
  real enumerator returns null and resolves separately. Drive `WindowsPlatformBridge`, not the
  pure helpers.

---

## Machine state (this Mac)

- **Logi launch agents were unloaded** during debugging (`com.logi.optionsplus`,
  `com.logi.cp-dev-mgr`) and `~/Library/LaunchAgents/com.logi.optionsplus.plist` is gone. Options+
  may not auto-start at login; launching it manually should re-register it.
- **Backups on the Desktop**: `claude-console-profile-backup-20260807-211135/` (the full
  `@_claudeconsole` registration as it was before any of this) and
  `ProfilePreview-before-sessions.json` (the installed profile before the session-keys edit).
- The installed profile's top row was **hand-edited** to Session 1/2/3 and is not what the package
  ships — see item 1.

---

## Standing context

The 2026-08-07 injection spike (`spikes/windows-injection/`, untracked) is what retired the
go/no-go: writes go to a **console handle**, not the foreground window, so on Windows a keypress
reaches the intended session or nothing at all — it cannot leak into another application even in
principle. That is a stronger guarantee than the macOS AppleScript path provides.

The one genuine platform gap: Windows Terminal exposes no supported way to ask which tab is in
front, so with several idle sessions the plugin cannot tell which you are looking at. One session
works with no pin; beyond that, press a session key first. Pinning is exact. This is documented in
the README's Windows notes.

## Future: other terminals (assessed 2026-08-08)

**iTerm2 is the one worth doing** (next version, not this release): its AppleScript dictionary
addresses sessions by `tty` and types via `write text`, so the exact-targeting guarantee ("a
keystroke reaches the intended session or nothing") carries over intact. Shape: a terminal seam
inside the Mac bridge (the same move IPlatformBridge made for the OS — the action layer already
speaks intents; only the seven Terminal.app AppleScript blocks in MacPlatformBridge are
terminal-specific), plus an iTerm-bound application/profile entry, same pattern as the Windows
.lp5. Everything TTY-keyed (sessions, statusline, hooks, voice, registry) already works in any
terminal unchanged.

**Ghostty / Warp / Alacritty / Kitty: no, until they grow scriptable per-tab addressing.** The
only way in would be blind keystrokes into the frontmost window — the exact unsafety this plugin
refuses (and the README already promises it won't type into them).
