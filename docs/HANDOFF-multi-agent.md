# Handoff — multi-agent work (branch `feat/multi-agent`)

State as of 2026-08-18. Read [docs/multi-agent-architecture.md](multi-agent-architecture.md) for the
design and the reasoning; this is where things stand and what to do next.

## Where it stands

One repo now builds a package per agent from a shared engine. Both were verified on real hardware
today, not just in tests.

| | |
|---|---|
| Branch | `feat/multi-agent`, **unpushed** (`git log --oneline origin/main..HEAD`) |
| `main` | untouched at `0472f03` — the shipped 2.0.1 |
| Claude Console | **2.1.0** (was 2.0.1) |
| Vizhi for Codex | **1.4.0** (new product) |
| Tests | 549 C# + 47 shell, green, deterministic |

**Verified working on hardware (Codex):** session discovery, the hook state bridge, live
busy/waiting/ready, project name from `cwd`, focus tracking and tab switching, risk-graded
approvals, model, and context percentage. Prompts, git and navigation keys all fire.

**Voice ships in both products.** It was briefly excluded from Codex on the reasoning that the
package carried no payload — true of the package, false of the feature: voice is agent-neutral, and
it had been working on the dev machine all along because the runtime home
(`~/.claude/claude-console`, a shared literal in `BridgeManager.cs:47`) already held Claude
Console's notarized helper. `pack-release.sh` now embeds the payload for `VizhiCodex` too. Sharing
that home is deliberate — one bundle id, one Microphone grant, one 141 MB model — but note it also
means both products read the same `prompts.json` (`PromptCommand.cs:23`), which nobody has decided
on. Only Tab and Cost stay dropped from the Codex profile; those are capability gaps, not
packaging ones.

**Windows:** first hardware attempt 2026-08-20 found the Codex adapter dead there — profile
visible, no keys firing — from two more unwired seams (bugs six and seven of the shape): discovery
(`WindowsProcessWatcher` carried hardcoded claude names; now matcher-driven like macOS) and the
state bridge (`hooks.json` got `/bin/sh …` on Windows, which can never run; the packaged hook exe
grew a `codex <event>` verb that writes the same envelope as codex-hook.sh to the codex-console
IPC root). Fixed and unit-pinned in 1.4.5, NOT yet re-verified on the Windows box. Claude Console
2.0 remains Windows-verified.

## What is on this machine right now

Checked 2026-08-18, after the hardware session:

- **Vizhi for Codex 1.4.0 is installed; Claude Console is not.** Both were uninstalled during
  testing and only Codex was put back.
- Its registration `@_codexconsole` and `~/.codex/hooks.json` are present and correct — written by
  the plugin, not leftovers. A reinstall recreates the hooks file, which then needs `/hooks` → trust
  inside Codex before any key shows live state.
- No orphaned registrations. `~/.codex/config.toml` is untouched: the 12 Vizhi hook lines from July
  2026 are still there and are not ours.

## The finding that shapes the product

**Two Terminal-bound plugins cannot coexist.** Both register `processOrBundleName =
com.apple.Terminal`, one wins activation, and the other is unreachable — selecting it in Options+
does not survive switching to Terminal, and its actions sidebar shows the winner's actions. The
registration document has no priority field to arbitrate with.

Decision taken: **keep shipping two products.** It only affects users running both agents, and two
focused packages serve everyone else better. But it creates an obligation — a user who installs both
sees one plugin silently stop working. **Each README and listing must say: install one.** Not
written yet.

The unified plugin (one binding, mixed grid, per-session agent) remains the answer for dual-agent
users and is additive — a third folder under `src/Products/`.

## Read this before writing code

Three bugs this session shared one shape: **a component built correctly, tested in isolation, and
never wired to anything.** Each passed its unit tests and each was found only on hardware.

1. `AgentProcessMatcher` was correct; `BridgeManager` built its bridge in the constructor, before
   the product declared its agent — so the Codex keypad discovered `claude` processes.
2. The fix for that didn't work either: the public constructor delegated to the internal
   test-injection one, marking every bridge as caller-supplied so the rebuild never ran. The tests
   asked the *adapter* for its matcher, which passes happily while the manager ignores it.
