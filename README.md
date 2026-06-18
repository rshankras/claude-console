# Claude Console

**Physical hardware controls for [Claude Code](https://claude.com/claude-code) on the Logitech MX Creative Keypad.**

> Press a button. Ship code.

Claude Console turns the MX Creative Keypad's nine LCD keys into a control surface for Claude Code: live session status, one‑press prompts and git actions, answering its questions, terminal navigation, and **fully offline voice** — dictate a prompt or jump into a project by speaking its name. No cloud, no API keys.

---

## Features

- **Live status** — Model, live cost, and context usage read straight from Claude Code's status line.
- **One‑press prompts** — Fix Bug, Write Tests, Explain, Refactor, Review, Optimize, Security, Document, Deploy.
- **Answer prompts** — respond to Claude's questions from the keypad: Up/Down/Return to navigate menus, Yes/No to type a quick reply.
- **Git, through Claude** — Commit, Diff, Push, Create PR, Status, Log.
- **Terminal & session nav** — activate Terminal, new tab, new Claude session, next/prev tab.
- **Offline voice dictation** — press, speak, press again; [whisper.cpp](https://github.com/ggerganov/whisper.cpp) transcribes locally and types it into your terminal.
- **Voice "Go to Project"** — say a project name; it scans your folders, fuzzy‑matches, and opens a new tab `cd`'d into the project with `claude` running.
- **Model switching** — Opus / Sonnet / Haiku, plus Plan, Compact, Context, Clear.

See [PRIVACY.md](PRIVACY.md) — everything runs on your Mac.

## Requirements

- **macOS on Apple Silicon** (whisper.cpp uses Metal).
- **Logitech MX Creative Keypad** + **Logi Options+** (installs the *Logi Plugin Service*).
- **[Claude Code](https://claude.com/claude-code)** CLI.
- To build from source: **.NET 8 SDK** and the **Logi Plugin Tool** (`dotnet tool install --global LogiPluginTool`).
- For voice: **whisper.cpp** (`brew install whisper-cpp`) and a ggml model (see below).

## Install (build from source)

```bash
# 1. Build the plugin — links + hot-reloads into the Logi Plugin Service
cd src
dotnet build -c Debug

# 2. Build the offline voice helper (signed app bundle, installed to ~/.claude/claude-console)
cd ..
bash tools/voice/build.sh

# 3. Get a whisper model (~142 MB, base.en is a good speed/accuracy balance)
mkdir -p ~/.claude/claude-console/whisper
curl -L -o ~/.claude/claude-console/whisper/ggml-base.en.bin \
  https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin
```

A pre‑packaged install via the Logitech Marketplace is planned — see [SUBMISSION.md](SUBMISSION.md).

## Connect the live status bridge

The live Cost / Model / Context keys read `/tmp/claude-console-state.json`, written by the bundled status‑line handler. Wire it into Claude Code once, in `~/.claude/settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "bash /ABSOLUTE/PATH/TO/claude-console/scripts/statusline-handler.sh"
  }
}
```

(The handler captures session state for the plugin and prints no visible status line — customize `scripts/statusline-handler.sh` if you want one.)

## Using voice

- **Voice key** — press (you'll hear a *Tink*), say your prompt, press again. It transcribes locally and types the text into the focused terminal.
- **Go to Project** — press, say a project name (e.g. *"indie app autopilot"*), press again. Opens a new tab in that project running `claude`; reuses an idle shell tab, or opens a new one if `claude` is already running.

First use prompts once for **Microphone** permission (granted to the helper, not the daemon). The plugin also needs **Accessibility** permission for the Logi Plugin Service (to type into the terminal).

## Answering Claude's questions

When Claude asks something, answer from the keypad instead of the keyboard:

- **Up / Down / Return** — navigate and confirm a selection menu: tool‑permission prompts, multiple‑choice questions (`AskUserQuestion`), plan‑mode confirmation.
- **Yes / No** — type `yes` / `no` + Enter, for plain‑text questions ("Should I proceed?"). They type the word, so they won't select a *numbered* menu item — use Up/Down + Return for those.

These inject keystrokes into the focused terminal, so keep it frontmost (same Accessibility permission as the prompt keys).

## Key map

| Group | Keys |
|-------|------|
| **Core** | Model* · Cost* · Status* · Esc · Plan · Compact · Context · Clear · Exit · Opus · Sonnet · Haiku |
| **Answer** | Yes · No · Up · Down · Return |
| **Prompts** | Fix Bug · Write Tests · Explore · Explain · Refactor · Review · Optimize · Security · Document · Deploy |
| **Git** | Commit · Diff · Push · Create PR · Status · Log |
| **Terminal** | Terminal · New Tab · New Claude · Next Tab · Prev Tab · **Go to Project** (voice) |
| **Universal** | **Voice** |

*\* live display, updates from the status line.*

## How it works

```
MX Creative Keypad → Logi Plugin Service → C# plugin (BridgeManager)
                                                  ↕  file IPC in /tmp
Claude Code ← status line (bash) + voice helper (Swift + whisper.cpp)
```

File‑based IPC in `/tmp` (`claude-console-*.json`); action keys type into the terminal via `osascript`; voice records through a signed helper app that owns its own Microphone permission. Full architecture and packaging notes in [SUBMISSION.md](SUBMISSION.md).

## Building & packaging

`dotnet build` hot‑reloads the plugin during development. To produce a Marketplace package (`.lplug4`) and the bundling/signing steps, see **[SUBMISSION.md](SUBMISSION.md)**.

## License

[MIT](LICENSE). Bundled third‑party components (whisper.cpp, the Whisper model) are MIT‑licensed. See [EULA.md](EULA.md).
