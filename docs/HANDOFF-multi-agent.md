# Handoff — multi-agent work

State as of **2026-08-21**. Read [multi-agent-architecture.md](multi-agent-architecture.md) for the
design and the reasoning; this is where things stand and what to do next.

## Where it stands

**The branch landed.** `feat/multi-agent` merged to `main` (87 commits, merge `c27a69f`). One repo
builds a package per agent from a shared engine, and both products live on `main`.

| | |
|---|---|
| `main` | merged, pushed, clean |
| Claude Console | **2.1.0** in `main` — **untagged, and never hardware-tested on this engine** |
| Vizhi for Codex | **1.5.3** — released, hardware-verified on macOS *and* Windows |
| Tests | 605 C# + 47 shell, green on both platforms |
| Tags | per product: `vizhi-codex/v1.5.3`. Bare `v…` tags are Claude Console's history |

### What works, per platform

**macOS — everything.** Session grid with project/state/context, risk-graded approvals (amber/red),
focus and tab switching, model, best-effort context, offline voice + Voice Draft, screenshot into
the running conversation, native `/review`, prompts, git, navigation.

**Windows — everything except approval LIGHTING.** Sessions, project, busy/done/ready, context,
tab focus by identity, screenshot, prompts, git, nav, voice. Yes/No keys still *answer* (Yes sends
Return and No sends Escape while the user can see the prompt; neither types a word), but they
type); they just don't *glow* when Codex is waiting. That gap is upstream and deliberate — see
Still open.

## The Marketplace, right now

**Claude Console 2.0.1 — in review, and the review is stuck.** QA re-sent the *same* rejection
("your submission includes PluginApi.dll") that 2.0.1 already fixed on 2026-08-13. Verified from
the submitted artifact: `ClaudeConsole_2.0.1.lplug4` contains exactly one DLL and no PluginApi. So
either the resubmission never reached their queue, or they re-reviewed 2.0.0.
**Pending action: check which version the contribute portal lists, then send the drafted reply.**
Evidence file for the attachment: `~/Downloads/ClaudeConsole_2.0.1-contents.txt` — the full archive
listing with its SHA-256, so QA can match bytes against their copy.

**Vizhi for Codex 1.5.3 — submission form filled, NOT submitted.** The package uploaded and all
three text fields are in; [marketplace-listing-vizhi.md](marketplace-listing-vizhi.md) holds the
exact copy, counts verified against the form's own counters (104/120, 478/500, 977/1000). Left
deliberately undone, both requiring a human: the **Developer Agreement checkbox** and the **Submit
button**. If the browser session is gone, re-upload `VizhiCodex_1.5.3.lplug4` and re-paste from the
listing doc.

Notable: the form had **no artwork/screenshot field**, so that open checklist item never blocked
submission.

## What is on this machine (macOS) right now

- **Vizhi for Codex 1.5.3 is installed; Claude Console is not.** Verified from a genuine from-zero
  install: registration recreated, layout imported, `~/.codex/hooks.json` rewritten by the plugin.
- Two consoles cannot coexist (below), so testing Claude Console means uninstalling Vizhi first.
- `~/.claude/claude-console/` holds the shared voice runtime (notarized helper, 141 MB model) and
  `prompts.json`. **Both products use it** — never delete it while cleaning up one of them.
- Uninstalling a plugin leaves its registration behind; `scripts/uninstall-registration.sh
  --remove` is the cleanup, and it needs the service stopped.

## The finding that shapes the product

**Two Terminal-bound plugins cannot coexist.** Both register `processOrBundleName =
com.apple.Terminal`; one wins activation and the other is unreachable, with no priority field to
arbitrate. Decision: **keep shipping two products**, and say "install one" everywhere — both
READMEs, the preview notes, and the Marketplace release notes now lead with it. The unified plugin
(one binding, mixed grid, per-session agent) remains the answer for dual-agent users, and is
additive: a third folder under `src/Products/`.

## Read this before writing code

**Test through the seam production uses.** Eight bugs on this branch shared one shape: a component
built correctly, unit-tested in isolation, and never wired to anything. Every one was found on
hardware, not by the suite. The last and deepest: the Windows process scan's *name filter* still
listed only `claude`, so a matcher-driven watcher was correct and never received a codex row to
inspect — **a seam is only as honest as its narrowest layer.**

**When a constructor takes a value, grep for the literal it replaces.** `MacPlatformBridge` took
`cliCommand` and used it on every path except two `const` scripts hardcoding `do script "claude"` —
so "New Codex" opened a Claude session, under a label that also hardcoded "New Claude", with a unit
test that pinned the bug by asserting the literal.

**A capability describes what the agent can honestly report HERE** — not what the CLI can do, but
what this platform's transport actually delivers. Codex reports no cost → no Cost key. Its hooks
never spawn on Windows → `ApprovalSignal = false` *there*, and the lighting disappears rather than
lying. Both are the rule that forbids a `$0.00`.

**The CLI is not the only door into an agent.** Twice a feature looked impossible from `--help` and
was reachable another way: screenshots into a live conversation (the model opens a path with its
own image tool) and `/review` (a TUI command, not only a subcommand). Check the TUI and the model's
tools before declaring something unsupported.

**When hardening produces no change in symptoms, the failure is upstream of your code** — and prove
it with a probe the failing system cannot distinguish from your real binary. Four rounds of
hardening the Windows hook exe changed nothing, because codex never spawned it.

**The profile and the product must agree.** Gating an action in code while the profile still binds
it leaves a key that looks live and cannot fire.

