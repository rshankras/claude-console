# Privacy Policy

_Last updated: 2026-08-21_

**Applies to both keypad plugins built from this repository — Claude Console (for Claude Code) and
Vizhi for Codex (for OpenAI Codex CLI), on macOS and Windows.** They share one engine, and
everything below is true of both except where a product or platform is named.

**These plugins run entirely on your own computer. They have no servers and no accounts, and they
never transmit your prompts, audio, screenshots, or session data anywhere.** There is exactly one
network request, ever: a one-time download of the offline speech model (see below).

## Voice / microphone

- When you press a voice key, audio is captured by the bundled helper (`ClaudeVoiceHelper.app`)
  and transcribed **on-device** by [whisper.cpp](https://github.com/ggerganov/whisper.cpp).
- **Your audio never leaves your computer.** It is not uploaded, streamed, or sent to any server —
  including Anthropic or OpenAI. There is no cloud speech service involved.
- The recording is written to a temporary file (`/tmp/<product>/voice/capture.wav`, or
  `%TEMP%\<product>\voice\` on Windows) only long enough to transcribe it, and is overwritten on
  the next use. The transcript is likewise temporary, and both are deleted automatically once
  stale. You may delete them at any time.
- The microphone is used **only** while a voice key is actively recording.

## The one network request

The first time you press a voice key, the plugin downloads the `base.en` speech model (~142 MB)
from Hugging Face so transcription can run offline from then on:

`https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin`

This is a plain file download — it sends no information about you, your prompts, or your machine
beyond what any download requires, and it is verified against a known checksum before use. It
happens **once**; after that the plugin makes no network requests at all. To avoid it entirely,
place `ggml-base.en.bin` at `~/.claude/claude-console/whisper/` yourself before first use.

## Session state

- The plugin stores what the keys display — model name, cost and token counts where the agent
  reports them, context percentage, activity, and the project directory — under a directory it
  owns: `/tmp/claude-console/` or `/tmp/codex-console/` (`%TEMP%\…` on Windows), one per product.
- **This data is owner-only.** The directory is created with `0700` permissions and its files with
  `0600`, so no other user account can read your prompts, dictation, or session state. Files from
  closed sessions are deleted automatically. (On Windows the equivalent per-user location is used.)
- Nothing in this directory is transmitted anywhere. It is read only by the plugin.

## What the plugin reads from the agent

To display live state, each plugin reads what its agent already writes on your machine:

- **Claude Code** — the status line and lifecycle hooks it produces, which the plugin wires by
  adding entries to `~/.claude/settings.json` — only when you turn the live keys on (a press, confirmed); nothing
  is written on install. Any status line you already had is chained, not replaced, and turning
  them off (a long press) removes exactly the plugin's entries.
- **Codex CLI (macOS)** — lifecycle hooks, installed as the plugin's own `~/.codex/hooks.json`.
  Your `config.toml` is never edited. Codex asks you to trust these hooks before they run.
- **Codex CLI (Windows)** — Codex's hook runner does not spawn processes on that platform, so the
  plugin instead **reads Codex's own local session transcript** (`~/.codex/sessions/**/rollout-*.jsonl`)
  to know when a turn starts and ends. It reads only the events it needs and stores none of the
  conversation. The same transcript is read on both platforms for the context-percentage key.

## Screenshots

The Screenshot key captures a screen region **you select**, using the system's own picker
(`screencapture -i` on macOS, the Windows snip overlay). The image is saved to the plugin's own
directory (above) and its file path is typed into your agent session so the agent can open it. The
image is never uploaded by the plugin; what your agent does with a file you point it at is governed
by that agent's own terms. Captures are not deleted automatically — you may remove them at any time.

## Permissions used

**macOS**

- **Microphone** — granted to the voice helper, for local transcription only.
- **Accessibility** — granted to the Logi Plugin Service, so the plugin can type text and
  keystrokes into your agent's Terminal tab.
- **Screen Recording** — required by macOS for the Screenshot key; used only while you are
  capturing.

**Windows**

- **Microphone** — enabled for desktop apps in Settings, for local transcription only.
- The plugin grants Codex's sandbox user group (`CodexSandboxUsers`, created by Codex's own setup)
  read access to its install directory and write access to its own state directory, so Codex can
  launch and feed the plugin's state hooks. No other permissions on your system are modified.

## Data collection

These plugins collect **no** analytics, telemetry, or personal data, and transmit nothing off your
device.

> Note: Claude Code and Codex CLI communicate with Anthropic and OpenAI respectively, under
> [Anthropic's](https://www.anthropic.com/legal) and [OpenAI's](https://openai.com/policies) own
> terms and privacy policies. These plugins only read the local state those tools already produce
> on your machine, and type input into them as you direct.

Questions: file an issue at the project repository.
