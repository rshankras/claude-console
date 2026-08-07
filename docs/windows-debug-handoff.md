# Windows debug handoff

**You are a Claude Code session running on the Windows laptop.** Everything in this branch was
written on a Mac and has never run on Windows. Two rounds of remote debugging (build on the Mac,
install here, paste the log back) each found real bugs but cost ~15 minutes per iteration. You can
close that loop in seconds. That is why you exist.

Read `docs/windows-port-2.0-plan.md` for the design. This file is the current state and how to
iterate.

## What works (verified on this machine)

- **Session discovery.** `claude-console-inject.exe selftest` correctly found 4 real CLI sessions
  (`C:\Users\sahan\.local\bin\claude.exe`) alongside Claude Desktop.
- **`Process.StartTime` is readable** without elevation — the session key (`pid-<pid>-<ticks>`)
  is sound.
- **The plugin loads.** Log shows `Plugin 'ClaudeConsole' version '1.8.x' loaded`.
- **Slot pinning works.** `BridgeManager: pinned slot 1 -> pid-…` appears on a session-key press.
- **The IPC root resolves** to `%LOCALAPPDATA%\Temp\claude-console`.

## What does NOT work — the live bugs

**BOTH FIXED ON-MACHINE 2026-08-07 (1.8.4). Kept for the record; root causes below.**

1. **No key injects anything.** 1.8.3 WAS installed and the helper WAS found — the log had moved
   on to `AttachConsole(<pid>) failed: 6` (ERROR_INVALID_HANDLE: target has no console). The pids
   were Claude DESKTOP processes: `ResolveCommandLines` built its PowerShell with
   `ForEach-Object {{ … }}` — doubled braces, written as if that string segment were interpolated
   when it is a plain literal — so PowerShell emitted the scriptblock's TEXT instead of pid+TAB+
   command line. Nothing parsed, every command line stayed null, the Desktop markers had nothing
   to match, and all ten Desktop processes were sessions again (the 1.8.3 marker fix was correct
   but starved of input; its tests injected `CommandLineResolver` and never ran the real query).
   Fixed to single braces; two Windows-only tests now drive the real powershell.exe round trip.
   Verified: `registry.json` slots only `.local\bin` pids; helper `text`/`key` against a live CLI
   session exit 0.
2. **The context / live keys show nothing.** Two stacked causes, plus a latent third:
   `EnsureBridgeAutoWired` bailed with `if (!OperatingSystem.IsMacOS()) return;` — the Phase 4
   wiring below it was unreachable; and `HookExePath` was a field initialiser reading
   `AppContext.BaseDirectory` (the same bug PluginPaths exists to kill, in its second home), so
   even an opened gate would have found no shim. Also `EnsureHook`'s idempotence check matched
   only `activity-hook.sh` — a substring no Windows command contains — so every plugin load would
   have appended five duplicate hooks. All three fixed; wiring verified in `settings.json`, the
   hook writes `sessions/` + `activity/` state for live sessions, and a service-triggered reload
   logs "already wired — no changes".

## Fixed already (don't re-fix)

- Claude Desktop counted as a session. It is a **Microsoft Store** install
  (`C:\Program Files\WindowsApps\Claude_…\app\Claude.exe`) whose exe is ALSO `claude.exe`; only
  the path distinguishes it. Two bugs here: the missing `windowsapps` marker, and
  `NeedsCommandLine` skipping `claude.exe` so the marker never had a command line to match.
- Helper located via `AppContext.BaseDirectory` — wrong under the SDK's load context. Use
  `PluginPaths` (fed from `Plugin.AssemblyFilePath`).

## Fast iteration loop — do NOT pack a .lplug4 to test a change

1. **Uninstall the ClaudeConsole plugin in Options+ first.** A dev `.link` and an installed
   package both register the name, and the service then rejects both with
   `because plugin 'ClaudeConsole' is already loaded`. Keep exactly ONE source.
2. `dotnet build src\ClaudeConsolePlugin.csproj` — the PostBuild target writes the dev `.link`
   into the live plugin dir and asks the service to reload. That IS the deploy step.
3. Press a key, then read `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\ClaudeConsole.log`.
4. Repeat. Seconds per cycle.