## Next, in the order I would do it

1. **Finish the Vizhi submission** — checkbox + Submit. Everything else is ready.
2. **Unstick the Claude Console 2.0.1 review** — portal check, then the drafted reply + evidence.
3. **Hardware-test Claude Console 2.1.0, then tag `claude-console/v2.1.0`.** It carries a week of
   engine changes it has never run: the Mac launch scripts were rewritten (now built from
   `_cliCommand`) and Windows discovery became matcher-driven. Test **New Claude** and session
   discovery specifically — those are the rewritten paths. 2.1.0 also fixes orphaned
   registrations, which 2.0.1 users hit today.
4. **File the upstream codex hooks issue** — evidence-complete in
   [spike-windows-codex-hooks.md](spike-windows-codex-hooks.md), never filed.
5. **Long press.** The keypad delivers only Press/Release (`PressDuration` is always 0), so hold
   detection has to be timed locally — the old Vizhi plugin did it in `VoiceCommand.cs:57-86`.
   Nine keys is this product's binding constraint and this doubles them.
6. **Codex's remaining verbs** — `resume --last`, `fork`, `apply`. Genuinely launch-path verbs;
   `LaunchAgentSession` is the machinery to reuse.

## Commands

```bash
bash tests/run-all.sh                                    # 605 C# + 47 shell
dotnet build src/Products/<Product> -t:Compile           # compile-check only

DOTNET_ROLL_FORWARD=LatestMajor \
  bash tools/voice/pack-release.sh <ver> <Product>       # ClaudeConsole | VizhiCodex

python3 tools/make-codex-profile.py                      # regenerate the Codex layout
bash scripts/uninstall-registration.sh [--remove]        # clean orphaned registrations
```

Version lives in **two** files per product (csproj + `LoupedeckPackage.yaml`); `ProductVersionTests`
enforces agreement. The assembly version is what the crash-disable marker keys on.

## Still open

- **Windows: approval lighting is DEFERRED, not broken** (decided 2026-08-21). Codex's hook runner
  spawns no process on Windows (upstream: openai/codex #17478, #27052, #26158, #24098, #20346), and
  hardware proved no file-based channel carries the signal either — the rollout records *nothing*
  while an approval is pending. A workaround was proven end to end: `codex app-server --listen
  ws://127.0.0.1:<port>` plus a passive observer client receives `thread/status/changed` with
  `activeFlags:["waitingOnApproval"]`, clearing on resolution, with the pending command fetchable
  via `thread/items/list`. Not built, because every session would need `--remote` (a hand-typed
  `codex` never would) and the API is experimental — while the upstream fix would light every
  session with zero plumbing, and this plugin is already pre-positioned for it (ACL grants, hook
  exe, readers). Revisit if upstream stalls *and* users ask; start from
  `tools/windows/approval-observer-prototype.py` plus its three recorded lessons: subscribe per
  thread, thread ids come back as bare strings, and exclude the `app-server` process from
  discovery or it appears as a phantom session.
- **Windows: a Store-installed codex CLI would be invisible.** The desktop-app exclusion drops
  anything under `WindowsApps` — right for the OpenAI desktop app's bundled codex.exe, wrong if a
  user's only codex is the Store one. Distinguish by the desktop app's own resources path when
  this shape turns up in the field.
- **A shipped profile never updates an existing install.** Import dedupes by profile GUID, so a
  package update leaves whatever was imported first. The only refresh today is losing the
  registration entirely, after which `SelfRegistration` rewrites it from the packaged lp5. A
  version-aware heal belongs next to `RegistrationHeal.cs`; until then, layout changes reach new
  installs only.
- **EULA legal review** — still carries its template notice. Both legal docs now cover both
  products and both platforms, including screenshots, transcript reading, and the Windows ACL
  grants ([PRIVACY.md](../PRIVACY.md), [EULA.md](../EULA.md)).
- **Repo rename** to something neutral, once 2.0.1 clears review. Both packages carry
  `github.com/rshankras/claude-console` URLs; redirects would cover it, but there is no reason to
  make QA look twice mid-review. `main`'s README now explains the name in its first paragraph.
- **Per-product CHANGELOG split** — one file still tells Claude Console's story only.
- **Trademark search for "Vizhi"** before the paid Apple app ships. The hackathon gate is settled
  (Devpost §8: submissions stay entrant property; sponsor gets judging plus three years' promotional
  use, no commercialization restriction), so the name is clear to use — the brand step was always
  trademark, never Devpost.
- **`prompts.json` is shared by both products** (`PromptCommand.cs:23`, under the shared runtime
  home). Editing prompts for one changes the other. Nobody has decided whether that is right.

## Two Windows repairs worth remembering

Not our bugs, but they cost a day to find and any Windows user may need them:

1. **Codex's own sandbox helpers are misplaced by its installer.** `codex-windows-sandbox-setup.exe`
   and `codex-command-runner.exe` ship inside the release's `codex-resources\`, but codex looks for
   them beside its launcher. Copy both to `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\` and codex's
   own tooling starts working ("the local command sandbox failed to start" disappears).
2. **The Logi installer disables ACL inheritance on its directory**, so codex's `CodexSandboxUsers`
   grant never reaches our files. The plugin now lays both grants down itself, in the background —
   `Platform/CodexSandboxAccess.cs`. Doing it on the Load path once cost a failed install: the
   service kills a `Load` that exceeds 10 seconds, and `icacls /T` across the Logi tree took
   exactly that long.
