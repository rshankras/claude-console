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

**Windows (1.6.0): official hooks plus a recovery fallback.** Current Codex supports Windows
command hooks through `commandWindows`, including the exact `PermissionRequest` event used for
risk-graded approval lighting. Vizhi installs those hooks and keeps the rollout reader as a
fallback while hooks await trust, for older clients, and for interrupted-turn recovery. Windows
tab-switching selects the intended session by console identity, including when two tabs have the
same title.

## The layout

| Page | Keys |
|---|---|
| 1 — Sessions & interaction | Codex sessions ×3 · Esc · No · Yes · **Screenshot** · **Dictate** · **Draft** |
| 2 — Codex controls | Model · Plan · Skills · Review · **Context gauge** · Compact · Up · Enter · Down |
| 3 — Prompts | Explore · Explain · Document · Optimize · Refactor · Fix Bug · Code Audit · Write Tests · Security |
| 4 — Terminal | Voice "Go to Project" · New Tab · New Codex · Prev/Next Tab · Exit · Agent · Fork · Resume |
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
  menu arrows and Enter available on page 2. Plan sends the native Shift+Tab toggle rather than
  repeatedly entering /plan; a customized CLI keymap may require restoring that shortcut.
  When the Agent workflow opens `codex agents` in a separate tab, tap the pinned session again
  to release targeting, then focus that tab before using the keypad arrows.
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
  native Plan toggle has a dedicated key instead.
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
4. **Trust the hooks.** Your next Codex session opens with **"Hooks need review — 7 hooks are
   new or changed"**. All seven are this plugin: one per lifecycle event, each running the same
   one-line launcher (`~/.codex/codex-console/scripts/codex-hook.sh`), which writes state files
   for the keypad and nothing else. Review and trust; Codex trusts by hash, so it's one-time.
   Until trusted, keys show static labels and no live state — `/hooks` reopens the review if
   you skipped it. (Never use `--dangerously-bypass-hook-trust` — it disables the review for
   everything, not just this plugin.)
   On Windows the reviewed command uses Codex's `commandWindows` override and the packaged
   `claude-console-hook.exe`; rollout polling remains available as a fallback.
5. **Grant permissions as the OS asks.** On macOS:
   - **Accessibility** (Logi Plugin Service) — how keys type into Terminal.
   - **Microphone** (first Voice press).
   - **Screen Recording** (first Screenshot press).
   On Windows, allow microphone access for desktop apps when using voice; text injection and
   screenshots need no additional permission prompt.

## Updating profiles and prompts

For an existing customized keypad, follow [the export, backup and explicit update workflow](../../../docs/profile-updates.md).
A plugin upgrade does not rearrange an imported profile. Vizhi prompt settings now live at
`~/.codex/vizhi/prompts.json`; the first load copies a valid Claude prompt list if present, then
future edits are independent. An existing Vizhi file always wins. Invalid migration sources
stay untouched and are retried on reload after correction.

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

## Compatibility notes for the acceptance fixes

On macOS, provisional session keys use the live Codex process directory before the first hook.
They say **Ready**, not Complete. After a conversation ends during Fork/Resume, old context and
session identity are cleared until a new hook arrives; the project remains visible.

Vizhi installs executable speech components under `~/.codex/vizhi-runtime/`; Claude Console keeps
its existing runtime location. The large model remains shared under
`~/.claude/claude-console/whisper/`. Alternating products cannot replace each other's helper.
First microphone permission at the new helper location still requires device verification.

Project discovery remains shared. A nonempty `~/.claude/claude-console/project-roots` overrides
automatic search; clear its entries to restore automatic discovery, or add project parent folders.
No personal project path is built into the plugin. No-match feedback now remains on the key for
8 seconds. Prompt configuration also remains shared; Code Audit is a Vizhi display label and no
longer rewrites Claude's Review label in an unedited configuration file.
