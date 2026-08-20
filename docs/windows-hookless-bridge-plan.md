# Plan: the hook-free Windows Codex bridge

Implements [windows-codex-hookless-bridge.md](windows-codex-hookless-bridge.md) (the spike's
design). Ships as **Vizhi for Codex 1.5.0** — a feature release, not another 1.4.x fix.
Estimated total: **2–3 focused days** plus one Windows hardware pass.

## Decisions taken here (the design doc left them open)

**D1 — No notify bridge in v1.** `notify` is a single global slot in the user's `config.toml`,
and this codebase's own doctrine (CodexStateBridge's header) is that we never edit
`config.toml` — writing our own `hooks.json` instead is *why* uninstall is one file delete.
The rollout stream already carries both edges (`task_started` AND `task_complete`), so notify
adds only redundancy for its config-editing cost. Deferred until real-world latency or missed
events prove a need; the design doc's chaining rules apply then.

**D2 — Session correlation by start-time proximity.** The rollout knows thread-id and cwd; the
grid keys sessions by `pid-<pid>-<startTicks>`. Neither knows the other. But a rollout file's
creation time tracks its codex process's start time within seconds, and discovery already holds
every codex pid + start time. Primary mapping: newest-unclaimed rollout ↔ process with the
nearest start time (claimed pairs stick). Single-session fast path: one live codex process =
trivial. Fallback when ambiguous: `shared.json` only — last-writer-wins is the existing
degradation the grid already understands, never a wrong per-session claim. PEB-walking for
process cwd is explicitly out of scope for v1 (P/Invoke + ReadProcessMemory for a tiebreak the
fast path makes rare).

**D3 — The bridge emits the EXISTING envelope.** Rollout events translate into the same
`{"schema":1,"agent":"codex-cli","event":…}` file the hook path writes: `task_started` →
`UserPromptSubmit`, `task_complete`/`turn_aborted` → `Stop`. `CodexStateReader`, the grid, and
every key stay byte-for-byte untouched — the transport changes, the contract doesn't.

## Phases

### Phase 0 — Honest capabilities per OS (½ day)
The product must stop promising what Windows can't deliver before the new transport exists.

- `CodexCliAdapter.Capabilities`: on Windows — `ApprovalSignal = false` (until an approval
  event is proven in the rollout stream), `HooksNeedTrust = false` (no hooks are installed).
  macOS values unchanged. The struct is per-instance; make the initializer branch on OS.
- `CodexStateBridge.EnsureInstalled`: no-op on Windows (never write `hooks.json`, never
  message about `/hooks` trust). macOS path untouched. `VizhiCodexPlugin.WireStateBridge`
  branches to the rollout bridge instead.
- Keys gated on `ApprovalSignal` disappear from the Windows experience honestly (the
  amber/red approval lighting); Yes/No keys remain — they type answers regardless.
- Tests: capability matrix per OS; the wiring test asserts Windows never writes hooks.json.

### Phase 1 — `CodexRolloutBridge` (1 day)
New file `src/Agents/CodexCli/CodexRolloutBridge.cs`, driven from the existing BridgeManager
poll (~2s), active only on Windows.

- **Discovery:** newest `rollout-*.jsonl` per session under
  `%USERPROFILE%\.codex\sessions\<yyyy>\<MM>\<dd>\`, today + yesterday only (sessions span
  midnight; older files are dead).
- **Tail:** extract `CodexContextReader`'s bounded-tail into a shared helper
  (`CodexJsonlTail`), read only bytes appended since the last poll per file, tolerate partial
  lines (carry the fragment, never parse it), cap read size per poll.
- **Translate:** `task_started` → busy envelope, `task_complete`/`turn_aborted` → idle
  envelope, per D3. Unknown events: ignored, logged at verbose once per type. Any surprise →
  no write (the reader's existing contract: degrade to unknown, never to a guess).
- **Key:** per D2, then WriteAtomic to `sessions/<key>.json` (+ `shared.json` always, matching
  the hook exe's pattern).
- Done→ready debounce lives where it does for macOS today (no new state machine).
- Tests, through the production seam: captured rollout fixtures (sanitized lines from the
  spike hardware — the payload shapes are in the spike doc), fed through a temp IPC root;
  assert envelopes, correlation, the ambiguous-falls-to-shared rule, and that a malformed line
  writes nothing. The suite stays green on macOS (bridge is pure file IO; only activation is
  OS-gated).

### Phase 2 — Windows ACL pre-positioning (½ day)
From the spike's forward-looking findings; cheap now, a mystery later.

- At install/heal on Windows, grant `CodexSandboxUsers:(OI)(CI)RX` on `%LOCALAPPDATA%\Logi`
  and write access on the IPC root — best-effort `icacls` via the existing registration-heal
  machinery, logged, never fatal (the group only exists after codex's sandbox setup ran).
  This is what makes hooks light up the day OpenAI fixes the hook runner, with no plugin
  update needed.
- Document both grants in the Vizhi README's Windows section.

### Phase 3 — The Windows test debt (½ day)
The four pre-existing failures the spike had to `--no-verify` past:

- `CodexStateBridge` tests asserting the macOS script shape: split expectations per OS (the
  HookCommand overload with the `windows:` flag already exists for exactly this).
- `AgentWiring` discovery tests running against live processes: inject enumerators, same as
  every other discovery test.
- Gate: `tests/run-all.sh` green on BOTH platforms before 1.5.0 tags.

### Phase 4 — Hardware pass, docs, release (½ day + the pass)
- Windows verification: two simultaneous codex sessions → two live keys, busy/done/ready
  transitions, no trust prompt anywhere, macOS regression pass (hooks still preferred there).
- File the upstream codex issue (drafted; evidence = spike table). Add a tripwire note in the
  handoff: when upstream hooks work, Windows *may* switch back — the capability flip is one
  OS-branch removal, and Phase 2 already did the ACL groundwork.
- Preview notes + README: Windows section rewritten around what IS live (state without hooks,
  approvals honestly absent). Release `vizhi-codex/v1.5.0`.

## Acceptance (from the design doc, unchanged)
Two sessions never cross-write; busy within one poll of `task_started`; malformed input writes
nothing; no trust prompt on Windows; macOS untouched. Approvals absent-by-declaration on
Windows is the accepted, documented gap.

## Risks
- **Rollout format drift** — already the declared-unstable contract (`BestEffortContext`);
  the bridge inherits the same degrade-to-nothing posture. Most likely break on codex
  releases; the fixtures make regressions cheap to pin.
- **Start-time correlation collisions** (two sessions started the same second) — falls to
  shared.json by design; revisit PEB-cwd only if the field shows it matters.
- **`turn_aborted` naming** unverified against 0.148 rollouts (spike verified `task_started`/
  `task_complete`); confirm on hardware in Phase 4 before mapping it.
