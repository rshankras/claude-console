# Claude Console 2.0 — Windows Port (full parity, one release)

**Goal:** Windows users get the *same* keypad experience as macOS — every key group works
identically: prompts, git, answer/menu keys, control keys (Esc/Mode/Tab/Compact/Clear/Exit),
live context + cost + model + activity, session slots with pinning + approval badge,
terminal/tab/window navigation, scroll, and offline voice (send/draft/go-to-project).

**Shipped as one release:** a single cross-platform `.lplug4` carrying both `pluginFolderMac`
and `pluginFolderWin`. Proposed version **2.0** (major — it's the platform-abstraction refactor
plus a whole new backend, from mac-only 1.7.1).

**Status of the unknowns:** the go/no-go was retired by the 2026-08-07 injection spike
(`spikes/windows-injection/`) — unfocused, correctly-targeted injection works across Windows
Terminal, classic conhost, and VS Code's terminal; menu keys (arrows+Enter) work too. What
remains is engineering, plus exactly one genuine platform gap (precise tab *focus*, see R1).

---

## 1. Architecture strategy

Today `BridgeManager.cs` (1457 lines) fuses orchestration with macOS specifics (`osascript`,
`ps`, `/tmp`, TTY). The port introduces a **platform seam** so one codebase serves both OSes:

```
IPlatformBridge                     // the OS-specific surface
 ├─ MacPlatformBridge   (existing behavior, refactored out of BridgeManager — no UX change)
 └─ WindowsPlatformBridge (new)

BridgeManager                       // platform-neutral orchestration, delegates OS calls:
   poll loop · pin/frontmost resolution · SelectSlot/ClearPin · slot assignment ·
   project fuzzy-match · voice runtime lifecycle · settings.json auto-wire policy
```

`IPlatformBridge` surface (names indicative):

| Member | Mac impl | Windows impl |
|---|---|---|
| `DiscoverSessions()` | `ps -axo` + TTY | process scan (cmdline match), keyed by Claude PID |
| `InjectText(id, text, submit)` | one `osascript` run | inject-helper child (`WriteConsoleInput`) |
| `InjectKeys(id, keySeq)` | AppleScript keystrokes | inject-helper key records |
| `FocusSession(id)` | AppleScript select tab | `wt focus-tab` + `SetForegroundWindow` (best-effort, R1) |
| `NewTab / NewWindow / Next/Prev Tab / Next/Prev Window` | AppleScript | `wt.exe` CLI |
| `NewClaude(dir) / GoToProject(dir)` | AppleScript + `cd` + `claude` | `wt new-tab -d <dir> claude` |
| `StateRoot` | `/tmp/claude-console` (0700/0600) | `%TEMP%\claude-console` (ACL-locked; `IpcPaths` already has this branch) |
| `EnsureBridgeWired(settings)` | bash hooks | compiled hook shim + settings.json merge |
| `CreateVoiceCapture()` | signed helper app + whisper arm64 | in-proc WASAPI + whisper x64/arm64 |

**Session identity — corrected after verification.** There is no passive API reporting which
console a process belongs to (`GetConsoleProcessList` only answers for the console you are
currently *attached* to), so discovery cannot "group by console" — and, thanks to the spike,
it never needs to: attaching directly to Claude's own PID reaches the right console. Model:

- **Stable session key = Claude node PID + process start time.** Start time guards against
  Windows PID reuse resurrecting a dead session's slot/pin. `SessionId` becomes opaque —
  a TTY string on mac, `"pid:<claudePid>:<startTicks>"` on Windows — so `SessionRegistry`/pin
  logic is unchanged above the seam.
- **Injection target = the Claude PID itself.** The spike's "any console member works"
  finding is kept as robustness (retry via the parent shell PID if the direct attach fails),
  not as a requirement. The console is never enumerated during discovery;
  `GetConsoleProcessList` is diagnostics only.
- **Titles are never identity** — every tab is `✳ Claude Code` at birth and Claude *rewrites*
  the title to a conversation summary mid-session. Do not read titles for correlation.

---

## 2. Phases (one release train)

| # | Phase | Output | Est. | Risk |
|---|-------|--------|------|------|
| 0 | **Platform seam** | `IPlatformBridge`; `MacPlatformBridge` extracted; `SessionId` made opaque; all existing mac tests still green | 4–6 d | Med |
| 1 | **Windows discovery** | process scan + Claude-PID keying (no console grouping); `SessionRegistry` on Windows | 2–3 d | Low |
| 2 | **Windows injection** | `claude-console-inject.exe` helper; full key set; guard parity | 3–4 d | Low* |
| 3 | **Windows terminal control** | `wt.exe` nav; New Claude / Go-to-Project; focus (R1) | 4–7 d | **High** |
| 4 | **Windows hooks + IPC** | compiled hook shim; ACL-locked state root; settings.json auto-wire | 4–5 d | Med |
| 5 | **Windows voice** | in-proc WASAPI capture; whisper Windows binary; reuse model DL + fuzzy match | 4–6 d | Med |
| 6 | **Packaging + auto-import** | `pluginFolderWin` payload; Windows app-binding; profile auto-import; version | 3–4 d | Med |
| 7 | **Parity QA + release** | on-hardware parity pass; regression matrix; docs; 2.0 ship | 4–6 d | Med |

