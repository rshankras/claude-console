# Changelog

All notable changes to Claude Console are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/); this project uses [SemVer](https://semver.org/).

## [1.8.13] — 2026-08-08

### Fixed
- **The reinstall self-heal now works on Windows too.** Field-testing on the laptop confirmed
  Windows loses the application registration on reinstall exactly like macOS. The detection is
  shared; the restart is not: Windows' `LogiPluginService.exe` is a plain process, not a system
  service — nothing respawns it after a kill — so the heal relaunches it explicitly (finding
  the exe from its own process, since the plugin runs inside it) and then bounces the Options+
  window, mirroring the macOS flow.

## [1.8.12] — 2026-08-08

### Changed
- Plugin author now reads **S.Ravi Shankar** in Options+ (package author + copyright metadata).

## [1.8.11] — 2026-08-08

### Fixed
- **The Options+ window comes back on its own after a heal.** The self-heal restarts the
  Options+ background agent, and launchd respawns it windowless — so the window the user was
  installing from stayed closed until they reopened it by hand. The heal (and
  `scripts/repair-registration.sh`) now explicitly reopens Options+ once the service is back.

## [1.8.10] — 2026-08-08

### Fixed
- **Back-to-back reinstalls each heal.** 1.8.9's self-heal keyed its install-event check on
  service uptime, which also suppressed a legitimate reinstall arriving within minutes of the
  previous heal's restart (found in the field within minutes of shipping). The gate is now
  "payload written after the current service started" — true for every real install, false by
  construction for the reload that follows our own restart, so consecutive reinstalls all heal
  and a restart loop remains impossible.
- The heal now waits 10 s before restarting the service so Options+ finishes its install flow
  first — the restart reads as a blink rather than an install error.

## [1.8.9] — 2026-08-08

### Fixed
- **Reinstalling no longer loses the Claude Console application in Options+.** Any reinstall —
  upgrade, install-over, or uninstall-then-install — runs an uninstall step that drops the
  application registration from the running service's memory while leaving it on disk, and the
  install step never re-registers over an existing directory: the icon vanished from Options+
  and the keypad fell back to the default profile until the service restarted. Nothing in the
  package can prevent it (the installer consults neither the packaged profile nor the
  registration on a reinstall — verified against four package variations), so the plugin now
  heals it: when a load is the install itself (service up for minutes, payload written seconds
  ago) and the on-disk registration predates the payload, it schedules one service restart,
  which rebuilds the application list from disk. A clean first install never triggers it, a
  cold start can never loop it, and a marker caps it at one restart per installed payload.
  Options+ will blink once a few seconds after a reinstall — that's the heal.

### Changed
- The packaged profile now carries its own package identity the way healthy plugin profiles do
  (a distinct package GUID, `packageName` self-reference, and the `@_claudeconsole` application
  binding instead of the legacy Terminal-export binding), and its embedded version finally
  tracks the plugin version. Hygiene alignment with the Vizhi profile shape; the reinstall fix
  above is the self-heal, not this.

## [1.8.8] — 2026-08-08

### Fixed
- **The packaged layout's preview finally admits it has session keys.** The profile that installs
  the 9-key layout imported the correct top row all along (Session 1/2/3 — each slot repaints live
  with context %, project and activity), but its Options+ preview strip still showed the retired
  Context / Working / Mode row from an older export. The preview now matches the layout,
  thumbnails included, on both the macOS and Windows profiles.
- **The Options+ actions sidebar opens on Claude Console's actions instead of System Actions.**
  The profile carried `nativePluginName: null` — an artifact of its origin as a plain Terminal.app
  profile export, from before the plugin owned an application entry — so Options+ had no plugin to
  scope the sidebar to and fell back to System Actions. The profile now names ClaudeConsole as its
  native plugin, the same stamp every healthy plugin profile carries. The plugin's actions were
  always available via All Actions; only the default view was wrong.

## [1.8.6]–[1.8.7] — 2026-08-07

Net change against 1.8.5: none. 1.8.6 split the Windows helper payload into its own package
folder on the theory that sharing one `bin/` with macOS broke application registration on a clean
install; the theory was wrong — the identical, known-good 1.7.1 package failed the same way on
the same machine, so the package was never the variable (the machine's disturbed Logi state was)
— and the split tripled the package to ~35 MB. 1.8.7 reverted it. One `bin/` serves both
platforms again (~26 MB), and Windows was verified working on hardware with the shared layout.

## [1.8.5] — 2026-08-07

### Fixed
- **A fresh macOS install registered no application, so the keypad layout never imported.**
  `ClaudeConsoleApplication.GetProcessName()` was hardcoded to `"WindowsTerminal"` in 1.8.0 — but
  that class runs on both platforms, so on macOS it named a process that does not exist and the
  service silently registered nothing. The plugin still loaded and its actions still appeared;
  only the application row was empty, with nothing in any log. Existing installs were unaffected
  because their registration was already on disk, which is why it took a clean install to find.
  The name is now chosen at runtime.

## [1.8.4] — 2026-08-07

### Added
- **Windows support.** One package now serves macOS and Windows. Every key group works on both:
  prompts, git, answer/menu keys, control keys, live context/cost/model/activity, session slots
  with pinning and the approval badge, terminal navigation, scroll, and offline voice.
- **Platform seam.** Everything OS-specific moved behind `IPlatformBridge`; `BridgeManager` keeps
  the platform-neutral half (poll loop, targeting, slot assignment, project matching, auto-wire
  policy). The action classes used to pass raw AppleScript through the bridge — they now name
  intents (`InjectKey(KeyStroke.ArrowUp)`), which is what made a second backend additive rather
  than invasive. macOS behaviour is unchanged.
- Four Windows helper executables ship in the package: `claude-console-inject` (types into a
  session's console), `claude-console-hook` (statusline + activity), `claude-console-focus`
  (selects the Windows Terminal tab), `claude-console-voice` (microphone + whisper).

### How Windows differs
- **Typing addresses the console handle, not the focused window** — a keypress reaches the intended
  Claude session or nothing at all, and cannot leak into another application even in principle.
- **Sessions are keyed by process + start time**, never by window title: every Claude tab reports
  the identical title and rewrites it to a conversation summary once chatting starts. The start
  time is what stops a recycled PID inheriting a dead session's slot and pin.
- **The keypad layout is imported by hand** — a package can carry only one auto-imported profile
  per device type, and that one declares the macOS binding. See the README.
- **No frontmost-tab tracking.** One session works with no pin; beyond that, press a session key
  first. Pinning is exact.
- **Next/Previous Window is unavailable** (`wt.exe` cannot express it) and opening a project always
  takes a fresh tab rather than guessing whether the current one is busy.

### Fixed
- macOS: `pack-release.sh` built without `SkipPluginLink`, dropping a dev `.link` beside the
  installed package — the service then rejected both with "already loaded" and the plugin failed
  to load. It now passes the flag and clears any stale link before packing.

## [1.7.1] — 2026-08-06

### Added
- **The keypad layout installs itself.** Claude Console is now an application plugin associated
  with Terminal.app (`HasApplication` + a real bundle id in `ClaudeConsoleApplication`), and the
  package ships `profiles/DefaultProfile70.lp5` — so a fresh install registers a "Claude Console"
  application in Logi Options+ and auto-imports the full 9-key layout (Sessions on top, Clear /
  Voice / Esc, Yes / No / Tab), no manual profile import. The mechanism is the one Vizhi uses.
  The pre-1.5 attempt at this crashed and disabled the plugin because `HasApplication` was enabled
  while the application class still returned empty names — an application with no identity. With
  the bundle id filled in, it registers cleanly.

## [1.7.0] — 2026-08-06

Both features answer the same piece of user feedback: sometimes you want to LOOK at what's about
to be sent — a voice transcript with a mis-heard word, a canned prompt that needs one more clause —
before it goes. The shared mechanism is drafting: type into Claude's input box, don't press Return,
let the user finish the thought and submit it themselves.

### Added
- **Voice Draft key** (Universal group). Same press-to-record, press-to-transcribe flow as Voice,
  but the transcript is only typed — not sent. Fix whatever whisper misheard, then submit with
  Return (keyboard or the keypad's Return key). Voice itself is unchanged: it still types and
  sends in one go. Two keys rather than a mode, so both behaviours are always one press away and
  no press depends on invisible state. Its icon is a mic WITH a waveform — still reads as a voice
  key (a first-cut pencil glyph didn't, per user feedback), but its silhouette differs from Voice's
  plain mic so the two keys don't blur together at key size.
- **The default prompts are now worth a key.** Users pointed out that "Explain how this code
  works" or "Refactor this for clarity" add nothing over typing the one word yourself. Every
  default prompt has been rewritten to carry what an expert would actually type: it scopes itself
  to something concrete (the uncommitted diff, the code under discussion — or it asks), names a
  method, and says what the output should look like. Review asks for file:line + severity + a
  failure scenario per finding; Optimize demands evidence of the bottleneck before touching
  anything; Deploy stops for approval before anything goes public. An **unedited** pre-1.7
  prompts.json is upgraded in place — the seed file wins over the built-ins, so without this no
  existing install would ever see the new prompts. Any edit at all (a reworded prompt, a swapped
  icon, an added key) marks the file as yours and blocks the upgrade.
- **Designer icon set adopted** across every key — the July icon pack's "custom coloured"
  variant (38 hand-drawn line icons, softer harmonized palette) replaces the SF Symbols
  originals. Threshold and model variants (gauge amber/red, the three model brains) are
  recoloured from the designer's own glyphs at build time, Voice Draft is composed from the
  designer's mic plus stroke-matched wave bars, and the few icons the pack predates (Deploy,
  hourglasses, window nav) are regenerated in the designer's palette so nothing looks foreign.
  The plugin tile is the pack's dark-terminal-with-starburst mark. Sources live in
  `assets/designer-icons/`; `tools/convert-designer-icons.swift` renders them, and
  `tools/generate-icons.swift` now owns only the leftovers — the two scripts can't overwrite
  each other's output.
- **Session-key face redesigned**, iterated against photos of the real keypad. The project name
  is now the key's single-line label — the largest, crispest text the hardware can render, in the
  same style as every other key — instead of sharing a shrunken two-line label with the context %.
  The % moved onto the face itself: bold, readable, and colour-coded with the Context gauge's
  thresholds (white, amber at 75%, red at 90%), so a session that needs /compact flags itself
  from across the room. Also: the pin brackets are thicker so the selected session reads at
  arm's length; the approval badge gets a halo ring for a clean silhouette; and when a pinned
  session is waiting on you — the one state that matters most — the badge owns the top-right
  corner instead of being drawn over the bracket.
- **`"submit": false` on prompt keys.** Any entry in `~/.claude/claude-console/prompts.json` can
  now be a draft key: it types its prompt and leaves the cursor in the input box, so a stem like
  "Explain how this code works, focusing on " becomes a fill-in-the-blank. Absent means `true` —
  every existing prompts.json keeps its type-and-send behaviour. The README documents the flag
  (and the fact, easy to miss, that the prompt keys were always fully customizable through that
  file — labels, icons, prompt text, and how many keys there are).

## [1.6.2] — 2026-08-06

### Fixed
- **Picking a session didn't stick — keys drifted back to whatever Terminal tab was in front.**
  Pressing a session key set the target in the same field the ~2.5s frontmost-tab poll overwrites,
  so the choice survived only until the next poll. Select session 2, glance back at session 1, then
  press Clear, and `/clear` wiped session 1 — the exact thing the grid exists to prevent. (A press
  landing while a poll's `osascript` was still in flight was reverted outright, before you could
  look anywhere.) A session key now **pins** its session: every key — Yes / No / Clear / Compact /
  Esc / prompts / voice — and the live Model / Cost / Context / Activity readouts stay on it until
  you press another session key or it exits. Switching Terminal tabs no longer moves the target,
  which is the whole point of answering session 2 while you're reading session 1. With nothing
  pinned the keys still follow the frontmost tab, so single-session use is unchanged.
- The pin is released automatically when its session exits, so the keys can never be stranded on a
  closed tab, and "Go to Project" drops it — that opens a new session, and holding the old pin
  would have sent your next keypress to the wrong one.
- The pin persists across a plugin reload, alongside the slot assignments (this is what
  `focused_session` in `registry.json` was always for — it was written into the schema but never
  read or set).

## [1.6.1] — 2026-08-06

First release actually exercised on hardware. Three bugs, all of which made the plugin look broken
on a real keypad; none were reachable from the tests as they stood.

### Fixed
- **Every session key read "Claude" instead of the project name.** Claude Code sends `null` — not
  `0` — for context and cost figures it doesn't have yet, which is the normal state of a session
  that hasn't done any work. Those fields were declared as non-nullable numbers, so a single `null`
  made the *entire* status-line payload fail to parse; the session was discarded and re-added by the
  process scan as an unnamed placeholder. **This affected every live key** (Model / Cost / Context /
  Activity), not just the grid — on a fresh session they all silently showed defaults. All payload
  numbers are now nullable, and a session with no context usage yet shows a blank rather than a
  misleading "0%".
- **Nothing could be typed or focused when a Terminal window had no tabs.** Terminal raises
  "Can't get every tab of item N of every window" (-1728) for such windows — a settings or inspector
  window is enough — which aborted the whole focus script. Since every typing key runs that script
  first, **Yes / No / prompts / voice all silently did nothing**, and pressing a session key didn't
  bring its tab up. Such windows are now skipped.
- **Session keys flickered, and presses hit "empty" slots.** The process scan runs on every 4th
  poll; on the polls in between, "no scan" was being treated as "nothing is alive", so sessions
  vanished and reappeared twice a second. The last known scan is now carried over.
- Session keys no longer clip their bottom line — the icon was crowding the two-line label.

## [1.6.0] — 2026-08-06

### Added
- **The keypad now tells you what it's asking to approve.** When Claude wants permission, **Yes** and
  **No** light with a badge — **amber** for a routine request, **red** when the pending command is
  destructive or hard to undo (`sudo`, `rm -rf`, `git push`, `reset --hard`, `terraform apply`,
  piping a download into a shell, and similar). The session key showing that session goes red too, so
  across a set of sessions you can see *which* one wants attention and whether to look first.
  - Driven by a new `PermissionRequest` hook, wired automatically like the others. It fires the
    moment a tool needs approval and carries the tool and its input. (`Notification` can't do this —
    it carries no tool name, and Claude Code delays it about six seconds for permission prompts, so
    approving quickly means it never fires at all.)
  - The badge is a **hint, not a gate**: Claude Code's own prompt is still what holds the command.
  - Older Claude Code builds ignore the unknown hook, so the badge simply stays amber there.
  - Risk matching is anchored on word boundaries, so `workforce` isn't mistaken for `--force` and
    `confirm.sh` isn't mistaken for `rm`.

### Fixed
- **The Activity key's "Waiting" state works again.** It tested a `status` field the status line
  never sends, so it silently read Ready forever; it now follows the session grid and the hooks.

## [1.5.0] — 2026-08-06

### Added
- **A key per Claude session.** The new **Sessions** group gives each running Claude Code session
  its own LCD key showing its project name, context usage, and whether it's working, waiting on you,
  or ready. Press one to jump to that Terminal tab — and to point every other key at it, so you can
  approve a prompt in session 2 while looking at session 1, or at a browser. Six slots fill a page
  alongside Yes / No / Voice.
  - **Slots are stable.** A session keeps its key for as long as it lives; when one exits the others
    do *not* shuffle, so muscle memory can't send an approval to the wrong session. The freed key is
    reused by the next session to start.
  - Sessions are discovered from a `ps` scan as well as the status line, so a brand-new session
    lights a key immediately (labelled "Claude" until it first renders), and a **closed tab clears
    within ~2 seconds** instead of lingering on a timer.
  - Slot assignments are remembered across a plugin reload.

### Changed
- **Typing keys follow the selected session.** With one session running nothing changes. With
  several, keys target the session you last selected or focused; if you haven't chosen and exactly
  one session is waiting on you, that one is used. When it's genuinely ambiguous the plugin declines
  to guess and the injection guard beeps rather than typing into the wrong session.

### Fixed
- **The Activity key's "Waiting" fallback never worked.** It tested a `status` field that Claude
  Code's status line does not send, so without the hooks the key always read Ready. README claimed
  otherwise; both are corrected.

## [1.4.0] — 2026-08-06

### Changed
- **Now targets .NET 10** to match the updated Logi Plugin Service (PluginApi 6.4 is built on the
  .NET 10 runtime; building against it from net8.0 fails with CS1705). `minimumLoupedeckVersion`
  is now 6.4 — install the current Logi Options+ before updating the plugin.
- **Keys now focus Claude's Terminal tab before typing.** Previously every typing key (Yes/No,
  prompts, git, Esc, Tab, voice…) injected into whatever app was frontmost — glance at Slack,
  press Yes, and "yes⏎" landed in Slack. Injection is now guarded: a single AppleScript run first
  activates Terminal.app and selects the tracked tab (verified by its TTY), then types; if
  Terminal isn't running or the tab is gone, it beeps and types **nothing**. Strict Terminal.app
  by design. Prompt text also now travels as an osascript argument instead of being escaped into
  the script source.

### Added
- **A test suite** (`bash tests/run-all.sh`) — 47 xUnit tests plus 15 bash-script checks, covering
  the injection guard (focus precedes typing; prompt text is passed as an argument, never
  interpolated into the script), IPC file permissions and symlink refusal, stale-file pruning, TTY
  normalisation, and voice project matching. The plugin's PostBuild target is now gated behind
  `SkipPluginLink`, so running the tests can't write the dev `.link` into the live Logi plugin
  directory or hot-reload the running service.

### Security
- **All IPC moved into a private root.** Session state, activity flags, and voice transcripts —
  which carry your prompts, cwd, and dictation — were world-readable loose files in `/tmp`. They
  now live under `/tmp/claude-console/` with owner-only permissions (0700 dirs / 0600 files),
  symlink refusal on the plugin side, and an ownership guard in the bash scripts. Legacy loose
  `/tmp/claude-console-*` files are cleaned up on plugin load.
- **Removed `cmd-queue.jsonl`** — an append-only, never-read, world-readable log of every prompt
  key ever pressed. Nothing consumed it; it no longer exists.
- **Stale-file pruning.** Per-tab state/activity/voice files from dead sessions are now deleted
  after 10 minutes; previously they accumulated until reboot.
- **Bounded reads + settings hardening.** The plugin ignores IPC files over 1 MB and refuses to
  rewrite `~/.claude/settings.json` through a symlink.

## [1.3.4] — 2026-07-10

### Changed
- **Go to Project animates while it listens.** Pressing it drew a static "Listening" label while the
  Voice key animated a green equalizer, so two keys doing the same thing looked different. Both now
  share one `ListeningFace` — the same wave frames, the same cadence — and the duplicated frame timer
  is gone from `VoiceCommand`.

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
