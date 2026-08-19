# Vizhi for Codex

**Physical hardware controls for [OpenAI Codex CLI](https://developers.openai.com/codex/cli) on the Logitech MX Creative Keypad.**

> Press a button. Ship code.

Vizhi for Codex turns the keypad's nine LCD keys into a control surface for Codex CLI: live
session status, one-press prompts and git actions, approval keys that grade the risk of what
they're approving, native `/review`, screenshots that land in the running conversation, and
fully offline voice dictation. No cloud, no API keys — everything runs on your machine.

Built on the same engine as [Claude Console](../../../README.md): one core, one adapter per
agent, one package per product. *(Not an OpenAI product; Codex is a trademark of OpenAI.)*

> **⚠️ Install one console per machine.** Vizhi for Codex and Claude Console both bind to
> Terminal.app, and the Logi Plugin Service activates only one plugin per application — the
> other silently loses its keys. If you use both agents, pick one console for now; a unified
> dual-agent console is on the roadmap.

## Status

**Preview.** Verified on real hardware on macOS (Apple Silicon): session discovery, the hook
state bridge, live busy/waiting/ready, focus tracking and tab switching, risk-graded approvals,
model and context display, voice, screenshot, and every prompt/git/navigation key. **Windows is
untested for this product** — the package carries the Windows payload, but the Codex adapter has
never run there. Preview on macOS.

## The layout

| Page | Keys |
|---|---|
| 1 | Codex sessions ×3 · **Screenshot** · **Voice** · **Voice Draft** · Yes · No · Esc |
| 2 | **Review** (native `/review`) · Model · Compact · Up · Enter · Down · Scroll ↑ · Scroll ↓ · Clear (`/new`) |
| 3 | Nine one-press prompts (Explore, Explain, Review, Optimize, Refactor, Write Tests, Document, Fix Bug, Security) |
| 4 | Voice "Go to Project" · New Tab · Next/Prev Tab · **New Codex** · Exit |
| 5 | Git through Codex: Commit · Create PR · Diff · Log · Push · Status |

Highlights that are Codex-specific:

- **Screenshot → current conversation.** Press, drag a region (the system's own ⇧⌘4 picker),
  and the path is typed into your running session with an instruction Codex's model follows to
  open the image. Add your question, press Return.
- **Native `/review`.** One press opens Codex's review picker — uncommitted changes, against a
  base branch, or a commit. (Page 3's Review is different on purpose: it types a structured
  review *prompt* into the current conversation.)
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
- **No Tab / Mode keys** — Codex has no autocomplete-accept or input-mode cycle; a key that
  does nothing reads as broken.
- **Context %** is read best-effort from Codex's session transcript, a format OpenAI documents
  as unstable — when it surprises us the key shows nothing rather than a stale number.

## Install (macOS preview)

1. **Logi Options+ 6.4+** with the MX Creative Keypad set up.
2. Double-click `VizhiCodex_<version>.lplug4` → Options+ asks to install.
3. **Let one blink happen.** Within a minute the plugin registers a "Vizhi for Codex"
   application in Options+ and imports the keypad layout by itself, then restarts the plugin
   service once — the Options+ window closes and reopens on its own. Don't quit Options+
   during that first minute.
4. **Trust the hook.** Codex trusts lifecycle hooks by hash: run `/hooks` inside Codex, trust
   the `vizhi-codex` entry, then start a new session. Until then keys show static labels and no
   live state. (Never use `--dangerously-bypass-hook-trust` — it disables the review for
   everything, not just this plugin.)
5. **Grant permissions as macOS asks**, each once:
   - **Accessibility** (Logi Plugin Service) — how keys type into Terminal.
   - **Microphone** (first Voice press).
   - **Screen Recording** (first Screenshot press).

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
