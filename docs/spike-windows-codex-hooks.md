# Spike: why does codex on Windows never spawn our hooks?

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

## Results

| Experiment | Result | Notes |
|---|---|---|
| E1 `cmd /c exit 0` | | |
| E2.1 cmd-wrapped exe | | |
| E2.2 unquoted | | |
| E2.3 forward slashes | | |
| E3.1 default | | |
| E3.2 unelevated | | |
| E3.3 danger-full-access | | |
| E4 ProcMon | | |
| E5 version/issues | | |

## Verdict

- **GO** (a working shape/mode found): note it above; the fix is one line in
  `CodexStateBridge.HookCommand` — commit here or hand back.
- **NO-GO** (nothing spawns): upstream codex bug; attach this table + trace to the issue and
  update the handoff. macOS remains the verified platform; Windows live-state waits on OpenAI.

Afterwards: restart Options+ (the plugin restores canonical hooks.json), re-trust in codex.
