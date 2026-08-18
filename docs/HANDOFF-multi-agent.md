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
| Tests | 540 C# + 47 shell, green, deterministic |

**Verified working on hardware (Codex):** session discovery, the hook state bridge, live
busy/waiting/ready, project name from `cwd`, focus tracking and tab switching, risk-graded
approvals, model, and context percentage. Prompts, git and navigation keys all fire.

**Never tested:** Windows, for either product. The Codex adapter has never run there.

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

## Next, in the order I would do it

1. **Land the branch.** Claude Console 2.1.0 fixes orphaned registrations, which affects users on
   2.0.1 today: uninstalling leaves an entry that claims the terminal and shows nine warning keys
   with no explanation. That is a real bug-fix release independent of any Codex work. Land as the
   no-op-for-Claude refactor release the architecture doc describes, then tag per product
   (`claude-console/v2.1.0`) since bare `v…` tags are now ambiguous.
2. **Write the "install one" note** in both READMEs and both listings. Cheap; prevents the worst
   support case.
3. **Long press.** The keypad delivers only Press/Release (`PressDuration` is always 0), so hold
   detection has to be timed locally — Vizhi does this in `VoiceCommand.cs:57-86`. Nine keys is this
   product's binding constraint and this doubles them. Highest value per unit of work.
4. **Codex's own verbs** — `codex review`, `resume --last`, `fork`, `apply` — into the four slots
   freed on the Codex profile.
5. **Screenshot key**, scoped per agent: Claude Code takes an image mid-conversation via a path;
   Codex only at launch via `-i, --image`. Same key, honestly different meanings. (Clipboard is not
   worth it — it duplicates ⌘V, and Vizhi's version clobbers the clipboard without restoring it.)
6. **Windows**, for either product.

## Commands

```bash
bash tests/run-all.sh                                    # 540 C# + 47 shell
dotnet build src/Products/<Product> -t:Compile           # compile-check only

DOTNET_ROLL_FORWARD=LatestMajor \
  bash tools/voice/pack-release.sh <ver> <Product>       # ClaudeConsole | VizhiCodex

python3 tools/make-codex-profile.py                      # regenerate the Codex layout
bash scripts/uninstall-registration.sh [--remove]        # clean orphaned registrations
```

Version lives in **two** files per product (csproj + `LoupedeckPackage.yaml`) and `ProductVersionTests`
enforces that they agree. The assembly version is what the crash-disable marker keys on.

## Still open

- Which Marketplace listing name the Codex product ships under — "Vizhi for Codex" is chosen but
  gated on the hackathon IP answer about the Vizhi name.
- Repo rename (to something neutral) once 2.0.1 clears review; the submitted package carries GitHub
  URLs, and redirects would cover it, but there is no reason to make QA look twice.
- Per-product CHANGELOG split — one file currently tells Claude Console's story only.
- The Codex plugin icon is generated (teal terminal, no vendor mark). The Logitech asset set is
  Claude Console branding — all eight carry Anthropic's sunburst — so it cannot be reused here.
