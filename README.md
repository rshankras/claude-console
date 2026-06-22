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
- **Model & modes** — a **Model** key opens the `/model` picker and shows the current model live; **Mode** cycles Claude Code's input modes (normal → auto‑accept edits → plan); plus Compact, Context, Clear.
- **Accept autocomplete** — **Tab** completes a slash‑command / `@file` suggestion and runs it in one press.

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

## Live activity indicator (working / waiting / done)

The **Activity** key can show — at a glance — whether Claude is **working**, **waiting on you**, or **ready**, which is handy for watching a long agentic run from across the room. It's driven by Claude Code *hooks* that push activity to the keypad. Add them to `~/.claude/settings.json` (merge into any existing `hooks` block):

```json
{
  "hooks": {
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "bash /ABSOLUTE/PATH/TO/claude-console/scripts/activity-hook.sh busy" }] }],
    "PostToolUse":      [{ "matcher": "*", "hooks": [{ "type": "command", "command": "bash /ABSOLUTE/PATH/TO/claude-console/scripts/activity-hook.sh busy" }] }],
    "Notification":     [{ "hooks": [{ "type": "command", "command": "bash /ABSOLUTE/PATH/TO/claude-console/scripts/activity-hook.sh waiting" }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "bash /ABSOLUTE/PATH/TO/claude-console/scripts/activity-hook.sh done" }] }]
  }
}
```

Restart Claude Code so the hooks take effect. Without them, the Activity key still shows **Waiting** on a permission prompt and **Ready** otherwise. The Context key also turns **amber at 75%** and **red at 90%** so you compact before an auto-compaction.

## Using voice

- **Voice key** — press (you'll hear a *Tink*), say your prompt, press again. It transcribes locally and types the text into the focused terminal.
- **Go to Project** — press, say a project name (e.g. *"indie app autopilot"*), press again. Opens a new tab in that project running `claude`; reuses an idle shell tab, or opens a new one if `claude` is already running.

First use prompts once for **Microphone** permission (granted to the helper, not the daemon). The plugin also needs **Accessibility** permission for the Logi Plugin Service (to type into the terminal).

## Answering Claude's questions

When Claude asks something, answer from the keypad instead of the keyboard:

- **Up / Down / Return** — navigate and confirm a selection menu: tool‑permission prompts, multiple‑choice questions (`AskUserQuestion`), plan‑mode confirmation.
- **Yes / No** — type `yes` / `no` + Enter, for plain‑text questions ("Should I proceed?"). They type the word, so they won't select a *numbered* menu item — use Up/Down + Return for those.

These inject keystrokes into the focused terminal, so keep it frontmost (same Accessibility permission as the prompt keys).

## Accepting autocomplete & switching modes

- **Tab** — completes Claude Code's highlighted suggestion (a `/slash` command or an `@file` mention) **and submits it** in one press, so you can fire a slash command without the keyboard. Because it always presses Return, it also sends `@file` completions and half‑typed commands — use **Up / Down / Return** if you want to complete *without* sending.
- **Mode** — sends **Shift+Tab**, which cycles Claude Code's input modes shown at the bottom of the TUI: **normal → auto‑accept edits → plan**. From normal, one press lands on auto‑accept edits and a second reaches plan mode.

Both inject keystrokes into the frontmost terminal (same Accessibility permission as the other keys).

## Scrolling the conversation

**Scroll Up / Scroll Down** page back and forth through the Claude Code transcript so you can read earlier messages without touching the keyboard. They send Page Up / Page Down to the focused terminal and work in both rendering modes:

- **Classic mode** (default) — Claude Code leaves the conversation in the terminal's scrollback, so these scroll Terminal natively. Keep a generous scrollback limit (Terminal ▸ Settings ▸ Profiles ▸ Window ▸ Scrollback) so there's history to scroll through.
- **Fullscreen mode** (`/tui fullscreen`) — Claude Code scrolls its own buffer by half a screen.

Like the prompt and answer keys, these inject into the frontmost terminal, so keep it focused.

## Customizing prompt keys

The **Prompts** keys are defined in `~/.claude/claude-console/prompts.json` (seeded with the defaults on first run). Edit it to bind your own prompts and macros — each entry becomes its own bindable key:

```json
[
  { "id": "ship", "label": "Ship", "icon": "create_pr",
    "prompt": "Run the tests; if green, commit with a conventional message and open a PR." },
  { "id": "standup", "label": "Standup", "icon": "log",
    "prompt": "Summarize what we changed today as 3 standup bullets." }
]
```

- **`id`** — unique key id · **`label`** — text under the icon · **`prompt`** — typed into the terminal on press.
- **`icon`** — an embedded icon basename; its baked colour is the key's colour. Pick from: `fix_bug`, `write_tests`, `explore`, `explain`, `refactor`, `review`, `optimize`, `security`, `document`, `deploy`, `commit`, `diff`, `push`, `create_pr`, `status`, `log`, `project`, `terminal` (an unknown name falls back to text).

Reload the plugin (rebuild, or restart Logi Options+) to pick up edits. Delete the file to restore the built-in defaults.

## Key map

| Group | Keys |
|-------|------|
| **Core** | Model* · Cost* · Activity* · Esc · Mode · Tab · Compact · Context · Clear · Exit |
| **Answer** | Yes · No · Up · Down · Return |
| **Prompts** | Fix Bug · Write Tests · Explore · Explain · Refactor · Review · Optimize · Security · Document · Deploy |
| **Git** | Commit · Diff · Push · Create PR · Status · Log |
| **Scroll** | Scroll Up · Scroll Down |
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