\*Phase 2 is low-risk because the spike already wrote and proved the mechanism; this is
productionizing it. **Total ≈ 6–9 focused weeks solo**, with R1 the main swing factor.

### Phase 0 — Platform seam (foundation, no user-visible change) — ✅ DONE 2026-08-07
Shipped on `feat/windows-port-phase0`. 245 C# tests + 20 bash tests green (was 200 C# at the
start — the delta is 45 new seam/backend tests, no test deleted).

What landed:
- `src/Platform/` — `IPlatformBridge` (the contract, including the injection guarantee and the
  opaque-session-key rules), `MacPlatformBridge` (all osascript/`ps`/TTY code, moved verbatim),
  `UnsupportedPlatformBridge` + `PlatformBridgeFactory` (runtime selection).
- `BridgeManager` shrank 1457 → ~1100 lines and no longer contains a line of AppleScript.
- **The action classes are now platform-neutral.** They used to pass raw AppleScript through the
  bridge (`InjectKeystroke("key code 126")`, `RunAppleScript("tell application \"Terminal\"…")`).
  They now name intents: `InjectKey(KeyStroke.ArrowUp)`, `Navigate(TerminalAction.NewClaudeTab)`.
  This was the real coupling — without it every key would have needed a Windows branch.
- Session identity renamed `GridSession.Tty` → `SessionKey`, and it is now genuinely opaque
  above the seam (proved by a test that drives macOS, Windows-shaped, and arbitrary tokens).

Two findings worth carrying forward:
1. **`SessionKey` vs `SessionId` is a live trap.** `GridSession` already had a `SessionId` —
   Claude's own conversation id. Both are strings, both get called "the session id" in
   conversation, and only one is safe to key files by. A blanket rename aliased them and the
   suite caught it; `A_session_carries_two_distinct_identifiers` now pins the distinction.
   The Windows shim must key files by `SessionKey`, never Claude's `session_id`.
2. **Voice is not yet behind the seam** (Phase 0b, ~1 day): `StartVoiceCapture` /
   `StopVoiceCaptureThen` keep their `OperatingSystem.IsMacOS()` gates. Harmless — they are
   self-contained and the *typing* half already routes through the seam — but Phase 5 should
   extract `IVoiceCapture` rather than add a second `if (IsWindows)` ladder.

Original scope, for reference:
- Define `IPlatformBridge`; move every `osascript`/`ps`/`/tmp`/TTY touch from `BridgeManager`
  into `MacPlatformBridge`. Behavior-preserving — the existing suite (`InjectionGuardTests`,
  `SessionRegistryTests`, `SessionTargetingTests`, `PendingApprovalTests`, …) is the safety net.
- Generalize `SessionId` from a TTY string to an opaque token. `SessionRegistry` persistence,
  `TargetTty` → `TargetSession`, `SelectSlot`, `ClearPin` all key on the token.
- Runtime selection of the concrete bridge (`OperatingSystem.IsWindows()`) — one IL assembly
  serves both OSes; only the helper executables are per-platform. (The voice entry points are
  already gated `OperatingSystem.IsMacOS()` today — those gates dissolve into the seam.)

### Phase 1 — Windows session discovery
- `WindowsProcessWatcher` (parallels `ClaudeProcessWatcher`): enumerate `node/claude/bun/deno`
  processes whose command line matches the claude CLI; key = PID + start time. Exclude Claude
  Desktop by executable name/path (the Electron `Claude.exe`) — the Windows analogue of the mac
  watcher's `??`-tty drop + case-sensitive `claude` match.
- **No console grouping** (see §1) — discovery yields Claude PIDs only. Mirror the mac poll
  economy: the mac `ps` scan runs only every 4th 500 ms tick; run the Windows scan at the same
  ~2 s cadence, preferring `Process.GetProcesses()` + targeted command-line lookups for new
  PIDs over a full WMI query per tick.
- Feed `SessionRegistry.AssignSlots` unchanged (stable slots 1–6, `registry.json`).
- Tests: `WindowsProcessWatcherTests` with captured process-table fixtures; slot-stability
  and PID-reuse tests.

