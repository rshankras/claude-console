# Multi-Agent Architecture — sharing one codebase across agents

How Claude Console, a Codex CLI console, and a future agent-agnostic plugin share one engine.
Written 2026-08-18.

**Decisions taken.** **One repo builds every product** (layout B below). The Codex product lists as
**"Vizhi for Codex"** — vendor-qualified, per the standing naming rule — which keeps the hackathon
IP answer on the critical path to *listing*, not to building.

*Revised from the initial shared-core-repo recommendation.* That recommendation rested on one
objection — an OpenAI product whose listing points at a repo named for Claude — and a repo rename
answers it, since GitHub permanently redirects the old web and git URLs. Everything else that
argued for splitting was friction without benefit for a single maintainer: submodules cost detached
HEADs and two-step commits, release cadence isn't actually coupled (each package tags and builds
independently from one repo), and nothing consumes the engine as a standalone library, so
versioning it separately buys nothing.

**Landed so far** (branch `feat/multi-agent`): the `IAgentAdapter` seam and both adapters; the
`src/Core` + `src/Agents/<agent>` layout; `tool_name`-aware risk grading so a patch is never graded
as a shell command; and `scripts/codex-hook.sh`, the Codex state-bridge writer. 420 C# tests plus
47 shell tests, green.

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

## Coexistence: two plugins, one keypad — ANSWERED ON HARDWARE (2026-08-18)

Both plugins bind their Options+ application to the terminal, and **two applications claiming the
same bundle cannot both activate**. With Claude Console and Vizhi for Codex installed together, one
wins whenever Terminal is frontmost and the other is unreachable — selecting the loser in Options+
does not survive switching to Terminal. The registration document has no priority or ordering field
(`name`, `displayName`, `deviceType`, `nativePluginName`, `processOrBundleName`, `modes`,
`defaultProfileName`, `isEnabled` — nothing else), so there is no hook for "Terminal, but running
Codex".

Registration itself is fine: `@_claudeconsole` and `@_codexconsole` coexist on disk, each with its
own profile, and neither disturbs the other. It is *activation* that is exclusive.

**This does not overturn shipping two products.** The constraint only affects someone running both
agents, which is a minority; for a single-agent user the other plugin isn't installed and nothing
is lost. Two focused packages remain the better product for most people.

**It does create a support obligation.** A user who installs both sees one plugin silently stop
working, with no error and no clue why — the worst kind of failure. Each product's README and
listing must say plainly: install one. And the `isEnabled` flag in `ApplicationInfo.json` is the
only lever for switching, which needs a service restart and is not a real answer.

**The unified plugin is the answer for dual-agent users, and it is ADDITIVE.** A third entry under
`src/Products/` referencing both adapters: one application binding, one profile, and a grid holding
Claude and Codex sessions together, each key asking its own session's adapter. The engine is close —
discovery already takes a matcher (so it takes two), state parsing already dispatches per adapter,
and the hook already stamps `agent` into every file. What is missing is a per-session agent on
`GridSession` and a merged read across both IPC roots.

Everything else must still be namespaced per product: IPC root, `@_` registration, profile GUIDs,
package name, crash-marker assembly version, `~/.<product>/` runtime home.

## Uninstall leaves the registration behind

Uninstalling through Options+ removes the PLUGIN and nothing else. A sideloaded install never gets
an application entry from the service, so the plugin writes one itself — and that entry survives.
It keeps claiming the terminal, wins activation against a plugin that IS installed, and shows a
keypad of unresolvable keys, so the surviving product looks broken with nothing to explain it.

Two legs, because neither covers the other's case:

1. **A running plugin sweeps on load** (`RegistrationCleanup`). Covers uninstalling one of two
   products. Safe because ownership is stamped: an entry without our stamp is never touched, and
   the running plugin's own entry is never removed.
2. **`scripts/uninstall-registration.sh`** covers uninstalling the LAST one — with no plugin left
   to run, nothing sweeps and the orphan persists indefinitely. Observed on hardware 2026-08-18.

A full uninstall also leaves `~/.codex/hooks.json` and `~/.codex/codex-console/` (the hook launcher)
and the `/tmp/<product>` IPC roots. The hooks file matters most: Codex keeps running a hook for a
plugin that no longer exists. Removing the launcher without the hooks file is the worst order —
every session then runs a hook pointing at a missing script.

## Repo layout

**Chosen: B — one repo, N packages.** This repo, renamed once 2.0.1 clears Marketplace review
(the submitted package carries GitHub URLs; redirects would cover it, but there's no reason to give
QA a second look). Rejected: A, a shared core repo consumed by thin product repos via submodule —
correct for a team, pure friction for one maintainer; and C, forking now and converging later,
which pays a duplication tax until a merge that historically never happens.

