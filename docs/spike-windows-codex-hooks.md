# Spike: why does codex on Windows never spawn our hooks?

> Historical investigation against Codex 0.148. Current Codex documentation now explicitly
> supports `commandWindows`; the 1.6.0 follow-up enables official Windows hooks and keeps the
> rollout transport as fallback rather than treating this older failure as permanent.

Time-box: **90 minutes**. Run on the Windows machine, in a Claude Code session opened on this
repo (`feat/multi-agent`). Everything below is self-contained; the full saga lives in
[HANDOFF-multi-agent.md](HANDOFF-multi-agent.md) ("Windows codex hooks").

## The one question

Codex reports `hook exited with code 1` for every lifecycle hook while **tool execution works**
in the same session. The hook exe's first instruction writes a breadcrumb file beside itself —
it has never fired under codex, so **the process is never created**. The same command line runs
fine by hand as the same user. Question: **is there any command shape or sandbox mode under
which codex will spawn a hook — or can no hook of any kind spawn (upstream bug)?**

## Ground truth already established (do not re-verify)

- Plugin 1.4.11 installed; hook exe sha256 starts `AF7A4AC3DAE0`; bulletproof (cannot return
  nonzero, bounded stdin, kernel parent walks, breadcrumbs on every failure path).
- `/hooks` in codex shows all 7 entries Installed=1 / Active=1 (trusted).
- `%TEMP%\codex-console\sessions\` stays empty under codex; populates when the exe runs by hand.
- `%LOCALAPPDATA%\...\VizhiCodex\bin\hook-invoked.log` (spawn proof) stays absent under codex.
- Codex's own tools broke earlier ("the local command sandbox failed to start") and later
  recovered; hooks failed identically in both eras.
- Codex Windows sandbox modes exist: elevated (preferred) / unelevated (fallback), set via
  `[windows] sandbox = "unelevated"` in `~/.codex/config.toml`. Official doc:
  learn.chatgpt.com/docs/windows/windows-sandbox.

## Rules of engagement

- **Stop the plugin service before editing hooks.json by hand** — the plugin rewrites it to
  canonical content on load: `taskkill /f /im logioptionsplus.exe; taskkill /f /im LogiPluginService.exe`.
  Restart Options+ afterwards to restore everything.
- Every hooks.json edit re-triggers codex's trust prompt — approve it each time.
- Record every result in the table below, even nulls.
- Never read or share `~/.codex/.sandbox-secrets/`.

## Experiment ladder (highest information per minute first)

### E1 — Can ANY hook spawn? (5 min, no tooling — run this first)
Stop the service. In `~/.codex/hooks.json`, replace the `UserPromptSubmit` command with:
`cmd /c exit 0`
Start codex, trust, type a prompt.
- Still `hook exited with code 1` → **no hook of any shape can spawn. Upstream bug confirmed.**
  Skip to E5.
- No failure → hooks CAN spawn; our command shape is the problem. Continue.

### E2 — Command-shape matrix (15 min, only if E1 spawns)
Same procedure per shape, `UserPromptSubmit` only; record pass/fail + breadcrumb:
1. `cmd /c ""C:\...\claude-console-hook.exe" codex UserPromptSubmit"` (shell wrapper — the /bin/sh analog)
2. Unquoted: `C:\...\claude-console-hook.exe codex UserPromptSubmit` (path has no spaces)
3. Forward slashes: `C:/Users/.../claude-console-hook.exe codex UserPromptSubmit`
4. Original quoted form (control)

### E3 — Sandbox-mode matrix (15 min)
One prompt per mode; record hook result + whether tools work:
1. Default (no config)
2. `[windows] sandbox = "unelevated"` in config.toml
3. `codex --sandbox danger-full-access` (diagnostic only)

### E4 — ProcMon (20 min)
Sysinternals Process Monitor, filter `Process Name is codex.exe`, Operation `Process Create`.
One prompt. Does codex attempt to create ANY process for the hook? If attempted: exact command
line and error. Save the trace (File → Save, CSV).

### E5 — Upstream intelligence (15 min)
`codex --version` vs latest release; search openai/codex issues for "hooks" + "Windows".
If E1 said upstream: file the issue (draft in the handoff conversation; evidence = this table
+ ProcMon trace if captured).

## Results — run 2026-08-20, codex-cli 0.148.0

The run answered the one question and then kept pulling: the failure has THREE layers, and the
first two are machine repairs that were applied and verified during the run.

**Layer 1 — codex's sandbox helpers are not where codex looks (upstream packaging bug).**
`codex sandbox -- cmd /c "echo hi"` failed with `orchestrator_helper_launch_failed:
… helper=codex-windows-sandbox-setup.exe … error=program not found`. Both helpers
(`codex-windows-sandbox-setup.exe`, `codex-command-runner.exe`) ship inside the release's
`codex-resources\` directory, but codex resolves them relative to the launcher
(`%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` — the log says so verbatim: "helper not
found next to current executable or under codex-resources"). **Repair applied: copy both exes
into that `bin\` directory.** After that, `codex sandbox` works.

**Layer 2 — Logi's protected DACL blocks the sandbox users (packaging interaction, ours to fix).**
Codex's setup stamps an inheritable read+execute ACE for `CodexSandboxUsers` on
`%LOCALAPPDATA%`, but the Logi installer disables inheritance on `%LOCALAPPDATA%\Logi`, so the
grant never reaches the hook exe: spawning it in the sandbox failed with
`CreateProcessAsUserW failed: 5 (Access is denied)`. **Repair applied:
`icacls %LOCALAPPDATA%\Logi /grant "CodexSandboxUsers:(OI)(CI)RX"`.** After that,
`codex sandbox -- <hook exe> codex UserPromptSubmit` runs the exe, which prints `{}` and exits 0
in ~0.6s (~2.1s worst case with held-open stdin — comfortably under the 5s hook timeout).

**Layer 3 — codex's HOOK runner still spawns nothing (upstream, the NO-GO).** With both repairs
in place, every lifecycle hook still fails in ~400 ms with `hook exited with code 1` (exact text
recovered from the `hook/completed` app-server notification — the TUI's "Failed" hides it). The
exe content is provably irrelevant: the real exe was swapped for a probe that logs every
invocation to a world-writable path and structurally cannot exit nonzero — the probe was NEVER
invoked while codex reported the same failures. The exit-1 codex reports belongs to its own
sandbox wrapper chain, which dies before creating the hook process. The identical command line
works through `codex sandbox`, so the divergence is inside the hook-runner spawn path.

| Experiment | Result | Notes |
|---|---|---|
| E1 `cmd /c exit 0` | inconclusive as designed, superseded | untrusted hooks are silently SKIPPED in `codex exec` (no output line at all) — the trust prompt is TUI-only. Superseded by the probe-exe swap: trusted command, known-good exe, zero invocations. **No hook of any shape spawns.** |
| E2.1–E2.3 shapes | moot | the spawn dies before the command shape matters; the original quoted form runs fine via `codex sandbox` |
| E3.1 default (elevated) | fail | hooks `exited with code 1` in ~400 ms, hook process never created |
| E3.2 unelevated | fail | `-c windows.sandbox="unelevated"` — identical failure. **The preview-note claim that unelevated fixes hooks is wrong; corrected.** |
| E3.3 danger-full-access | not run | diagnostic-only; blocked by session policy |
| E4 ProcMon | replaced | probe exe + orchestrator log substitute: hook attempts never reach the sandbox orchestrator log; `windowsSandbox/readiness` reports `ready` |
| E5 version/issues | 0.148.0 = latest | upstream cluster: openai/codex #17478 (enable hooks on Windows), #27052 (hook failures opaque), #26158 (`CreateProcessAsUserW failed: 2` regression), #24098 (elevated fails/unelevated works), #20346 (sandbox can't spawn) |

Two instruments from the ground truth are now known-invalid under a working sandbox, because the
sandbox user has no write access outside its write roots:

- **hook-invoked.log can never appear** — the exe's own directory is read/execute-only to the
  sandbox user, so breadcrumb absence no longer proves the exe didn't run.
- **`%TEMP%\codex-console` is not writable by the sandbox user** (verified: echo-redirect into it
  fails with Access denied). Even a successfully spawned hook would silently drop every state
  file. When OpenAI fixes the hook runner, the plugin must also grant `CodexSandboxUsers` write
  on the IPC root (or hooks light nothing).

## Verdict: NO-GO, with precise coordinates

Upstream codex bug in the hook-runner spawn path, 0.148.0, elevated AND unelevated. The two
machine repairs above are PREREQUISITES, not fixes — they make `codex sandbox` and codex's own
tools work, and they will be needed the day the hook runner is fixed. macOS remains the verified
platform; Windows live-state waits on OpenAI. File the issue with this table; the probe-exe
methodology is the reproduction evidence.

State restored after the run: real hook exe back in place (sha256 `AF7A4AC3DAE0…`), hooks.json
canonical and still trusted, probe artifacts deleted. Kept deliberately: the two helper exes
beside the codex launcher, and the `CodexSandboxUsers` RX grant on `%LOCALAPPDATA%\Logi`.

## Postscript (same day): live state is implementable WITHOUT hooks

Two hook-free channels were verified on the same hardware immediately after the NO-GO:

- **`notify` spawns unsandboxed.** `codex exec -c 'notify=["<probe exe>"]'` invoked the probe
  **as the logged-in user** (not the sandbox user) with argv JSON:
  `{"type":"agent-turn-complete","thread-id":…,"turn-id":…,"cwd":…,"client":"codex_exec",
  "input-messages":[…],"last-assistant-message":"ok"}`. The binary's serde pool shows
  `agent-turn-complete` is the only notify type — so this channel yields ready/done transitions
  keyed by thread-id and cwd. `notify` is a single config slot and the desktop app already
  occupies it (`codex-computer-use.exe "turn-ended"`), so a notify bridge must chain-call the
  previous command — the statusline-chain pattern the plugin already ships.
- **Rollout JSONL records the busy edge.** The live session rollout (written by codex as the
  normal user) carries `event_msg` payloads `task_started` and `task_complete` — verified in
  the day's rollouts. The plugin already tails rollouts (CodexContextReader) under the
  best-effort/unstable-format contract; extending that reader gives busy→done without hooks.

Unverified: whether approval requests appear in the rollout stream (the day's exec runs used
`approval: never`, which can't trigger one). If they don't, risk-graded approvals remain
hook-only and wait on OpenAI; busy/done/ready does not.

Sketch: a `CodexNotifyBridge` (notify exe that chains the prior notify command and writes the
same state envelope the hook writes today) plus `task_started`/`task_complete` in the rollout
reader — both inside the existing `IAgentAdapter` seam, with hooks remaining the preferred
bridge the day upstream fixes spawn.
