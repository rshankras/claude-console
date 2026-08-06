# Privacy Policy

_Last updated: 2026-08-06_

**Claude Console runs entirely on your Mac. It has no servers and no accounts, and it never transmits your prompts, audio, or session data anywhere.** It makes exactly one network request, ever: a one-time download of the offline speech model (see below).

## Voice / microphone

- When you press a voice key, audio is captured by the bundled helper (`ClaudeVoiceHelper.app`) and transcribed **on‑device** by [whisper.cpp](https://github.com/ggerganov/whisper.cpp).
- **Your audio never leaves your computer.** It is not uploaded, streamed, or sent to any server — including Anthropic. There is no cloud speech service involved.
- The recording is written to a temporary file (`/tmp/claude-console/voice/capture.wav`) only long enough to transcribe it, and is overwritten on the next use. The transcript is likewise temporary, and both are deleted automatically once stale. You may delete them at any time.
- The microphone is used **only** while a voice key is actively recording.

## The one network request

The first time you press a voice key, the plugin downloads the `base.en` speech model (~142 MB) from Hugging Face so transcription can run offline from then on:

`https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin`

This is a plain file download — it sends no information about you, your prompts, or your machine beyond what any download requires, and it is verified against a known checksum before use. It happens **once**; after that the plugin makes no network requests at all. To avoid it entirely, place `ggml-base.en.bin` at `~/.claude/claude-console/whisper/` yourself before first use.

## Session state

- The status‑line handler writes Claude Code session metadata (model name, cost, token counts, context percentage, and the project directory) under `/tmp/claude-console/` so the plugin can display it on the keys.
- **As of 1.4.0 this data is owner‑only.** The directory is created with `0700` permissions and its files with `0600`, so no other user account on the Mac can read your prompts, dictation, or session state. Files from closed sessions are deleted automatically.
- Earlier versions wrote these files with default permissions, which on a shared Mac left them readable by other local accounts. Version 1.4.0 removes any such leftovers on first load.
- Nothing in this directory is transmitted anywhere. It is read only by the plugin.

## Permissions used

- **Microphone** — granted to the voice helper, for local transcription only.
- **Accessibility** — granted to the Logi Plugin Service, so the plugin can type text/keystrokes into Claude's Terminal tab.

## Data collection

Claude Console collects **no** analytics, telemetry, or personal data, and transmits nothing off your device.

> Note: Claude Code itself communicates with Anthropic under [its own terms and privacy policy](https://www.anthropic.com/legal). Claude Console only reads the local status line Claude Code already produces.

Questions: file an issue at the project repository.