Build prerequisites: the csproj points `PluginApiDir` at `C:\Program Files\Logi\LogiPluginService\`
on Windows — **verify PluginApi.dll is actually there** and fix the path if Options+ installed
elsewhere. Needs .NET 10 (the Plugin Service runs on it).

Helper executables (they ship in the package but you can build them directly):

```
dotnet publish tools\windows\ClaudeConsoleInject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
dotnet publish tools\windows\ClaudeConsoleHook   -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
dotnet publish tools\windows\ClaudeConsoleFocus  -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Copy the `.exe`s next to `ClaudeConsolePlugin.dll` in whatever bin the service is loading.

`claude-console-focus.exe` (added 2026-08-07) is the tab-focus helper: it reads the session's
console title via AttachConsole and selects the Windows Terminal tab carrying it via UI
Automation — closing Phase 3's "cannot select the tab" gap. It alone targets `net8.0-windows`
(needs the Windows Desktop runtime for System.Windows.Automation); that is why it is a third exe
and not an inject verb — if the Desktop runtime is missing only focus degrades to raising the
window. **The Mac pack flow must add this exe to the package payload.**

## Diagnosing, in order

**Is the helper found?** Log line `claude-console-inject.exe not found in the plugin package`
means `PluginPaths.PackagedFile` returned null — so `Plugin.AssemblyFilePath` isn't what we
assume on Windows. Log its actual value from `ClaudeConsolePlugin.Load` and work from there.

**Does the helper work at all, outside the plugin?** This is the fastest way to isolate:

```powershell
.\claude-console-inject.exe selftest          # lists sessions + keys
.\claude-console-inject.exe text --pid <pid> --start-ticks <ticks> --submit false --text "hello"
```

Run that against a real session from `selftest`. If text appears in Claude, the Win32 layer is
fine and the bug is entirely plugin-side. If it doesn't, the bug is in the helper and the exit
code says which (`0` ok, `2` session missing, `3` failed, `5` elevated).

**Is the hook wired?** `Get-Content "$env:USERPROFILE\.claude\settings.json" | Select-String
"claude-console"`. If empty, `EnsureBridgeAutoWired` bailed — most likely `HookExePath` was null
for the same reason as the inject helper. The wiring only takes effect in a Claude session
started AFTER it lands.

**Is the hook writing state?** After using a Claude session, look for
`%LOCALAPPDATA%\Temp\claude-console\sessions\pid-*.json`. `registry.json` in that directory is
written by the PLUGIN, so its presence proves both sides agree on the root.

## Current machine state (2026-08-07, after the fixes)

- The INSTALLED package (`%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\ClaudeConsole\bin`) has a
  hand-patched 1.8.4 `ClaudeConsolePlugin.dll` copied over the 1.8.3 package — its metadata yaml
  and `PackageHash.bin` no longer match the DLL. It loads and works, but the Mac session should
  pack a real 1.8.4 `.lplug4` and reinstall to make it clean. The helper exes were not touched
  (both bugs were plugin-side).
- At service start the log shows one cosmetic
  `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` pair (the service scans
  the package twice). If keys are ever dead after a restart, `start loupedeck:plugin/ClaudeConsole/reload`
  brings it up; a successful load logs `ClaudeConsolePlugin: Loaded`.
- The wiring landed in `~/.claude/settings.json`; live keys need a Claude session started AFTER
  that (existing sessions' statuslines already flow).

## Rules

- **Keep the tests green**: `dotnet test tests\ClaudeConsolePlugin.Tests.csproj`.
  **Reality check from this machine: 27 of the 338 never passed on Windows** — they exercise
  macOS-only behavior with no platform guard (Unix file modes in PrivateFiles/SessionRegistry
  tests, AppleScript key-code specs, the factory test, macOS injection paths). Baseline at the
  handoff commit: 311/338 on Windows. After the fixes: 321/348, same 27 failures, all 10 new
  tests green. Guarding those 27 is an open follow-up — nothing here touched them.
- **When you fix something, add a test that would have caught it.** Both bugs found remotely got
  through because fixtures encoded an assumption instead of testing the real path. Prefer driving
  `WindowsPlatformBridge` over calling the pure helpers directly.
- Don't touch the macOS backend. It ships and works; this branch must not regress it.
- Voice (Phase 5) is deliberately not implemented on Windows. Leave it.
- Commit as you go with real explanations. The Mac session will pull this back.
