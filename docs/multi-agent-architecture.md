# Multi-Agent Architecture — sharing one codebase across agents

How Claude Console, a Codex CLI console, and a future agent-agnostic plugin share one engine.
Written 2026-08-18.

**Decisions taken.** Shared core repo (layout A below): a neutral repo holds the engine *and all
adapters*; each product repo stays thin and consumes it. The Codex product lists as **"Vizhi for
Codex"** — vendor-qualified, per the standing naming rule — which keeps the hackathon IP answer on
the critical path to *listing*, not to building. The engine itself ships under a neutral name until
that resolves.

**Landed so far.** `src/Agents/` — `IAgentAdapter`, `AgentCapabilities`, and both adapters, with
`tests/AgentSeamTests.cs` pinning the honesty invariants. 403 tests green.

## The finding

The plugin is a **matrix**, and it already has one of its two seams:

```
                 IAgentAdapter        Claude Code │ Codex CLI │ (Gemini, Cursor, …)
                       ×
                 IPlatformBridge         macOS    │  Windows
                       ↓
        shared engine: session grid · file IPC bus · actions · voice ·
        self-registration + heal · key rendering · risk classifier
```

`IPlatformBridge` (the OS seam) landed in 2.0.0 and is proven on both platforms. What's missing
is the mirror seam for the agent. The two are orthogonal: an adapter never learns which OS it's
on, a bridge never learns which agent it's driving.

**The split is ~85/15 in favour of sharing.** Measured by `grep -ric claude` across `src/`, the
agent-specific knowledge is concentrated in three places — `BridgeManager`'s auto-wire half (62
hits), the process watchers (`"claude"` / `"claude.exe"`), and the slash-command vocabulary in a
few actions. Everything else — 4,500 LOC of platform bridges, registry, IPC, voice, registration
heal, rendering, plus 396 tests — is agent-neutral already, or one rename away from it.

Duplicating 85% of a hard-won codebase to vary 15% of it is the wrong trade, and the 1.8.x
self-heal saga is the evidence: that bug was found once and fixed twice (macOS, then Windows).
With three agents it would be fixed three times.

## Why Codex is an unusually clean second agent

Codex CLI's lifecycle hooks are near-identical to Claude Code's, so the state bridge is the same
shape on both sides — not an adapter fighting a foreign model:

| | Claude Code | Codex CLI |
|---|---|---|
| Busy | `UserPromptSubmit`, `PostToolUse` | `UserPromptSubmit`, `PostToolUse` |
| Idle | `Stop` | `Stop` |
| **Approval pending** | `PermissionRequest` (+ `tool_input.command`) | `PermissionRequest` (+ `tool_input`) |
| Compaction | `PreCompact` | `PreCompact` / `PostCompact` |
| Session id, cwd, model | statusline JSON | every hook payload |
| Config | `~/.claude/settings.json` | `~/.codex/config.toml` `[[hooks.X]]` or `~/.codex/hooks.json` |

Two Codex advantages over Claude Code: hooks are **multi-consumer by design** (matcher groups,
concurrent handlers), so wiring in doesn't require the statusline-chain dance; and `PermissionRequest`
carries a structured decision protocol, so a future "approve from the keypad" key has a real API
rather than a synthesised keystroke.