### Phase 2 — Windows injection (productionize the spike)
- `claude-console-inject.exe` — **GUI-subsystem** child (no console of its own) so
  `AttachConsole(claudePid)` is clean; open `CONIN$`; one `WriteConsoleInput` of the full
  `INPUT_RECORD[]`; exit. Fallback: retry via the session's parent shell PID (spike-proven
  equivalent). One process per injection → the long-lived GUI plugin host never mutates
  global console state (the Windows analogue of the mac "single osascript run").
- Key coverage for full parity: text (Unicode via `VkKeyScan`), Enter, Esc, Tab, arrows,
  PageUp/Down, **Ctrl-C / Ctrl-U / Shift+Tab** (Mode, Clear, interrupt) — the spike's token set
  plus modifiers (see the spike's known gap).
- Guard parity: the atomic single-call + console-targeted delivery *is* the guarantee (can't
  leak to another app, unlike `SendInput`). Add `WindowsInjectionGuardTests` asserting one call,
  one target console.
- Error 5 (elevated target) → typed failure the UI can surface (see R6).

### Phase 3 — Windows terminal control (the hard phase, R1)
- Nav via `wt.exe`: `new-tab`, `new-window`, `focus-tab --next/--previous`, `-w <id>` window
  targeting. New Claude / Go-to-Project: `wt new-tab -d <dir> claude`.
- **Precise focus** (session slot → bring that tab on screen): maintain a `TabLocator` mapping
  session → (WT window id, tab index). Exact for plugin-created sessions; best-effort for
  user-created ones (raise the WindowsTerminal.exe window via `SetForegroundWindow`, then
  `focus-tab -t <trackedIndex>`), with resync on user reordering. **Pinning/injection is always
  exact regardless of focus** — the functional half of the slot key never depends on R1.
- **Supported host for full nav = Windows Terminal.** conhost / VS Code get injection + pin
  (still the core value); relative nav/focus degrade. Documented, not hidden.

### Phase 4 — Windows hooks + IPC
- **Compiled hook shim** `claude-console-hook.exe` (recommended over PowerShell): one shim,
  arg-dispatched for statusline vs activity. It finds its session key exactly the way the bash
  scripts find the TTY — they climb the parent chain with `ps -o tty=` until a real tty appears
  (`statusline-handler.sh:35-46`); the shim climbs the parent chain until it hits the Claude
  node process and keys by that PID + start time. Like the bash handler, it stays dumb: pass
  Claude's stdin JSON through verbatim — all field parsing already lives in `ClaudeState.cs`
  and is platform-neutral.
- State root: keep `IpcPaths`' **existing** Windows branch (`Path.GetTempPath()` →
  `%TEMP%\claude-console\{sessions,activity,voice}` + `registry.json`) — per-user by
  construction. The shim links the same C# path/ACL source, so plugin and shim agree by
  construction — the property the mac side had to enforce by hand-matching bash to C#
  (the documented reason `IpcPaths` pins `/tmp`). `WindowsPrivateFiles` enforces owner-only
  ACLs and refuses reparse points (the NTFS analogue of 0700/0600 + symlink refusal).
- Auto-wire into `%USERPROFILE%\.claude\settings.json`: merge `statusLine` + hooks pointing at
  the shim (absolute, quoted paths — no execution-policy surface), chain any existing status
  line, back up to `settings.json.claude-console.bak`, honor a `no-autowire` marker — mirroring
  `EnsureBridgeAutoWired`.

### Phase 5 — Windows voice (simpler than mac)
- **No helper app, no TCC, no signing/notarization.** In-process WASAPI capture (NAudio or raw
  WASAPI) → 16 kHz mono WAV; Windows mic consent is a one-time OS prompt.
- Bundle/download a whisper.cpp Windows build (`whisper-cli` x64 + arm64); reuse the existing
  `base.en` download + **sha256 verify**, `ListeningFace` animation, and `MatchProject` fuzzy
  matcher unchanged.
- Retire the entire `tools/voice/` signing pipeline for Windows.

### Phase 6 — Packaging + auto-import
Much of this is pre-wired (verified against the codebase 2026-08-07):
- `LoupedeckPackage.yaml` already carries the Windows key **commented out** —
  `#pluginFolderWin: bin` (`metadata/LoupedeckPackage.yaml:36`, "required to support Windows");
  the key convention is confirmed by the service's own `@Generic` package. Uncomment it.
  Windows payload = plugin dll + `claude-console-inject.exe` + `claude-console-hook.exe` +
  whisper bin + profile.
- `ClaudeConsoleApplication` **already overrides `GetProcessName()`** (returns `"Terminal"`,
  marked unused on mac) — change the value to the Windows Terminal process name so
  `HasApplication` auto-imports the packaged profile the way `com.apple.Terminal` does on mac
  (empty names here were the pre-1.5 brick — keep them real). Verify the exact expected form
  ("WindowsTerminal" vs "WindowsTerminal.exe") against a shipping Windows plugin; the SDK also
  exposes `GetProcessNames()` (plural) if more hosts need binding later.
