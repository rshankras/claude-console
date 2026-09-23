# Claude Console 2.3.1: Windows uninstall cleanup (#55)

Base: `origin/main` at `796f7e9ea8725744a0be2e6c25f57ece8f3093e6`.
Branch: `fix/windows-uninstall-2.3.1`.

## Scope and implementation order

1. Use the supported `Plugin.Uninstall()` callback; leave ordinary `Unload()` free of settings cleanup. The installed PluginApi 6.4.1.3246 exposes this callback, and Logitech documents it as preceding package removal.
2. Reuse source-preserving edits to remove only Claude Console's hooks and restore a chained status line. Serialize cooperating writers, back up before replacement, and fail without changing settings on parse, backup, lock, or write errors. Cleanup must work before `Load()` and after repeated calls.
3. Establish install/upgrade callback semantics and preserve the live-status preference through a replacement. Do not treat an update as a user request to disable live status. Keep all changes scoped to Claude Console; no Vizhi version or hook migration in this release.
4. Set the Claude Console assembly and package versions to 2.3.1, correct outdated lifecycle guidance, and record release verification.

## Validation

- Existing settings/wiring baseline: 55 tests passed on Windows before edits.
- Targeted tests: foreign hooks and source formatting, chained status line, absent/disabled/partial wiring, repeated cleanup, malformed JSON, denied/locked files, backup failures, concurrent edits, and replacement behavior.
- Exercise the actual SDK override against an isolated home; verify that ordinary unload never removes settings.
- Build Claude Console with `SkipPluginLink=true`, run the full C# suite, and validate version/package contents. No development link, live plugin replacement, or host restart is part of the build.
- Device acceptance: Options+ uninstall with live status on, service restart, upgrade, and reinstall. Record actual callback delivery and host handling of a failed callback; do not equate a unit test with that device evidence.

## Constraints

The existing checkout contains user profile edits and is untouched. The implementation uses a separate worktree. An isolated .NET 10 SDK supplies the project's required toolchain. The user's working Vizhi installation, profiles, and CLI hook configuration must remain unchanged.

Windows Computer Use was unavailable during the initial reproduction, and the installed LogiPluginTool launcher lacks its DLL. Device verification may require restoring the supported host tooling or a user-run acceptance pass. This does not justify marking the release device-verified without evidence.

## Implementation and host evidence — 2026-09-23

Implemented `ClaudeConsolePlugin.Uninstall()` and `Install()` on Windows. Cleanup uses
`ClaudeSettingsStore`, extracted from the bridge's existing source-preserving writer. Normal
unload stops polling only. The lifecycle service takes explicit paths and does not depend on
the bridge singleton, a previous `Load()`, a helper executable, or a UI service.

The SDK contract is documented at
[Logitech: install and uninstall](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/install-and-uninstall/).
Read-only inspection of this machine's installed `Loupedeck.Service.PluginInstaller` also found:

- Replacement calls uninstall on the old package, copies the new package, then invokes install.
- Uninstall unloads the active plugin and creates a fresh plugin context for the callback.
- Returning false from uninstall logs an error but does **not** prevent package deletion.
- Returning false from install removes the new package. Accordingly, a failed settings restore
  logs a deferred result, keeps its receipt and retries on load; it does not reject installation.
- Removing a developer link can bypass the normal packaged uninstall callback.

Local inspection evidence is under the original checkout's
`tmp/issue55-architecture-review/` (including `host-installer/`). Vendor decompiled source is
not part of this branch. These findings describe the installed host; the supported packaged
workflow still needs the device acceptance below.

The upgrade receipt contains only owned wiring, the chained command and the expected uninstalled
status line. It is flushed before cleanup commits. Install merges only those entries, retargets
known commands to the new packaged helper, preserves unrelated edits and a newer user status
line, and consumes the receipt after success. Partial wiring stays partial. An Off marker wins.
Reinstall remembers the same preference as upgrade; this is disclosed in the uninstall guide.
No additional runtime executable, scheduled task or service is installed.

## Completed verification

- Baseline: 55 existing settings/wiring tests passed before edits.
- Full C# suite: **1,222 passed, zero failed**, `tests/TestResults/uninstall-full.trx`.
- Final lifecycle suite: **20 passed, zero failed**, `tests/TestResults/uninstall-lifecycle.trx`.
  This includes two tests added after the full run: a separate PowerShell process holding the
  settings lock, and preservation of a custom Windows file ACL across atomic replacement.
- Focused coverage includes the real SDK uninstall override without initialization/Load,
  ordinary unload, foreign hook groups, comments, UTF-8 BOM, CRLF, Unicode, trailing commas,
  chained status lines, fresh/full/partial/off setup, repeated calls, replacement/reinstall,
  a moved helper path, failed cleanup followed by install without duplicates, malformed JSON,
  backup/receipt/chain failures, lock contention and deferred restoration.
- Existing settings tests also exercise external edits and the bounded retry.
- Claude Console Release build: **zero warnings, zero errors**, with `SkipPluginLink=true`.
  Assembly file version is **2.3.1.0**; the project and staged manifest both say **2.3.1**.
  The output contains no `PluginApi.dll` or host dependency closure.
- Live Claude settings, Codex hooks, installed Vizhi files and the original checkout's two
  edited Vizhi profiles match the pre-test SHA-256 snapshot. Logi Plugin Service remains the
  same process (PID 20684). The snapshot is local scratch, `tmp/live-state-before.json`.
- The existing xUnit1031 warning in `BridgeNoticeTests.cs` remains; the product build is clean.

Build/test logs are local artifacts, not committed user data. The final release package has
**not** been produced or installed. The release packager requires the macOS signing/notarization
checks; the shell/macOS suites and a macOS device regression pass remain release checks.

## Packaged device acceptance still required

1. Install the candidate `.lplug4` normally, with no developer link. Enable live status and add
   a harmless foreign hook and status line in a disposable QA account. Save settings and hashes.
2. Uninstall in Options+ while live status is on. Before another Claude turn, verify that only
   owned entries are gone, the chained status line is restored, the backup holds the preceding
   file, and `lifecycle.log` reports completed cleanup. Then run Claude and check for hook errors.
3. Reinstall and replace the package while live status is enabled. Verify five owned hooks and
   one status line, no duplicates, correct helper paths, unchanged foreign settings and profiles.
   Repeat with live status off and with partial wiring; neither should become fully enabled.
4. Restart the service with the installed plugin and confirm settings wiring remains intact.
5. In the disposable account, deny settings replacement or hold the sidecar lock and uninstall.
   Expect a recorded failure and retained recovery data, even though the host removes the
   package. Resolve the file problem, reinstall, turn live status off and retry uninstall.

Collect the package hash/version, Options+/service version, before/after settings diff,
`lifecycle.log`, the service's callback log entries and test timestamps. Redact unrelated
settings before sharing. Do not close #55 as device-verified until these steps pass.
