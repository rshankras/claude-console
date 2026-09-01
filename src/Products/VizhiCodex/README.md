# Vizhi for Codex

**Physical hardware controls for [OpenAI Codex CLI](https://developers.openai.com/codex/cli) on the Logitech MX Creative Keypad.**

> Press a button. Ship code.

Vizhi for Codex turns the keypad's nine LCD keys into a control surface for Codex CLI: live
session status, one-press prompts and git actions, approval keys that grade the risk of what
they're approving, native `/review`, screenshots that land in the running conversation, and
fully offline voice dictation. No cloud, no API keys — everything runs on your machine.

Built on the same engine as [Claude Console](../../../README.md): one core, one adapter per
agent, one package per product. *(Not an OpenAI product; Codex is a trademark of OpenAI.)*

> Vizhi for Codex and Claude Console can be installed side by side: since 1.6.0 / 2.2.0 neither
> binds an application, so the Logi Plugin Service has nothing to arbitrate — put each product's
> keys on whatever profile you like. (Earlier versions both claimed Terminal.app and only one won.)

## Status

**Preview.** Verified on real hardware on macOS (Apple Silicon): session discovery, the hook
state bridge, live busy/waiting/ready, focus tracking and tab switching, risk-graded approvals,
model and context display, voice, screenshot, and every prompt/git/navigation key.

**Windows (1.6.0): a different state transport, and one honest gap.** Codex's hook runner
creates no process on Windows — proven on hardware, and an upstream issue, not something a
plugin can fix ([the spike](../../../docs/spike-windows-codex-hooks.md)). So Windows reads
codex's own rollout transcript instead: sessions, project, busy/done/ready and best-effort
context all work, with **no hooks installed and no `/hooks` trust prompt**. Risk-graded
approval lighting is unavailable there — codex publishes no approval event outside the hook
runner, and this keypad never shows a state the agent did not report. Yes/No still answer a prompt
you can see: Yes sends Return and No sends Escape, without typing a word or claiming the prompt was
observed. Windows tab-switching selects the intended session by console identity, including when
two tabs have the same title.

## The layout

| Page | Keys |
|---|---|
| 1 — Sessions & interaction | Codex sessions ×3 · **Screenshot** · **Voice** · **Voice Draft** · Esc · No · Yes |
| 2 — Codex controls | Model · Plan · Skills · Agent · Fork · Resume · Review · **Context gauge** · Compact |
| 3 — Prompts | Explore · Explain · Document · Optimize · Refactor · Fix Bug · Code Audit · Write Tests · Security |
| 4 — Terminal | Voice "Go to Project" · New Tab · New Codex · Prev/Next Tab · Exit · Up · Enter · Down |
| 5 — Git | Git Status · Diff · Log · Commit · Push · Create PR |

Highlights that are Codex-specific:

- **Screenshot → current conversation.** Press, drag a region (the system's own ⇧⌘4 picker),
  and the path is typed into your running session with an instruction Codex's model follows to
  open the image. Add your question, press Return.
- **Native `/review`.** One press opens Codex's review picker — uncommitted changes, against a
  base branch, or a commit. (Page 3's Code Audit is different on purpose: it types a structured
  review *prompt* into the current conversation.)
- **Codex power controls.** Plan, Agent, Fork, Skills, and Resume open Codex's own TUI
  workflows. The keypad does not imitate their menus; it opens the native picker and leaves the
  terminal arrows and Enter available on the following page.
- **Context at a glance.** The gauge shows remaining capacity, turns amber below 25% and red below
  10%. Press for Codex `/status`; hold to run `/compact`.
- **Approvals you can read from across the room.** Yes/No and the session key light amber when
  Codex wants permission — red when the pending command is destructive (`git push`, `rm -rf`,
  `sudo`…).
- **Voice, fully offline** — [whisper.cpp](https://github.com/ggml-org/whisper.cpp), notarized
  helper, on-device model. **Voice** submits; **Voice Draft** types without submitting so you
  can fix a mis-hearing.

## What you won't find, and why

The keypad never shows a value the agent did not report:

- **No Cost key** — Codex bills a subscription and reports no spend; a `$0.00` would be
  indistinguishable from a free session.
- **No autocomplete Tab / generic Mode-cycle keys** — Codex has no equivalent interaction; its
  supported native `/plan` workflow has a dedicated Plan key instead.
- **Context %** is read best-effort from Codex's session transcript, a format OpenAI documents
  as unstable — when it surprises us the key shows nothing rather than a stale number.

## Install (macOS and Windows preview)

1. **Logi Options+ 6.4+** with the MX Creative Keypad set up.
2. Double-click `VizhiCodex_<version>.lplug4` → Options+ asks to install.
3. **Import the layout.** The plugin is universal — no application binding, no layout of its
   own — so nothing appears until you give it keys. From the release, import
   `VizhiCodex-Keypad.lp5` on macOS or `VizhiCodex-Windows.lp5` on Windows: Options+ → keypad →
   profile menu → **Import Profile**. It lands on Terminal or Windows Terminal with the Codex keys
   populated. Or add the terminal yourself and drag the **Vizhi for Codex** actions on by hand.
4. **Trust the hooks on macOS.** Your next Codex session opens with **"Hooks need review — 7 hooks are
   new or changed"**. All seven are this plugin: one per lifecycle event, each running the same
   one-line launcher (`~/.codex/codex-console/scripts/codex-hook.sh`), which writes state files
   for the keypad and nothing else. Review and trust; Codex trusts by hash, so it's one-time.
   Until trusted, keys show static labels and no live state — `/hooks` reopens the review if
   you skipped it. (Never use `--dangerously-bypass-hook-trust` — it disables the review for
   everything, not just this plugin.)
   Windows installs no hooks and shows no trust prompt; it reads Codex's rollout transcript.
5. **Grant permissions as the OS asks.** On macOS:
   - **Accessibility** (Logi Plugin Service) — how keys type into Terminal.
   - **Microphone** (first Voice press).
   - **Screen Recording** (first Screenshot press).
   On Windows, allow microphone access for desktop apps when using voice; text injection and
   screenshots need no additional permission prompt.

## Building from source

```bash
dotnet build src/Products/VizhiCodex -t:Compile          # compile check
bash tests/run-all.sh                                    # full suite
DOTNET_ROLL_FORWARD=LatestMajor \
  bash tools/voice/pack-release.sh <ver> VizhiCodex      # pack the .lplug4
```

See the repo root's [CLAUDE.md](../../../CLAUDE.md) and
[docs/multi-agent-architecture.md](../../../docs/multi-agent-architecture.md) for the
architecture — the platform seam, the agent seam, and why each product ships separately.

## Privacy & license

Everything on-device; nothing leaves the machine — [PRIVACY.md](../../../PRIVACY.md).
MIT — [LICENSE](../../../LICENSE).