```
vizhi/                    (renamed from claude-console; old URLs redirect)
  src/
    Core/                 the engine — platform bridges, session grid, IPC bus,
                          targeting, voice, self-registration + heal, rendering
    Agents/
      ClaudeCode/         adapter, its own project
      CodexCli/           adapter, its own project
    Products/
      ClaudeConsole/      thin: branding, package metadata, profiles, icons → .lplug4
      VizhiCodex/         thin: same, for Codex                            → .lplug4
  tests/                  one suite, one run
```

**Each adapter is its own project, not a folder inside Core.** A product references only the
adapter it ships, so the Claude package cannot carry Codex code — which is exactly the packaging
hygiene Marketplace QA already enforced once. The agent-agnostic plugin later is a third entry
under `Products/` that references both.

Sequence:

1. Define `IAgentAdapter` in place, behind the existing suite, with both adapters proving the
   contract generalises. Nothing ships. **(done — `src/Agents/`, 403 tests)**
2. Reorganise into the layout above and split the adapters into their own projects. Claude
   Console's next release is a no-op refactor — the safest possible proof the move held.
3. Build the Codex state bridge against verified payloads; ship the second package.
4. Rename the repo (post-approval), update `homePageUrl` / `supportPageUrl` in both packages.

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

**Install via a separate `~/.codex/hooks.json`, not by editing `config.toml`.** Codex reads
hooks from either, and the standalone file touches nothing the user already configured —
uninstall is deleting one file rather than surgically removing a marker block from a config
that may have changed underneath us. Vizhi edited `config.toml`; the separate file is strictly
cleaner, and on this machine no `hooks.json` existed to collide with.

Still unverified: the exact payload **field names** on each hook event. Vizhi guessed them with
nested fallback chains and never confirmed them — its single largest fragility. Confirming them
needs user-level hooks plus an interactive trust grant, i.e. a change to a live `~/.codex/config.toml`.
Do that deliberately, capture one payload per event, and write the adapter against ground truth.

## Codex hook payloads — captured, not guessed (codex-cli 0.145.0, 2026-08-18)

Recorded from live sessions via a temporary `~/.codex/hooks.json` listener, all seven events.
Vizhi guessed these field names with nested fallback chains; these are observed.

**Carried by every event:** `session_id`, `cwd`, `hook_event_name`, `transcript_path`.
Everything except `SessionEnd` also carries `model` and `permission_mode`; turn-scoped events add
`turn_id`. Do not assume a uniform payload — `SessionEnd` is the exception.

| Event | Adds |
|---|---|
| `SessionStart` | `source` (e.g. `startup`) |
| `UserPromptSubmit` | `prompt` — the user's text |
| `PreToolUse` | `tool_name`, `tool_input.command`, `tool_use_id` |
| `PostToolUse` | `tool_response`, plus the `PreToolUse` fields |
| `PermissionRequest` | `tool_name`, `tool_input.command` (no `tool_use_id`) |
| `Stop` | `stop_hook_active`, **`last_assistant_message`** |
| `SessionEnd` | `reason` (e.g. `other`) — and **no** `model` / `permission_mode` |

**`tool_input.command` is overloaded, and this is the trap.** Its meaning depends on `tool_name`:

- `tool_name: "Bash"` → a shell command string, the same shape Claude Code sends. `RiskClassifier`
  grades it unchanged.
- `tool_name: "apply_patch"` → **not a command at all**, but a patch body:
  `*** Begin Patch / *** Add File: /tmp/codex-probe.txt / +hello / *** End Patch`.

Feeding a patch to a shell-command classifier is a category error in both directions: it will miss
the actual risk (which files are touched, and whether they are outside the workspace — the captured
example writes to `/tmp`), and it can fire on shell metacharacters that appear in ordinary diff
content. **Risk grading must branch on `tool_name` first**, with a path-based rule for `apply_patch`
alongside the existing command-based rule for `Bash`.

Three findings that shorten the adapter:

1. **`last_assistant_message` arrives on `Stop`.** Vizhi tailed 512 KB of rollout JSONL to
   reconstruct it. No transcript parsing needed for this.
2. **`transcript_path` is handed to us**, so the best-effort token read never constructs or guesses
   a path — it opens what the event names, or gives up.
3. **`permission_mode`** is a field Vizhi never knew about; it makes the active approval policy
   displayable, the closest Codex analogue to Claude Code's input-mode cycle.

Not present anywhere: cost, token counts, context percentage, and **no TTY**. Session identity is
`session_id` + `cwd`. The hook process is a child of `codex` and inherits its controlling terminal,
so it can read its own TTY directly rather than walking the parent chain as Vizhi does.

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
