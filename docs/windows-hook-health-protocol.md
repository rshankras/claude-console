# Windows hook failure reporting and launcher validation

The Claude launcher remains the encoded PowerShell command. Replacing it with Bash has not been
validated on Windows and is not part of this change. The current form already has Windows contract
tests for Git Bash, direct PowerShell, Unicode input and paths containing spaces/apostrophes. Those
tests must run on the Windows laptop before release; a passing Mac test run is not that evidence.

## What the keys say, and when

The monitor answers one of three things, and only one of them is a problem:

| Status | Meaning | Keys | Options+ |
|---|---|---|---|
| **Unavailable** | The newest evidence is the **helper** failing — file missing, would not launch, crashed — and no hook has succeeded since | **Blocked** on live keys, Yes/No and session slots | one Warning, cleared on recovery |
| **Healthy** | A live session delivered after the last such failure | normal | none |
| **AwaitingFresh** | No failure since the last success, but no live receipt either: a fresh install, a new day (receipts die with their sessions), an upgraded exe, or no session open | normal faces; a session that has not reported yet shows a dash, never Blocked | none |

Per-session gating is separate and stricter: Yes/No act only on an approval this exact session
delivered after the recovery boundary, and a stale one is shown as "nothing pending" and refused
quietly with a log line. That gate never raises a warning, because "no proof yet" is not a failure.

## Failure and recovery records

Records live under `<product IPC root>/hook-health/<helper path hash>/`. The hash is lowercase SHA-256
of UTF-8 `Path.GetFullPath(helperPath).ToUpperInvariant()` (`WindowsHookHealth.HealthDirectoryName`).
The plugin computes it from the package path, the exe from `Environment.ProcessPath`, and the Claude
launcher gets the finished name baked in at wiring time, so the launcher carries no hashing code.
Product IPC roots remain separate: `%TEMP%\claude-console` and `%TEMP%\codex-console`. Moving or
replacing a helper must require fresh delivery; records from another installation path must not make
this installation healthy.

