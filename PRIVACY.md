# Privacy Policy

_Last updated: 2026-06-17_

**Claude Console runs entirely on your Mac. It has no servers, no accounts, and makes no network calls of its own.**

## Voice / microphone

- When you press a voice key, audio is captured by the bundled helper (`ClaudeVoiceHelper.app`) and transcribed **on‑device** by [whisper.cpp](https://github.com/ggerganov/whisper.cpp).
- **Your audio never leaves your computer.** It is not uploaded, streamed, or sent to any server — including Anthropic. There is no cloud speech service involved.
- The recording is written to a temporary file (`/tmp/claude-console-voice.wav`) only long enough to transcribe, and is overwritten on the next use. You may delete it at any time.
- The microphone is used **only** while a voice key is actively recording.

## Session state

- The status‑line handler writes Claude Code session metadata (model name, cost, token counts, context percentage) to `/tmp/claude-console-state.json` so the plugin can display it on the keys. This file stays on your machine and is read only by the plugin.

## Permissions used

- **Microphone** — granted to the voice helper, for local transcription only.
- **Accessibility** — granted to the Logi Plugin Service, so the plugin can type text/keystrokes into your terminal.

## Data collection

Claude Console collects **no** analytics, telemetry, or personal data, and transmits nothing off your device.

> Note: Claude Code itself communicates with Anthropic under [its own terms and privacy policy](https://www.anthropic.com/legal). Claude Console only reads the local status line Claude Code already produces.

Questions: file an issue at the project repository.
