# Changelog

All notable changes to Claude Console are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/); this project uses [SemVer](https://semver.org/).

## [1.0.0] — 2026-06-25

Initial release.

### Added
- MX Creative Keypad plugin (`LogitechCreativeFamily`) for Claude Code.
- Live status keys: model, cost, and context usage from the Claude Code status line.
- **Per‑tab live status** — with multiple Claude Code sessions in different Terminal tabs, the
  Model/Cost/Context/Activity keys follow the frontmost tab. Sessions write per‑TTY state/activity
  files; the plugin reads the one matching the frontmost Terminal tab (Terminal.app only).
- One‑press prompt keys (Fix Bug, Write Tests, Explain, Refactor, Review, Optimize, Security, Document, Deploy).
- Git keys (Commit, Diff, Push, Create PR, Status, Log) and control keys (Mode, Compact, Context, Clear, Exit).
- **Tab** control key — accepts the highlighted autocomplete and submits it in one press (Tab, then Return).
- **Mode** key — sends Shift+Tab to cycle Claude Code's input modes (normal → auto-accept edits → plan).
- **Model** key — opens Claude Code's `/model` picker and shows the current model live as a colour‑coded brain.
- Terminal/session navigation (activate, new tab, new Claude session, next/prev tab).
- **Offline voice dictation** via a bundled whisper.cpp helper — press, speak, transcribe locally, type into the terminal.
- **Voice "Go to Project"** — speak a project name; live folder scan + fuzzy match → new tab `cd` + `claude`.
- SF Symbol key icons.
- File‑based IPC bridge (`/tmp`) and companion status‑line / hook scripts.

### Known limitations
- macOS (Apple Silicon) only.
- Terminal navigation targets Terminal.app.
- whisper.cpp + model are not yet bundled (installed separately); see [SUBMISSION.md](SUBMISSION.md).
- Accept/Reject permission hook is experimental and off by default.
