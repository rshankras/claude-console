# Vizhi for Codex 1.4.4 — preview notes

*For initial review. Not yet on the Marketplace; Claude Console 2.0.1 is in Marketplace review
separately.*

## What this is

A second keypad product from the Claude Console codebase: **Vizhi for Codex** gives OpenAI's
Codex CLI the same physical control surface Claude Console gives Claude Code — live session
keys, risk-graded approvals, one-press prompts and git verbs, native `/review`, screenshot
into the running conversation, and fully offline voice dictation.

The point of the preview is as much the **architecture** as the product: one engine now builds
a package per agent. `src/Core` is agent-neutral (platform bridges, session grid, file IPC,
actions, voice, self-registration); `src/Agents/<agent>` is a small adapter (~a file per
concern) declaring what the agent can honestly report; `src/Products/<name>` pairs them with
branding, a profile, and a version. Supporting a new terminal agent is an adapter plus a thin
product folder — the engine, tests, and packaging are shared.

Two design rules the product is built around:

- **A key never shows a value the agent did not report.** Codex bills a subscription and
  reports no spend, so there is no Cost key rather than a fake `$0.00`. No autocomplete, no
  Tab key. Capability flags per adapter decide, and the packaged profile agrees with the code.
- **A press never lands in the wrong window.** Every typing key resolves Codex's own Terminal
  tab and types there atomically, or beeps and types nothing.

## Verified on hardware (macOS, Apple Silicon)

Session discovery · hook state bridge · live busy/waiting/ready · project name · focus
tracking and tab switching · risk-graded approvals (amber/red) · model + context display ·
voice (submit and draft) · screenshot → current conversation · native `/review` · all prompt,
git, and navigation keys.

**Windows: untested for this product.** The package carries the Windows payload (Claude
Console 2.0 is Windows-verified on this engine), but the Codex adapter has not run there yet.
Please preview on macOS.

## Install

1. Logi Options+ **6.4+**, MX Creative Keypad, [Codex CLI](https://developers.openai.com/codex/cli) installed natively.
2. Double-click `VizhiCodex_1.4.4.lplug4` → install via Options+.
3. Wait ~1 minute: the plugin self-registers its application + layout and restarts the plugin
   service once (Options+ blinks and returns on its own).
4. In Codex, run `/hooks` → **trust** the `vizhi-codex` entry → start a new session. Codex
   trusts hooks by hash, so this is a one-time, deliberate step — live state on the keys
   begins here.
5. Grant, once each as macOS prompts: **Accessibility** (typing), **Microphone** (voice),
   **Screen Recording** (screenshot).

## Known limitations (preview)

- **One console per machine** — this and Claude Console both bind Terminal.app; the plugin
  service activates one plugin per application, so installing both leaves one silently dead.
  A unified dual-agent console is the planned answer for users of both.
- Context % is read best-effort from a Codex transcript format documented as unstable — the
  key shows nothing rather than a stale number when parsing surprises us.
- Layout changes in a future package don't reach an already-imported profile (import
  de-duplicates by profile id); a version-aware heal is on the roadmap.

## Ask us about

The two-seam architecture (`docs/multi-agent-architecture.md`), the risk classifier behind
the approval keys, the offline voice pipeline (notarized helper + whisper.cpp, no network),
or what a third agent would take to add.
