# Changelog

All notable changes to Claude Console are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/); this project uses [SemVer](https://semver.org/).

## [1.3.4] — 2026-07-09

### Added
- **Custom icons for prompt keys.** Key icons can now load from `~/.claude/claude-console/icons/<name>.png`
  (checked before the embedded resources), so custom prompts in `prompts.json` can carry their own
  icons — and any built-in icon can be overridden — without rebuilding the plugin. Only simple
  basenames are looked up (no paths), and a missing/unreadable file falls back to the embedded set,
  then to a text label.
- **Voice prompt keys.** A `prompts.json` entry with `"voice": true` becomes a dictation toggle:
  press to start listening (key face shows red "Listening"), speak, press again to stop — the
  entry's prompt text is sent with the transcript appended. Made for slash commands that take
  spoken arguments, e.g. `"prompt": "/apple:brainstorm "` + what you said. Uses the same on-device
  Whisper recorder as the Voice key; one capture at a time (other voice keys are ignored while
  recording).

## [1.3.3] — 2026-07-02

### Fixed
- **Window-nav key icons no longer clash with the tab keys.** Next/Prev Window drew the same
  line-arrow as Next/Prev Tab, set apart only by a circle-vs-square background that was invisible at
  key size — so a window key looked identical to the matching tab key. They now use a solid triangle
  in a square (`arrowtriangle.left/right.square.fill`), differing from the tabs' arrow-in-circle on
  both the glyph *and* the surrounding shape. Regenerate via `tools/generate-icons.swift`.

## [1.3.2] — 2026-07-02

### Added
- **Window navigation** — three keys for people who prefer separate Terminal windows over tabs:
  **New Claude (Window)** (opens a new window already running `claude`), **Next Window** (`Cmd+`` `)
  and **Prev Window** (`Cmd+Shift+`` `). They sit alongside the existing tab keys in the Terminal group.
- **Action descriptions** — every keypad action now carries a one-line description shown in Logi
  Options+ (Answer, Core, Terminal, Git, Prompts, Scroll), so it's clear what each key does before
  you map it. Prompt keys show the exact text they'll type; git keys show the instruction they send.
- **Plugin icon** — replaced the placeholder puzzle-piece with a proper icon (a terminal prompt with a
  spark), reproducible via `tools/generate-plugin-icon.swift`.

## [1.3.1] — 2026-07-02

### Fixed
- **Thread leak that crashed the Logi Plugin Service.** The live-status poller ran on an
  auto-repeating 500 ms timer whose callback shelled out to `osascript` (to find the frontmost
  Terminal tab) and blocked on an un-timed `ReadToEnd()`. A slow or hung `osascript` let poll
  callbacks overlap and pile onto the thread pool, which grew unbounded until `LogiPluginService`
  hit the macOS ~4096-thread limit and aborted (`SIGABRT`) — after which Logi crash-disabled the
  plugin, so its keys showed only an exclamation mark / plain text and eventually vanished until a
  Mac restart reset the count. The poll now runs on a **non-overlapping one-shot timer** (re-armed
  only after each poll finishes), `osascript` calls are bounded by a **hard timeout that kills a
  hung process**, and the frontmost-tab probe is throttled from ~1 s to ~2 s.

### Changed
- Bumped the assembly version to 1.3.1 (a fresh version also sidesteps any stale Logi crash-disable
  marker, which is keyed by assembly version).

## [1.3.0] — 2026-06-26

### Added
- **Live-status bridge auto-wires itself — zero setup.** The live keys (Cost / Context / Model and
  Activity) read `/tmp` state that only gets written when Claude Code is wired to push it via a
  `statusLine` handler and four `hooks`. Previously that meant cloning the repo and hand-editing
  `~/.claude/settings.json`, so a package-only install showed defaults. The plugin now ships both
  scripts embedded in the DLL, writes them to `~/.claude/claude-console/scripts/` on first load, and
  merges the `statusLine` + hooks into `settings.json` itself (`BridgeManager.EnsureBridgeAutoWired`).
  Takes effect on the next Claude Code session.
- Safe by design: backs `settings.json` up once (`settings.json.claude-console.bak`), **merges rather
  than clobbers** — appends a hook only if absent, and **chains** an existing `statusLine` (records it
  to `~/.claude/claude-console/statusline-chain` and runs it through, so a custom status bar still
  renders) — writes atomically, and is idempotent. Opt out with a `~/.claude/claude-console/no-autowire` file.

## [1.2.0] — 2026-06-26

### Added
- **Ready-made keypad layout** (`profiles/ClaudeConsole-Keypad.lp5`) — a one-click importable Logi
  Options+ profile that maps every key (prompts, git, answer, nav, voice, live status), so new users
  get the full layout without assigning keys by hand. Import via Logi Options+ → MX Creative Keypad →
  Import Profile. Bound to Terminal.app; auto-activates when Terminal is frontmost.
- **Uninstall / clean-reinstall** — `scripts/uninstall.sh` plus a README section. The script removes
  the app footprint (voice runtime + ~142 MB model, `/tmp` IPC files, the Microphone grant, any
  crash-disable marker, and a dev `.link`) with a confirmation prompt and a `--dry-run`; the Logi
  Options+ plugin/profile removal and `~/.claude/settings.json` bridge lines stay documented as manual.

### Fixed
- **Assembly version now tracks the release** (`<Version>` in the csproj). The Logi Plugin Service keys
  its crash-disable marker by assembly version; with it pinned at 1.0.0.0, any single load-crash could
  keep the plugin disabled across every rebuild. Versioned builds let a new build dodge a stale marker.

## [1.1.1] — 2026-06-25

### Fixed
- Voice didn't set up from a **package-only install**: the in-package helper + whisper weren't
  installed because `Assembly.Location` is empty in the Loupedeck SDK's plugin load context, so the
  package directory couldn't be found. The plugin now resolves it via the SDK's
  `Plugin.AssemblyFilePath`. Validated end-to-end on the MX Creative Keypad.

## [1.1.0] — 2026-06-25

Offline voice is now self-contained and ships in the package.

### Added
- **Bundled, self-contained `whisper-cli`** — vendored with its dylib closure and relocated to run
  with no Homebrew at runtime (`tools/voice/bundle-whisper.sh`).
- **Speech model auto-downloads** (`ggml-base.en.bin`, ~142 MB) and is checksum-verified on first
  use — no manual download (`BridgeManager.EnsureVoiceModel`).
- **Developer-ID signed + notarized** voice helper (stapled) and whisper bundle, so they pass
  Gatekeeper on other Macs (`tools/voice/sign-and-notarize.sh`).
- The helper + whisper **ship inside the `.lplug4`** and install to `~/.claude/claude-console/` on
  first use (quarantine stripped), so **voice works from a package-only install**
  (`tools/voice/pack-release.sh`, `BridgeManager.EnsureVoiceRuntimeInstalled`).

### Fixed
- whisper.cpp aborted under the hardened runtime (Metal GPU init); the `whisper-cli` build now
  carries the required Metal entitlements (`tools/voice/whisper.entitlements`).

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
