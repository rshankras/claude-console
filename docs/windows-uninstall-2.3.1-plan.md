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

Build/test logs are local artifacts, not committed user data. A Windows QA candidate was
subsequently prepared and its real Options+ uninstall was verified (details below). The final
release package still requires the macOS signing/notarization checks; the shell/macOS suites
and a macOS device regression pass remain release checks.

## Windows QA handoff — 2026-09-23

`~/Downloads/ClaudeConsole-2.3.1-verification/` now contains:

- `ClaudeConsole_2.3.1-edbd9a0-windows-test.lplug4` (17,334,133 bytes), built from implementation
  commit `edbd9a0`. SHA-256:
  `BD6430D29E23395A2D3C391DBDF63B732C3937D720CFB3A5998A3F1208FC8ACC`.
- `START-HERE.txt`, exact installation and before/after/reinstall instructions for this machine.
- `Save-QASnapshot.ps1`, a read-only collector that saves settings, chain/receipt/log, installed
  version, generated launcher count and available service logs in timestamped evidence folders.
- Package-verification output and `SHA256.txt`.

The plugin and both self-contained Windows helpers were rebuilt from the branch. The unchanged
voice payload was taken from the SHA-256-verified published 2.2.2 archive, retaining each entry's
contents and ZIP metadata. Archive CRCs, unique entries and packaged DLL identity were checked.
Logitech's `logiplugintool verify` and the repository package verifier both passed, including
version agreement, helper contracts, no host API dependency payload, and four live support links.
This is a Windows test candidate, not macOS signing/notarization evidence or a Marketplace release.
Helper publishing reported the existing platform-analysis and COM-trimming warnings.

A separate working `logiplugintool` 6.1.4 was found in the earlier main-package-test worktree;
the installed Program Files launcher remains incomplete. The read-only collector was exercised
against the machine at handoff: **2.2.2.0 installed, six generated launchers**, saved as a baseline.
No install, uninstall or service restart was performed by the assistant during handoff. The
owner subsequently installed and uninstalled the candidate as recorded below.

## Real Options+ uninstall result — PASS, 23 September 2026

The owner installed the candidate, enabled live status, and uninstalled Claude Console through
Options+ with live status still on. The assistant captured and compared the before/after state.

| Check | Observed result |
| --- | --- |
| Before, 19:20:44 IST | DLL 2.3.1.0 installed; six owned launchers; no developer link |
| After, 19:23:45 IST | Plugin DLL and manifest absent; zero owned launchers |
| Settings edit | Exactly five owned hooks and the owned status line removed; remaining parsed settings match the original after only those removals |
| Rolling backup | Byte-for-byte identical to the before-uninstall settings |
| Codex hooks | Byte-for-byte unchanged |
| Recovery state | Owned-wiring reinstall receipt created; no Off marker created |
| Host process | Logi Plugin Service remained PID 20684 |

New lifecycle entry at 19:23:12 IST:

```text
2026-09-23T13:53:12.5852248Z Claude Console lifecycle uninstall: complete; owned wiring removed
```

Cleanup was observed without starting a post-uninstall Claude turn. No chained status line
was present in this run, so chained restoration and mixed foreign-hook preservation still need
separate device scenarios despite their automated coverage. Reinstall, replacement, service
restart, Off-state behavior, failure scenarios and a post-uninstall Claude turn remain pending.

The local handoff folder contains `UNINSTALL-RESULT.txt`, `uninstall-settings.diff`, and
`evidence/20260923-192044-575-before-uninstall` /
`evidence/20260923-192345-205-after-uninstall`. Raw settings snapshots are not committed.
The measured result and outstanding checks are also recorded on
[issue #55](https://github.com/rshankras/claude-console/issues/55#issuecomment-5796207287),
which remains open pending the remaining acceptance and release checks.

## Packaged device acceptance checklist

Steps 1–2 have now passed for the enabled setup on this machine, except that a subsequent
Claude turn and the added foreign-hook/chained-status-line scenarios remain unverified.
Steps 3–5 remain pending.

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