- The **csproj is already dual-platform**: `PluginApiDir` switches to
  `C:\Program Files\Logi\LogiPluginService\` under `Windows_NT`, and the PostBuild/clean
  targets have Windows branches. Remaining open item: confirm the Windows service's PluginApi
  runtime (likely .NET 10, as on mac). The two helper exes ship self-contained
  (`dotnet publish -r win-x64` cross-compiles from the mac, so releases can still be packed here).
- Verify `DefaultProfile70.lp5` imports on Windows (Logi layout format is cross-platform;
  re-stamp GUID if the service dedupes on import).

### Phase 7 — Parity QA + release
- On real MX Creative Console hardware on Windows, walk **every key group** against a parity
  checklist (mac feature → Windows behavior).
- Regression: re-run the spike T-matrix (T1–T4, T6, T7) as an automated smoke where feasible.
- Docs: README Windows section, `SUBMISSION.md` Windows items, CHANGELOG 2.0.
- Ship: single cross-platform `.lplug4`; version aligned in csproj + yaml + CHANGELOG.

---

## 3. Risks & mitigations

| ID | Risk | Sev | Mitigation |
|----|------|-----|-----------|
| R1 | No supported WT API maps a tab to its console → precise *focus* of arbitrary user-created tabs isn't guaranteed | **High** | Plugin-tracked tab index + `wt focus-tab` (exact for plugin-created sessions); window-raise fallback for others; **pin/injection stays exact** so the slot key's function never depends on focus |
| R2 | Terminal fragmentation — `wt` nav only on Windows Terminal | Med | WT = supported host for full UX; conhost/VS Code = injection+pin only; documented |
| R3 | settings.json hook command quoting / PS execution policy | Med | Compiled shim + absolute quoted paths; no script interpreter in the loop |
| R4 | GUI host mutating global console state on every keypress | Med | Per-injection short-lived child helper; host never attaches |
| R5 | whisper.cpp Windows arch/perf (x64 vs arm64) | Low | Bundle/download per-arch; `base.en` is CPU-fine, no GPU |
| R6 | Elevated Claude session unreachable (AttachConsole error 5) | Low | Catch error 5 → "session elevated" keypad state; documented boundary |
| R7 | WSL-hosted Claude sessions not reachable from a Windows plugin | Med | **Out of scope v1**; detect a WSL session and show an informative state |
| R8 | Windows PID reuse resurrects a dead session's slot/pin | Low | Session key = PID **+ start time**; a recycled PID is a new session |
| R9 | `wt.exe` missing (stock Win10) or not on PATH | Low | Detect at startup; nav keys show "Windows Terminal required" state; injection + pin unaffected |

---

## 4. Explicit scope for v1 (parity, not superset)

**In:** full key-group parity on Windows Terminal; injection+pin on conhost & VS Code terminal;
offline voice; auto-wire; auto-import; one cross-platform package.

**Out (documented, not silent):** WSL sessions (detect + inform); full nav on non-WT hosts;
GPU whisper; touching the mac UX (this release must not regress mac — the seam + existing suite
guarantee it).

---

## 5. Verification notes (2026-08-07 codebase audit)

The plan was fact-checked against the code; corrections above are folded in. Confirmed facts
worth knowing before Phase 0 starts:

- `BridgeManager.cs` members and line numbers cited in §1 are accurate; the mac-specific
  surface to extract also includes `ScanClaudeTtys:316`, `QueryFrontmostTerminalTty:430`,
  `NormalizeTty:451`, and the `PsRunner`/`OsascriptRunner` seam hooks (`:687`, `:673`) —
  those two runners are effectively a proto-seam already.
- The bash scripts key state files by climbing the parent chain (`ps -o tty=` per ancestor)
  because hooks can spawn without a controlling terminal — the shim's parent-climb is a
  faithful translation, not a new idea. The scripts pass JSON through verbatim; all parsing
  is in platform-neutral `ClaudeState.cs`.
- Voice model download + sha256 verify (`BridgeManager.cs:1157-1199`) and `MatchProject`
  are platform-neutral and reusable as-is; only capture/transcription entry points are
  mac-gated today.
- Poll economy to mirror: 500 ms tick; frontmost probe every 4th tick, `ps` scan every 4th
  tick (offset), prune every 60 s; scans are non-overlapping.

## 6. Why this is one release, not phased shipping

The user requirement is parity: a Windows user should not meet a half-built keypad. The phases
above are the *build* order, not a ship order — nothing goes to users until Phase 7 signs off the
whole parity checklist. The platform seam (Phase 0) also means mac and Windows share one codebase
from 2.0 on, so future features land on both at once instead of drifting.