3. `CodexStateReader` was correct and **nothing called it**. The grid deserialised every state file
   as Claude's statusline, which a Codex envelope satisfies with all fields null — so sessions sat
   at "ready" forever wearing a project name they never reported.

The lesson, and it cost most of an afternoon: **test through the seam that production uses**, not
the component beside it. A test that constructs the collaborator itself proves nothing about
whether the system connects them.

A fourth, different in kind: gating an action in code while the profile still binds it does not
remove the key — it becomes an unresolvable binding. **The profile and the product must agree.**

A fifth surfaced 2026-08-19, same shape as the first three: `MacPlatformBridge` took `cliCommand`
at construction and used it on every launch path EXCEPT the two `Navigate` scripts, which were
consts hardcoding `do script "claude"` — so the Codex keypad's "New Codex" key opened a claude
session. `NavCommand.GetCommandDisplayName` hardcoded the "New Claude" label two lines below a
comment warning against exactly that, and a test PINNED the bug by asserting the literal
(`Assert.Contains("do script \"claude\"")` for every product). Found because a user read a key
label. When a constructor takes a value, grep for the literal it replaces — every remaining
occurrence is this bug waiting.

## Next, in the order I would do it

1. **Land the branch.** Claude Console 2.1.0 fixes orphaned registrations, which affects users on
   2.0.1 today: uninstalling leaves an entry that claims the terminal and shows nine warning keys
   with no explanation. That is a real bug-fix release independent of any Codex work. Land as the
   no-op-for-Claude refactor release the architecture doc describes, then tag per product
   (`claude-console/v2.1.0`) since bare `v…` tags are now ambiguous.
2. ~~Write the "install one" note~~ — DONE 2026-08-19 in the root README and the new
   src/Products/VizhiCodex/README.md (plus docs/vizhi-codex-preview.md, the note that travels
   with the preview build). Still owed to the two Marketplace LISTINGS when they exist.
3. **Long press.** The keypad delivers only Press/Release (`PressDuration` is always 0), so hold
   detection has to be timed locally — Vizhi does this in `VoiceCommand.cs:57-86`. Nine keys is this
   product's binding constraint and this doubles them. Highest value per unit of work.
4. **Codex's own verbs** — STARTED 2026-08-19: Review landed. `/review` is a first-class TUI
   command (review_popups.rs), not subcommand-only as the adapter first recorded — the same
   CLI-is-not-the-only-door trap as images. `AgentVerb.Review` now maps to "/review" on Codex and
   stays null on Claude Code (there it is a prompt, and the key hides). It holds page 2 slot 1;
   Clear demoted to page 2's far corner. Still open: `resume --last`, `fork`, `apply` — genuinely
   launch-path verbs (`LaunchAgentSession` is the machinery to reuse).