- `failure-<observedUtcTicks>-<scope>-<guid>.json`: immutable object containing `schema: 1`,
  `observedUtcTicks` (UTC `DateTime` ticks), `reason` and `scope`. The scope is in the name so
  trimming is per scope without reading: the plugin keeps the newest 16 of each on every read,
  and the exe trims only its own `-delivery-` records, so a run of delivery failures can never
  push the helper failure out (laptop review of 3de7b20). Records from earlier builds without
  the scope in the name are read to classify them. Concurrent writers cannot overwrite a later
  failure with an earlier one.
  The launcher's suffix is 8 hex digits rather than a full GUID: Windows PowerShell 5.1 enforces
  MAX_PATH (260), and with the 64-hex directory under a long `%TEMP%` the full name went over by
  three characters and the marker silently vanished. The plugin and the exe (.NET, no such limit)
  keep the full GUID. Very long `%TEMP%` paths remain a known limit of the launcher (#122).
- `last-failure.json`: `{schema, observedUtcTicks, reason}` of the newest **helper**-scope failure
  — the barrier itself, kept apart from the diagnostic records. The plugin writes it when it
  records a failure and whenever it reads a launcher record newer than it. A restart reads it
  first; even with every record swept, an approval observed before the failure stays stale.
  - `scope: "helper"` — the exe may not have run: `missing`, `launch-failed`, `nonzero-exit` from
    the launcher, `missing` / `health-unreadable` from the plugin. These move the recovery barrier.
  - `scope: "delivery"` — the exe ran but this invocation could not complete: `input-timeout` from
    the launcher, `observation-failed` from the exe (a forfeited Codex payload, a locked keyed file,
    the concurrency cap). These only withhold that invocation's receipt and are logged once by the
    plugin. A record without a scope is treated as helper scope.
- `success-<sessionKey>.json`: atomic object containing `schema: 1`, `sessionKey`, `event`,
  `startedUtcTicks`, and `completedUtcTicks`. The helper captures start time immediately after
  arming its watchdog and writes the record only after successful keyed state/pending writes.
  Shared fallback writes and an invocation log are not recovery evidence.
- `last-success.json`: `{schema, startedUtcTicks, completedUtcTicks}` of the newest receipt the
  plugin pruned. It is how the plugin remembers that the helper worked after a failure once the
  sessions that proved it are gone; without it every reboot after any past failure would read as
  Unavailable. Written only by the plugin, only while pruning, only when it moves forward.

Unavailable means a helper-scope failure is newer than every success ever seen, live or pruned,
unless a non-future file timestamp proves a newer installation. Putting a quarantined file back
keeps the keys Blocked until a hook actually runs. A replaced file with a known timestamp newer
than the failure starts over as AwaitingFresh. A timestamp synthesized from first sighting of a
future-dated file cannot prove an upgrade: it may be the same helper restored after quarantine.
That distinction survives restart and the clock passing the original file time.
A hook that started before the latest failure cannot recover the bridge by finishing
afterwards. A fresh observation from one session does not validate another session's cached
approval. A fresh statusline or Notification also does not make an older permission payload
current. The monitor and approval gate enforce those separate checks.

"The installed file's time" is not simply its mtime (#125). The installer restores the package's
ZIP timestamp, and ZIP times carry no zone: a helper packed at 11:34 IST is dated 11:34 *local*
wherever it lands, hours ahead of the clock on a machine behind IST. Used raw, that floor judged
every receipt stale until the clock caught up — Yes/No refused quietly, values withheld, Vizhi
reading Run /hooks, and no Blocked face because nothing had failed. The monitor therefore latches
the floor to `min(mtime, first sighting)` per distinct mtime, and persists a future-dated latch in
`helper-version.json` (`{schema, mtimeTicks, versionTicks}`) so a restart inside the window, or
after the clock has passed the file time, keeps the same floor. A past-dated file latches to its own
mtime and writes nothing. A replaced file has a new mtime and starts over, so an upgrade still
demands fresh evidence. The Codex bridge takes the same latched floor for the exe when it computes
its installed-at; `hooks.json` is written locally and keeps its real mtime.

Windows Claude statusline, activity and pending JSON and Codex hook envelopes contain `hookStartedUtcTicks`.
This is transport metadata supplied by the helper, bound to the actual permission payload. The
approval gate requires that invocation to start after the recovery boundary. A file modification
time alone cannot provide this guarantee: a permission hook started before the failure could finish
late while a newer statusline hook restores general health. Older helpers and ordinary rollout
approval snapshots do not provide this proof. Codex code-mode approvals remain supported when
the exact `rollout-code-mode` transport supplies both `observationStartedUtcTicks` (scan start)
and `rolloutEventUtcTicks` (the original request event), both after the recovery boundary.
The rollout reader publishes that authority only after reading through a complete end of the
transcript; partial reads, unresolved trailing lines and truncation remove it. A fresh helper receipt
for that same session is still required. Re-reading an old request does not create a new approval.
The same start-time check applies when publishing live values and activity. Stamping IPC leaves
the original stdin unchanged for the user's chained statusline, including its formatting and Unicode.

The Claude launcher records missing files, native launch exceptions and nonzero exits (helper scope)
and input timeouts (delivery scope). It preserves UTF-8 stdin, stdout and native exit codes, and
remains silent for a missing helper. It is 3,874 characters encoded for a typical path (1,428 of
PowerShell); six copies of it sit in `settings.json`, so `WindowsLauncherHealthTests` holds a ceiling. The
helper records failed required keyed writes, including failures inside the Codex path that must keep
returning exit 0 to the CLI. Missing permission input clears any old Claude pending payload and does
not publish a success receipt. Diagnostic writes are best effort and must never break a user's hook.

These records describe observed failures, not antivirus diagnosis. If endpoint protection blocks the
PowerShell process before it can write a marker while the helper still exists, there is no new signal
to distinguish that from an idle session. A marker also cannot be guaranteed when its directory is
unwritable. Do not infer CrowdStrike's detection reason from either case; collect its detection logs.

The Codex installed command runs the same launcher since #126, with `LauncherProfile.Codex`: the
records go under `%TEMP%\codex-console` and stdin is read in bounded 1.5 s chunks, then the helper
runs with whatever arrived — Codex can leave the pipe without EOF, and a launcher that waited for EOF
and then skipped the helper would lose the event that the direct launch used to record with a null
payload. Codex already starts PowerShell for `commandWindows`, so this command is the launcher
source executed in that shell, without starting a second PowerShell. SessionEnd uses a 500 ms
input wait to leave room for cold startup within Codex's hard three-second limit; other events
keep their five-second deadline and 1.5 s input wait. Contract tests run the installed command
through `powershell.exe -Command` with held-open input and an unwarmed helper.
The command changed once for this release, so every existing Windows user re-trusts it once in
`/hooks` (the keys read Run /hooks until then). Before #126 the command ran the exe directly and a
missing or unlaunchable helper left nothing anywhere the plugin reads (replicated 2026-09-28: exit 1,
zero files), so Codex stayed Healthy on old receipts and every Yes/No press logged "no pending
approval" — QA's 18 September signature. The `LauncherProfile.Claude` text is unchanged from 2.3.2;
`UpgradeOwnedCommands` would rewrite every settings.json if it moved.

## Windows laptop gate

Run with normal Defender/McAfee protection enabled, in an isolated test installation:

1. Run `dotnet test tests/ClaudeConsolePlugin.Tests.csproj --filter FullyQualifiedName~WindowsHookContractTests`.
   Confirm both ordinary Windows tests and Git Bash tests actually execute rather than skip. Include
   Unicode payloads and a helper path with spaces and an apostrophe. Keep the output with the build ID.
2. In a real Claude session, configure a disposable user statusline that reads input and prints a
   recognizable string. Confirm the existing chain still receives the input and displays its output.
   Restore the user's original statusline afterwards. Do not use production settings for automated tests.
3. Establish live status and a pinned session, then temporarily rename the hook executable. Press a
   live key and Yes/No. Expect Blocked, an Options+ status warning, and no input sent to the terminal.
4. Restore the helper and trigger a fresh hook. Expect automatic recovery. In two sessions, recovery
   of session A must not enable an old pending approval in session B. A fresh statusline alone must
   not enable an approval observed before the failure.
5. Test Yes on one visible fresh permission prompt and No on a separate fresh prompt. Repeat after
   restarting the plugin while the helper is unavailable to verify the failure survives a reload.
6. Exercise a present but unlaunchable helper in the isolated contract fixture. Renaming a file tests
   absence, not prevention of execution. Confirm a launch failure marker and no false recovery.
7. In Codex, test a code-mode approval separately from a normal hook-reported permission. After a
   helper failure, an old code-mode request must remain disabled; a new request with fresh helper
   delivery must allow Yes and No on separate prompts. Keep #111 open if this fails on the protected
   Windows setup.

Only reconsider a Bash launcher after establishing the actual shell used by each supported Claude
Code version on the Windows laptop, including when Git Bash is absent. Require the same input,
quoting, exit-code, chaining, timeout and failure-reporting results before changing the command.

The final signed package still requires Logitech QA to repeat the Yes/No and recovery checks with
their CrowdStrike policy enabled. Defender/McAfee results do not establish CrowdStrike acceptance.