Two Codex gaps: **no cost reporting** (subscription, not per-token), and no statusline, so context
usage must come from tailing the rollout JSONL (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`,
`event_msg/token_count` → `total_token_usage`). The docs explicitly say the transcript format is
not a stable interface — so that path is best-effort and must degrade to "unknown", never to a
wrong number.

Codex also offers verbs Claude Code has no equivalent for, and they deserve keys:
`codex review`, `codex resume --last`, `codex fork`, `codex apply`, `codex exec`.

**Consequence for the design: capability flags, not lowest-common-denominator.** A Cost key on a
Codex session must hide or grey out. It must never render `$0.00`, and the grid must never imply
a number it doesn't have.

## The seam

```csharp
internal interface IAgentAdapter
{
    // Identity
    String Id { get; }                 // "claude-code" | "codex-cli"
    String DisplayName { get; }        // "Claude Code" | "Codex"
    String CliCommand { get; }         // "claude"     | "codex"
    String[] ProcessNames { get; }     // discovery: {"claude"} | {"codex"}
    String IpcRootName { get; }        // "claude-console" | "codex-console"

    // What this agent can honestly report — drives key visibility, never a fake value
    AgentCapabilities Capabilities { get; }   // Cost, ContextPercent, ModelPicker, Modes,
                                              // Compact, NativeReview, Resume, …

    // State bridge: install/repair the agent's own reporting into its config, idempotently
    void EnsureStateBridgeWired();     // Claude: statusline + hooks → settings.json
                                       // Codex:  [[hooks.*]] → config.toml / hooks.json
    Boolean IsStateBridgeWired { get; }

    // Agent JSON (written by the hook script) → the neutral model
    AgentState ParseState(String json);

    // Input vocabulary
    String SlashCommand(AgentVerb verb);   // Model/Compact/Context/Clear/… — null when unsupported
    KeyStroke? ChordFor(AgentVerb verb);   // e.g. Claude's Shift+Tab mode cycle
    String LaunchCommand(String projectDir);
}
```

`AgentState` is today's `ClaudeState` renamed and made honest: nullable `Cost`, nullable
`ContextPercent`, plus `Activity` (idle | busy | waiting | approval) and the pending command that
`RiskClassifier` already grades. The hook scripts converge on **one** `agent-hook.sh` parameterised
by agent id, writing the same file layout under a per-agent IPC root.

**Version the IPC schema now.** It stops being an internal detail the moment a second writer and
a third reader exist (the companion app is already planned against it). A `"schema": 1` field in
every state file, and a documented contract in `docs/ipc-schema.md`.

## Coexistence: two plugins, one keypad

Both plugins bind their Options+ application to the terminal (`com.apple.Terminal` /
`WindowsTerminal`). Registrations are already namespaced — `@_claudeconsole` and `@_vizhi` have
coexisted on this machine — but **which profile activates when Terminal comes forward, with both
installed, is unverified.** That must be tested on hardware before the second listing goes live.
It is also the strongest argument for the eventual single agent-agnostic plugin: one application
binding, one profile, a mixed grid of Claude and Codex sessions.

Everything else must be namespaced per product: IPC root, `@_` registration, profile GUIDs,
package name, crash-marker assembly version, `~/.<product>/` runtime home.

## Repo layout

Three viable shapes:

- **A — shared core repo (recommended).** New neutral repo holds the engine *and all adapters*;
  each product repo stays thin (branding, package metadata, profiles, icons, ~50-line plugin
  class) and consumes core as a submodule. The agent-agnostic plugin then costs almost nothing:
  it's a fourth thin repo that instantiates core with every adapter. Product repos keep their
  names, URLs, issues, and release cadence.
- **B — monorepo.** One repo builds N packages. Least ceremony day to day; couples release cadence
  and puts an OpenAI product under a repo named for Claude unless everything is renamed at once.
- **C — fork now, converge later.** Fastest to a shipping Codex package, zero risk to the plugin
  currently in Marketplace review; pays the duplication tax until the merge, which historically
  doesn't happen.

Recommended sequence for A:

1. Extract `Core` + `IAgentAdapter` **inside claude-console**, behind the 396-test suite; Claude
   Code becomes the first adapter. Nothing ships from this step.
2. Split `Core` (+ both adapters) into the new repo; claude-console consumes it. Its next release
   is a no-op refactor release — the safest possible proof the extraction held.
3. Write the Codex adapter fresh, ship the second package.

## Verified against codex-cli 0.145.0 (2026-08-18)

Empirical, not from docs:

- **Codex is a native binary** (`~/.codex/packages/standalone/releases/<ver>-<arch>/bin/codex`), so
  basename process discovery and the Windows console-handle injection path carry over unchanged.
  An npm-distributed build would break that assumption and need a command-line match instead.
- **Project-local hooks are silently skipped in an untrusted project.** A probe repo with a valid
  `.codex/hooks.json` ran a full `codex exec` turn and fired *nothing* — no error, no warning.
  Hence the wiring targets the **user layer**, as Vizhi's does.
- **Hooks are trusted by hash, reviewed through `/hooks`, and re-flagged whenever the command
  changes.** Two consequences: installation cannot be silent the way Claude Code's `settings.json`
  edit is, and *an update that rewrites the hook command re-nags every existing user*. Install a
  small **stable launcher** and version the logic it delegates to. Never
  `--dangerously-bypass-hook-trust` — it disables review for every hook on the machine, not ours.
- Rollout transcripts confirm the context source: `event_msg`/`token_count` →
  `total_token_usage`, alongside `task_started` / `task_complete` / `turn_aborted`.

Still unverified: the exact payload **field names** on each hook event. Vizhi guessed them with
nested fallback chains and never confirmed them — its single largest fragility. Confirming them
needs user-level hooks plus an interactive trust grant, i.e. a change to a live `~/.codex/config.toml`.
Do that deliberately, capture one payload per event, and write the adapter against ground truth.

## Do not port Vizhi's code

Vizhi is frozen pending hackathon judging, and whether the submission encumbers its IP — and the
*name* — is an open question that now gates three products. The Codex wiring it proved (hook
events → a shell script → local IPC state) is a **design**, already recovered and documented
above; re-implement it on this architecture rather than lifting source.

There is also a correctness reason. Vizhi focuses the target tab in one `osascript` run and types
in a **second** one. `activate` is asynchronous, so a click in that gap sends the keystroke —
including a bare `keystroke "y"` approval, which it fires with no application targeting at all —
into whatever is frontmost now. Its own spec called a post-focus TTY re-verification mandatory; the
code never shipped it. `IPlatformBridge`'s injection guarantee (focus and type in one indivisible
operation, or type nothing and report why) exists precisely to make that class of bug
unrepresentable, and it is the strongest argument for this codebase being the base.

**Worth taking from Vizhi**, though, and each for a concrete reason:

- **Locally-timed long press.** The keypad delivers only Press/Release here — `PressDuration` is
  always 0 and the SDK's LongPress fires at most once — so hold detection must time the press
  itself and suppress the base class. Hard-won; copy the mechanism.
- **Provisional sessions from `ps`.** A grid key lights the moment `codex` is running, before hook
  trust is granted or if hooks fail outright, and is replaced when the real session arrives.
  Graceful degradation instead of an empty grid — and given the trust step, Codex needs it more.
- **Capacity slots.** Sessions compact toward slot 1, so N keys always mean the N live sessions.
- **Stateless animation.** Frame index derived from wall-clock rather than a counter, so every
  animated key stays in phase with no shared state.
- **Symlink refusal before every config write**, and pinning the speech model by exact SHA-256.
- **The parity test idea** — assert cross-surface agreement in a test rather than by discipline.

Reject: the TypeScript/C#/JXA triple implementation (~2× the maintenance surface, and zero tests on
the C# that actually runs), Terminal.app-only injection, and clobbering the clipboard on every send.

Same reason the shared core must not be called Vizhi yet: the name is gated. Ship it neutral,
rename when the brand question resolves — that rename is Phase 2 work either way, and it already
has a plan (registration, profile GUIDs, crash-marker migration for existing users).

## Risks

| Risk | Mitigation |
|---|---|
| Two plugins fight over Terminal profile activation | Verify on hardware before the second listing |
| Codex rollout JSONL is explicitly unstable | Hooks are the source of truth; token counts best-effort, degrade to unknown |
| Codex asks the user to *trust* new hooks on next launch | Document as an install step, like the Accessibility grant. Never suggest `--dangerously-bypass-hook-trust` |
| Extraction destabilises a shipping plugin | Refactor lands behind the existing suite; ship it as a no-op release before the Codex work |
| Product naming ("Codex …") is trademark-sensitive | Vendor-qualified naming decision belongs to the brand plan, not to the code |
