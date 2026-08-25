# Vizhi Desktop

**Physical hardware controls for the ChatGPT / Codex desktop app on the Logitech MX Creative Keypad.**

> Answer your agent without going to it.

Vizhi Desktop watches the desktop app through the macOS Accessibility API and puts its approval
prompt on a physical key. When Codex needs permission to run something, the key lights — amber for
routine work, red when the command it is asking about looks destructive — and one press answers it
**without the app ever coming to the front**. Your editor keeps focus. Nothing is typed into the
wrong window, because nothing is typed at all.

Built on the same engine as [Claude Console](../../../README.md) and
[Vizhi for Codex](../VizhiCodex/README.md): one core, one adapter per surface, one package per
product. *(Not an OpenAI product; ChatGPT and Codex are trademarks of OpenAI.)*

> **This one coexists.** Unlike the two terminal consoles — which both bind to Terminal.app and
> therefore cannot both be active — Vizhi Desktop binds to the ChatGPT app's own bundle, which
> nothing else claims. You can run it alongside Claude Console or Vizhi for Codex.

## Status

**Preview, macOS only.** The Accessibility mechanism is verified end to end on a live app:
reading the approval card unfocused, pressing Allow/Deny with another app frontmost and no focus
theft, writing and submitting the composer, and switching the app between its ChatGPT and Codex
modes — all while the app sat in the background. Windows needs a different mechanism (UI
Automation) and its recon has not been run.

## Setup — the part that matters

Installing gives you an auto-imported page that appears **when the ChatGPT app is frontmost**.
That page is useful, but it is not the point of this product, because a Logi application profile
is only active while its application is in front — and the whole idea here is answering the agent
while you are somewhere else.

**So the first thing to do after installing is place four keys on your default profile**, which is
active no matter what you are looking at:

| Key | What it gives you |
|---|---|
| **Activity** | Working / Waiting / Ready at a glance — and *Hidden* when the app's window is locked or gone, because "cannot see" is not the same as "nothing to do" |
| **Approve** | Answers the pending request. Amber for routine, **red** when the command looks destructive — press **Show ChatGPT** if you want the exact wording before deciding |
| **Deny** | The other half. Lit only while something is actually pending |
| **Show ChatGPT** | The escape hatch: brings the app forward when you want to look before deciding |

In Logi Options+, select your default profile, then drag those four actions from **Vizhi Desktop**
onto whichever keys you like.

**Why you have to do this by hand:** your default profile is your configuration. A plugin that
imported itself over it would wipe whatever you already had there, so this product doesn't. It is
one placement, once.

## The auto-imported page

Bound to the app, so it appears while ChatGPT is frontmost — the keys that only make sense when
you are actually looking at it:

| Key | Action |
|---|---|
| **Activity** | Same status face as above |
| **Mode** | Switch between ChatGPT and Codex. The key shows the mode you are in; pressing moves to the other |
| **New Chat** | Fresh conversation |
| **Voice** | Hold a thought, speak it, and it lands in the composer and sends. Fully offline (whisper.cpp), no cloud, no API key |
| **Stop** | Interrupt the running task |

Approve and Deny are deliberately **not** duplicated here. If you are looking at the app, the card
is on your screen — a key that appears only when you do not need it teaches the wrong idea of
where answering happens.

## What it will not do

- **No "Always allow" key.** The app offers it; this keypad does not bind it. Standing permission
  to run anything should not be one elbow away on a physical device.
- **No cost or context keys.** The desktop app publishes neither, and this keypad never shows a
  number the agent did not report. (The terminal consoles show cost for Claude Code because Claude
  Code reports it.)
- **No typing into the wrong window.** Every action addresses the app's controls directly through
  Accessibility rather than sending keystrokes, so there is no focused window to get wrong.

## Permissions

- **Accessibility** — for the Logi Plugin Service, so the plugin can read and press the app's
  controls. macOS will ask once.
- **Microphone** — only if you use the Voice key, and granted to the small bundled helper rather
  than to the service. Shared with the other consoles: one helper, one grant, one speech model.

## Requirements

- macOS 13+, Apple Silicon or Intel
- The ChatGPT desktop app, signed in
- Logi Options+ with the Logi Plugin Service 6.4 or newer
- MX Creative Keypad