5. ~~Screenshot key~~ — DONE 2026-08-19, corrected same day. `ScreenshotCommand` in Core:
   `screencapture -i` (the system picker) → the path is TYPED into the CURRENT conversation with
   Vizhi's proven instruction sentence, no Return — the user appends their question. This item's
   original premise ("Codex only at launch via `-i`") was true of the CLI flag and WRONG about
   the workflow: Codex's model opens a file mid-session with its image-viewing tool when told the
   path, which the July Vizhi plugin proved on hardware (VizhiActionRouter.cs:444-448). The trap,
   for next time: the CLI is not the only door into an agent — the model's own tools are another.
   First press cost a one-time Screen Recording grant for LogiPluginService (granted on this
   machine; verified capturing real files). `ImageAtLaunch`/`LaunchAgentSession("-i", path)`
   survives as the tested fallback for a genuinely launch-only agent. On the Codex profile the
   key holds page 1 slot 4; Clear moved to page 2 beside Compact. Claude Console's profile does
   NOT bind it (its page 1 is full) — the action registers there, bindable from the sidebar.
   Windows capture is an honest unsupported-stub. (Clipboard remains not worth it — duplicates
   ⌘V, and Vizhi's version clobbers the clipboard without restoring it.)
6. **Windows**, for either product.

## Commands

```bash
bash tests/run-all.sh                                    # 549 C# + 47 shell
dotnet build src/Products/<Product> -t:Compile           # compile-check only

DOTNET_ROLL_FORWARD=LatestMajor \
  bash tools/voice/pack-release.sh <ver> <Product>       # ClaudeConsole | VizhiCodex

python3 tools/make-codex-profile.py                      # regenerate the Codex layout
bash scripts/uninstall-registration.sh [--remove]        # clean orphaned registrations
```

Version lives in **two** files per product (csproj + `LoupedeckPackage.yaml`) and `ProductVersionTests`
enforces that they agree. The assembly version is what the crash-disable marker keys on.

## Still open

- **Windows: codex tab-switching lands on the first tab** (hardware, 2026-08-20, 1.4.8). The
  focus helper identifies a tab by its console TITLE (the only process→tab mapping Windows
  offers); codex tabs likely share one title, so UI Automation's first match always wins. Needs a
  hardware-in-the-loop investigation: confirm the title collision (read the tab labels of two
  codex sessions), then either find a second discriminator or degrade honestly to raising the
  window. Injection is unaffected — it addresses console handles, not tabs.
- **Windows: a Microsoft Store-installed codex CLI would be invisible.** The desktop-app
  exclusion drops any process under WindowsApps — right for the OpenAI desktop app's bundled
  codex.exe, wrong if a user's ONLY codex is the Store one run via an app-execution alias.
  Distinguish the desktop app by its own resources path, not by WindowsApps wholesale, when this
  shape shows up in the field.
- ~~Windows codex hooks: "exited with code 1"~~ — ROOT CAUSE FOUND 2026-08-20, not ours: codex
  on Windows spawns hooks through its native sandbox, and on the test machine that sandbox
  fails to start ("the local command sandbox failed to start" — codex's own shell commands die
  the same way). Confirmed against the binary (WindowsSandboxSetupMode elevated/unelevated) and
  the official doc. Fix on the machine: repair the elevated setup (UAC approval, local-user
  creation, firewall, logon rights — Windows error 1385 = missing logon rights) or set
  `[windows] sandbox = "unelevated"` in config.toml. The four hardening rounds it took to
  corner this (1.4.7-1.4.11: exit-zero guarantee, kernel parent walks, evidence-first writes,
  bounded stdin, spawn-proof breadcrumb) all remain — the hook exe is now bulletproof and
  self-diagnosing, which is how a spawn-side failure was finally provable. The lesson for the
  file: when hardening produces no change in symptoms, the failure is upstream of your code.

- **A shipped profile never updates on an existing install.** Import dedupes by profile GUID, so a
  package update leaves whatever was imported first — a dev machine ran the fixed 1.4.0 package for
  a day while the keypad still rendered the layout imported ten days earlier, warning triangle and
  all. The only path that refreshes it today is losing the registration entirely, which makes
  `SelfRegistration.RegisterIfMissing()` rewrite it from the packaged lp5 (verified: delete
  `Applications/Loupedeck70/@_<slug>/Profiles/<GUID>/` with the service stopped, and the service
  reaps the empty registration; the plugin recreates it on next load). A version-aware heal belongs
  next to `RegistrationHeal.cs`. Until it exists, any layout change we ship reaches new installs
  only.

- ~~The Vizhi name gate~~ — RESOLVED 2026-08-19: the hackathon's published rules (OpenAI Devpost,
  Section 8) keep submissions entrant property; the sponsor's only rights are judging plus three
  years of hackathon-promotion use. No commercialization restriction, so "Vizhi for Codex" is
  clear to use — unless a separate signed prize agreement exists, which would control. The real
  brand step was never Devpost: a trademark search + registration for "Vizhi" before the paid
  Apple app ships.
- Repo rename (to something neutral) once 2.0.1 clears review; the submitted package carries GitHub
  URLs, and redirects would cover it, but there is no reason to make QA look twice.
- Per-product CHANGELOG split — one file currently tells Claude Console's story only.
- The Codex plugin icon is generated (teal terminal, no vendor mark). The Logitech asset set is
  Claude Console branding — all eight carry Anthropic's sunburst — so it cannot be reused here.
