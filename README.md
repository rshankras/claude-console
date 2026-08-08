# Claude Console

**Physical hardware controls for [Claude Code](https://claude.com/claude-code) on the Logitech MX Creative Keypad. macOS and Windows.**

> Press a button. Ship code.

Claude Console turns the MX Creative Keypad's nine LCD keys into a control surface for Claude Code: live session status, one‑press prompts and git actions, answering its questions, terminal navigation, and **fully offline voice** — dictate a prompt or jump into a project by speaking its name. No cloud, no API keys.

---

## Features

- **Live status** — Model, live cost, and context usage read straight from Claude Code's status line.
- **One‑press prompts** — Fix Bug, Write Tests, Explore, Explain, Refactor, Review, Optimize, Security, Document, Deploy. One-word keys, **full structured prompts** underneath — and all of it [customizable](#customizing-prompt-keys): reword, add, remove, or make any key a **draft** you edit before sending.
- **Answer prompts** — respond to Claude's questions from the keypad: Up/Down/Return to navigate menus, Yes/No to type a quick reply.
- **Git, through Claude** — Commit, Diff, Push, Create PR, Status, Log.
- **Terminal & session nav** — activate Terminal, new tab, new Claude session, next/prev tab. Prefer windows over tabs? There are **New Claude (Window)** and **Next/Prev Window** keys too.
- **Offline voice dictation** — press, speak, press again; [whisper.cpp](https://github.com/ggerganov/whisper.cpp) transcribes locally and types it into your terminal. **Voice** sends it straight away; **Voice Draft** leaves it in the input box so you can fix mis-hearings first.
- **Voice "Go to Project"** — say a project name; it scans your folders, fuzzy‑matches, and opens a new tab `cd`'d into the project with `claude` running.
- **Model & modes** — a **Model** key opens the `/model` picker and shows the current model live; **Mode** cycles Claude Code's input modes (normal → auto‑accept edits → plan); plus Compact, Context, Clear.
- **A key per session** — run Claude in several Terminal tabs and each gets its own key showing project, state and context usage. Press one to focus that tab and point every other key at it.
- **See what you're approving** — Yes/No and the session's own key light **amber** when Claude wants permission, and **red** when the pending command is destructive (`git push`, `rm -rf`, `sudo`…). [Legend](#the-approval-badge).
- **Accept autocomplete** — **Tab** completes a slash‑command / `@file` suggestion and runs it in one press.
- **Types where it should** — every key finds Claude's own Terminal tab and types there, so a press can't land in Slack or a browser because you glanced away. Can't find it? It beeps and types nothing.

See [PRIVACY.md](PRIVACY.md) — everything runs on your own machine.

## Requirements

**Both platforms**

- **Logitech MX Creative Keypad** + **Logi Options+ 6.4 or newer** (installs the *Logi Plugin Service*). 1.4.0 onwards is built for the .NET 10 runtime that ships with Plugin Service 6.4 — on an older Options+ the plugin will not load, so let Options+ update itself first. (Staying on an older Options+? Use [1.3.4](https://github.com/rshankras/claude-console/releases).)
- **[Claude Code](https://claude.com/claude-code)** CLI, installed **natively** (WSL sessions are not visible to the plugin — see [Windows notes](#windows-notes)).

**macOS**

- **Apple Silicon** (whisper.cpp uses Metal).
- Terminal.app. Every typing key targets Claude's own Terminal tab.
- To build from source: **.NET 10 SDK**, the **Logi Plugin Tool** (`dotnet tool install --global LogiPluginTool`), and for voice **whisper.cpp** (`brew install whisper-cpp`) — needed only to *build* the bundled `whisper-cli`. The speech model downloads automatically on first use.

**Windows**

- **Windows 10/11**, x64 or arm64.
- **Windows Terminal** for the tab/window navigation keys. Typing works in Windows Terminal, classic conhost **and** VS Code's integrated terminal — injection addresses the console directly, not the focused window — but the nav keys drive `wt.exe`, so they need Windows Terminal.
- The keypad layout is **imported by hand**, not auto-installed — see [Windows notes](#windows-notes).

## Windows notes

The Windows build reaches Claude a different way than macOS does, and two differences are worth knowing before you install.

**Typing is if anything safer than on macOS.** macOS focuses Claude's Terminal tab and types into it in one atomic AppleScript run. Windows writes key events straight to the target session's *console handle*, which has no relationship to the focused window at all — a keypress reaches the intended Claude session or nothing whatsoever, and it cannot leak into another application even in principle.

**Import the layout by hand.** A plugin package can carry exactly one auto-imported default profile per device type, and that file has to declare a single application binding — which on this cross-platform package is the macOS one. So on Windows: Options+ → your keypad → profile menu (`⋯`) → **Import** → pick [`profiles/ClaudeConsole-Windows.lp5`](profiles/ClaudeConsole-Windows.lp5), and choose **windowsterminal** as the application. (You can also just drag Claude Console's actions onto keys yourself; nothing depends on the profile.)

**The keypad can't follow your eyes between tabs.** Windows Terminal exposes no supported way to ask which tab is in front, so with several idle sessions open the plugin can't tell which one you're looking at. One session needs no pin and just works; so does "exactly one session is waiting on you". Beyond that, **press a session key first** — pinning is exact, and every subsequent key goes to that session until you pin another. Pressing a session key also brings its tab to the front.

**Not available on Windows:** Next/Previous *Window* (an OS-level gesture `wt.exe` cannot express — those keys log and do nothing). Opening a project always uses a fresh tab rather than reusing an idle one, because Windows Terminal offers no way to tell a busy tab from an idle one and guessing wrong would type into a live session.

**Elevated sessions are unreachable.** Options+ runs unelevated, and Windows blocks the console attach across integrity levels. Run Claude unelevated.

## Install (released plugin)

Download the latest `ClaudeConsole_<ver>.lplug4` from [**Releases**](https://github.com/rshankras/claude-console/releases), then:

1. **Double-click it** — Logi Options+ registers the plugin. (Or, with the Logi Plugin Tool: `logiplugintool install ./ClaudeConsole_<ver>.lplug4`.) If macOS blocks it, right-click → **Open**, or run `xattr -dr com.apple.quarantine ClaudeConsole_<ver>.lplug4`.
2. That's it for the layout — since 1.7.1 the install **registers a "Claude Console" application in Options+ and imports the 9-key layout by itself** (Sessions on top, Clear / Voice / Esc, Yes / No / Tab; more pages behind it). Rearrange or rebind any key afterward. (On older versions, import the layout by hand — see [Import the ready-made layout](#import-the-ready-made-layout) below.)
3. On first use, grant **Accessibility** to the Logi Plugin Service (so it can type into your terminal). For **voice**, press the Voice key and grant **Microphone** when prompted — the helper and speech model install themselves on first use.

> Everything works straight from the download — including the live **Model / Cost / Context / Activity** keys. On first load the plugin installs its status-line + hook scripts and wires them into `~/.claude/settings.json` for you, so the live keys light up on your **next Claude Code session** with no setup. (Details, and how to opt out, in [The live status bridge](#the-live-status-bridge) below.)

## Import the ready-made layout

> **Since 1.7.1 this happens automatically on install** — the package carries the profile and the
> plugin registers its own Options+ application. The steps below are only for older versions, or
> if you deleted the auto-imported profile and want it back without reinstalling.

Rather than mapping nine keys by hand, import the bundled profile to get the full layout instantly:

1. Download **`ClaudeConsole-Keypad.lp5`** from [**Releases**](https://github.com/rshankras/claude-console/releases) (alongside the `.lplug4`), or take it from [`profiles/`](profiles/) in this repo.
2. In **Logi Options+** → your **MX Creative Keypad**, open the profile menu (the `⋯` / profile dropdown) → **Import Profile** → pick the `.lp5`.
3. It installs as **Claude Console — Keypad**, bound to **Terminal**, so it activates automatically whenever Terminal.app is frontmost. Prompts, git, answer, navigation, voice, and the live status keys are all pre‑mapped.

Notes:
- Install the plugin first (step 1 above) so the imported keys resolve to real actions.
- Import once. If you later reinstall or update the plugin, your profile stays put — just reinstall the plugin and the keys light up again; no need to re‑import.
- The profile is bound to Apple's **Terminal.app**, and so are the keys themselves: since 1.4.0 every typing key (prompts, answers, voice) focuses Claude's Terminal.app tab before it types — it will not type into iTerm2/Ghostty/Warp or any other app.
- It's only a starting point — rebind or rearrange any key afterward.

## Install (build from source)

```bash
# 1. Build the plugin — links + hot-reloads into the Logi Plugin Service
cd src
dotnet build -c Debug

# 2. Build the voice helper AND bundle a self-contained whisper-cli
#    (both installed to ~/.claude/claude-console; no Homebrew needed at runtime)
cd ..
bash tools/voice/build.sh
```

The ~142 MB `base.en` whisper model is fetched automatically (and checksum-verified) the first time
you press the Voice key — no manual download. To pre-seed it, just drop `ggml-base.en.bin` at
`~/.claude/claude-console/whisper/`.

A pre‑packaged install via the Logitech Marketplace is planned — see [SUBMISSION.md](SUBMISSION.md).

## The live status bridge

The live keys read state files under a private `/tmp/claude-console/` directory that Claude Code writes via a status‑line handler (Cost / Model / Context ← `sessions/`) and four hooks (Activity ← `activity/`). Everything in it is owner‑only (0700 dirs / 0600 files), so your prompts and session state are never readable by other users on the Mac.

**This is set up automatically — no action needed.** On first load the plugin writes both scripts to `~/.claude/claude-console/scripts/` and merges the `statusLine` + hooks into `~/.claude/settings.json` for you. It's careful about it: backs `settings.json` up first (`settings.json.claude-console.bak`), only **appends** a hook when it isn't already present, and **chains** an existing `statusLine` (records yours and runs it through, so your custom status bar still renders) rather than overwriting it. The live keys come alive on your **next Claude Code session** — Claude Code reads hooks/statusLine at session start, so a session already running won't pick them up. To opt out, create an empty file at `~/.claude/claude-console/no-autowire` before first load.

<details>
<summary>Wire it by hand instead (e.g. if you opted out)</summary>

Add this to `~/.claude/settings.json` — the scripts live at `~/.claude/claude-console/scripts/` (or use `scripts/` from a clone). Merge the `hooks` into any existing block:

```json
{
  "statusLine": {
    "type": "command",
    "command": "bash ~/.claude/claude-console/scripts/statusline-handler.sh"
  },
  "hooks": {
    "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh busy" }] }],
    "PostToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh busy" }] }],
    "Notification":      [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh waiting" }] }],
    "Stop":              [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh done" }] }],
    "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh permission" }] }]
  }
}
```

The status‑line handler captures session state for the plugin and prints no visible status line. Restart Claude Code so the changes take effect.
</details>

Without the hooks the **Activity** key reads **Ready** and never changes — its working / waiting / done detail, handy for watching a long agentic run from across the room, comes entirely from the hooks above. (Versions before 1.5.0 claimed it could still show **Waiting** on a permission prompt without them; it never could.) The **Context** key needs only the status line, and turns **amber at 75%** / **red at 90%** so you compact before an auto‑compaction.

## Several sessions at once

Run Claude in more than one Terminal tab and each session gets **its own key** in the **Sessions** group — project name, context usage, and a face for working / waiting / ready. Press one to jump to that tab.

Pressing a session key also **pins every other key to it**, which is the point: you can approve a prompt in session 2 while looking at session 1, or while reading a browser. The pin **holds** until you press another session key or that session exits — switching Terminal tabs does not move it. With only one session running, nothing changes — the keys just work, no selection needed. If you haven't picked a session and exactly one is waiting on you, that's the one that gets your **Yes**. When it's genuinely ambiguous the plugin won't guess; it beeps instead of answering the wrong Claude.

Notes:
- **Slots are stable.** A session keeps its key for as long as it lives — close one and the others stay put, so you don't approve the wrong session out of muscle memory. The freed key is reused by the next session you start.
- A session waiting for approval shows a badge on its key — see [The approval badge](#the-approval-badge) for what the colours mean.
- The pin survives a plugin reload, and is released automatically if its session exits, so the keys are never stranded on a closed tab. **Go to Project** drops it too, since that starts a session somewhere new.
- A new session takes a key **immediately** (labelled "Claude" until it first renders a status line), and a closed tab clears within about two seconds.
- Six sessions fit; beyond that the extras run fine, just without a key.
- Terminal.app only, like the rest of the plugin.

## Using voice

- **Voice key** — press (you'll hear a *Tink*), say your prompt, press again. It transcribes locally, types the text into Claude's Terminal tab, **and sends it**.
- **Voice Draft key** — same flow, but the transcript is only **typed, not sent**: it sits in Claude's input box so you can fix anything whisper misheard, then submit with **Return** (keyboard or the keypad's Return key). Use Voice for quick prompts, Voice Draft for anything long enough to mis-transcribe.
- **Go to Project** — press, say a project name (e.g. *"indie app autopilot"*), press again. Opens a new tab in that project running `claude`; reuses an idle shell tab, or opens a new one if `claude` is already running.

Start and stop a recording with the **same** key — each voice key is its own start/stop toggle.

First use prompts once for **Microphone** permission (granted to the helper, not the daemon). The plugin also needs **Accessibility** permission for the Logi Plugin Service (to type into the terminal).

**Voice records but types nothing (empty transcript):** macOS ties the Microphone grant to the helper's code signature, so **re-signing or rebuilding the helper resets it** — and it fails *silently* (no re-prompt). Reset the permission and re-grant on the next press:

```bash
tccutil reset Microphone com.rshankar.claudeconsole.voicehelper
```

A stable Developer‑ID signature (via `tools/voice/sign-and-notarize.sh`) avoids this going forward.

## Answering Claude's questions

When Claude asks something, answer from the keypad instead of the keyboard:

- **Up / Down / Return** — navigate and confirm a selection menu: tool‑permission prompts, multiple‑choice questions (`AskUserQuestion`), plan‑mode confirmation.
- **Yes / No** — type `yes` / `no` + Enter, for plain‑text questions ("Should I proceed?"). They type the word, so they won't select a *numbered* menu item — use Up/Down + Return for those.

### The approval badge

**The keys tell you what you'd be approving.** When Claude asks for permission, a small filled dot appears in the **top-right corner** of the key:

| Badge | Meaning | What to do |
|-------|---------|------------|
| *(none)* | Nothing is waiting for an answer. | — |
| 🟡 **Amber** | Waiting on you, and it's **routine** — reading a file, running a test, an edit. | Press **Yes** without looking. |
| 🔴 **Red** | Waiting on something **destructive or outward-facing**. | Look at the screen first. |

Red is triggered by the pending command matching one of the patterns in [`src/RiskClassifier.cs`](src/RiskClassifier.cs) — `sudo`, `rm -rf`, `git push`, `git reset --hard`, `git clean -fd`, `--force`, `dd of=`, `mkfs`, `chmod 777`, `drop table`, `delete from`, piping a download into a shell, `kubectl delete`, `terraform apply`/`destroy`, `npm publish`, `gh release create`, `killall`, `shutdown`…

The same pending request lights up **three keys at once**:

- **Yes** and **No** — what pressing them *right now* would approve, for whichever session the keys are pinned to.
- **That session's own key** — so with several sessions running you can see *which* one wants attention. It carries the same amber/red distinction.

Two things worth knowing:

- It's a **hint, not a gate.** Claude Code's own prompt is still what actually holds the command, and the classifier deliberately leans toward warning you unnecessarily — a badge that stayed quiet on a real `git push --force` would be worse than one that cries wolf. (Bare generic flags like a lone `-f` are *not* flagged, though: a badge that lights on every other command teaches you to ignore it.)
- **Amber means "answer me", not "the plugin knows what this is."** A session waiting for a plain-text question, an idle prompt, or an older Claude Code build without the `PermissionRequest` hook all show amber. Only **red** is a specific claim about the command.

The badge clears itself as soon as the session stops waiting. It's powered by a `PermissionRequest` hook the plugin wires up for you (see [The live status bridge](#the-live-status-bridge)).

These keys focus Claude's Terminal tab automatically before typing (verified by its TTY), so they work even when another app is frontmost — if Terminal isn't running or the tab is gone, they beep and type nothing. Same Accessibility permission as the prompt keys.

## Accepting autocomplete & switching modes

- **Tab** — completes Claude Code's highlighted suggestion (a `/slash` command or an `@file` mention) **and submits it** in one press, so you can fire a slash command without the keyboard. Because it always presses Return, it also sends `@file` completions and half‑typed commands — use **Up / Down / Return** if you want to complete *without* sending.
- **Mode** — sends **Shift+Tab**, which cycles Claude Code's input modes shown at the bottom of the TUI: **normal → auto‑accept edits → plan**. From normal, one press lands on auto‑accept edits and a second reaches plan mode.

Both focus Claude's Terminal tab before sending keys (same Accessibility permission as the other keys).

## Scrolling the conversation

**Scroll Up / Scroll Down** page back and forth through the Claude Code transcript so you can read earlier messages without touching the keyboard. They send Page Up / Page Down to Claude's Terminal tab and work in both rendering modes:

- **Classic mode** (default) — Claude Code leaves the conversation in the terminal's scrollback, so these scroll Terminal natively. Keep a generous scrollback limit (Terminal ▸ Settings ▸ Profiles ▸ Window ▸ Scrollback) so there's history to scroll through.
- **Fullscreen mode** (`/tui fullscreen`) — Claude Code scrolls its own buffer by half a screen.

Like the prompt and answer keys, these focus Claude's Terminal tab automatically before scrolling.

## Customizing prompt keys

Every **Prompts** key is yours to change. The keys show one-word labels (Explore, Review, …), but each sends a **full, structured prompt** — Review, for instance, asks for a senior-engineer pass with file:line, severity, and a concrete failure scenario per finding; Deploy runs the checks but stops for your approval before anything goes public.

All of it is defined in `~/.claude/claude-console/prompts.json` (seeded with the defaults on first run). Edit it to suit how you work — reword any prompt, relabel or re-icon a key, delete keys you never press, or add your own macros. Each entry becomes its own bindable key, and there's no fixed count:

```json
[
  { "id": "ship", "label": "Ship", "icon": "create_pr",
    "prompt": "Run the tests; if green, commit with a conventional message and open a PR." },
  { "id": "standup", "label": "Standup", "icon": "log",
    "prompt": "Summarize what we changed today as 3 standup bullets." },
  { "id": "explain_this", "label": "Explain…", "icon": "explain", "submit": false,
    "prompt": "Explain how this code works, focusing on " }
]
```

- **`id`** — unique key id · **`label`** — text under the icon · **`prompt`** — typed into the terminal on press.
- **`icon`** — an embedded icon basename; its baked colour is the key's colour. Pick from: `fix_bug`, `write_tests`, `explore`, `explain`, `refactor`, `review`, `optimize`, `security`, `document`, `deploy`, `commit`, `diff`, `push`, `create_pr`, `status`, `log`, `project`, `terminal` (an unknown name falls back to text).
- **`submit`** *(optional, default `true`)* — set `false` to make a **draft key**: it types the prompt but doesn't press Return, so you can edit or finish the sentence before sending it (with Return — keyboard or keypad). The third example above types a stem and leaves the cursor at the end.

Reload the plugin to pick up edits (restart Logi Options+ / `killall LogiPluginService`, or rebuild if you develop from source). Delete the file to restore the built-in defaults.

**Your edits are permanent.** The moment you change anything in the file, it's yours — no plugin update will ever overwrite it. Only a prompts.json still in its untouched factory state is upgraded in place when a new release improves the default prompts (this happened once, in 1.7.0, when the defaults grew from one-liners into real prompts).

## Key map

| Group | Keys |
|-------|------|
| **Sessions** | Session 1-6* — one key per running Claude session (press to focus it and pin the other keys to it) |
| **Core** | Model* · Cost* · Activity* · Esc · Mode · Tab · Compact · Context · Clear · Exit |
| **Answer** | Yes · No · Up · Down · Return |
| **Prompts** | Fix Bug · Write Tests · Explore · Explain · Refactor · Review · Optimize · Security · Document · Deploy |
| **Git** | Commit · Diff · Push · Create PR · Status · Log |
| **Scroll** | Scroll Up · Scroll Down |
| **Terminal** | Terminal · New Tab · New Claude · Next Tab · Prev Tab · New Claude (Window) · Next Window · Prev Window · **Go to Project** (voice) |
| **Universal** | **Voice** · **Voice Draft** |

*\* live display, updates from the status line.*

## How it works

```
MX Creative Keypad → Logi Plugin Service → C# plugin (BridgeManager)
                                                  ↕  file IPC in /tmp/claude-console (owner-only)
Claude Code ← status line (bash) + voice helper (Swift + whisper.cpp)
```

File‑based IPC under a private `/tmp/claude-console/` root (0700 dirs / 0600 files); action keys focus Claude's Terminal tab (verified by TTY) and type via `osascript`; voice records through a notarized helper app that owns its own Microphone permission, then transcribes with a bundled, self‑contained `whisper-cli` (no Homebrew at runtime). Full architecture and packaging notes in [SUBMISSION.md](SUBMISSION.md).

## Troubleshooting

**Keys show only an exclamation mark or plain text, then the whole plugin disappears (you drop to the default profile), and a Mac restart brings it back.** This was a thread leak, **fixed in 1.3.1**: the live‑status poller could accumulate threads until the *Logi Plugin Service* hit the OS thread limit and crashed, which disabled the plugin until the service restarted. **Update to 1.3.1 or later.**

**Keys show only an exclamation mark / plain text right after building from source.** If you've *both* installed the released `.lplug4` *and* run `dotnet build` (which writes a dev `.link`), the plugin is registered twice and the service refuses the duplicate — the plugin log shows `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` and the keys don't resolve. Keep **one** source: uninstall the packaged plugin in Logi Options+ to develop against the `.link`, or remove the dev `.link` (`scripts/uninstall.sh` does this) to run the installed package.

**The Claude Console icon vanishes from Options+ after a reinstall — with or without an explicit uninstall first — and the keypad drops to the default layout, yet the plugin shows as installed and its log shows a clean load.** Any reinstall runs an uninstall step first (installing over an existing installation does it implicitly), which removes the application registration from the running service's *memory* but leaves it on *disk* (that's how your profiles survive reinstalls); the install step then sees the on-disk entry and silently skips re-registering it. Nothing is actually broken. Restart the service — it rebuilds the registration from disk at startup — then the Options+ UI:

```bash
killall LogiPluginService && sleep 5 && killall logioptionsplus_agent
```

(A reboot does the same, and `bash scripts/repair-registration.sh` runs the whole recovery for you, with a sanity check first. **1.8.9 and later heal this automatically** — Options+ blinks once a few seconds after a reinstall as the plugin restarts the service; the manual recovery only matters for older versions.)

**On Windows, a reinstall loses the layout differently — and a restart won't bring it back.** Windows' uninstall deletes the plugin's application data outright (the Claude Console entry *and* the imported keypad layout), and the reinstall doesn't recreate them. Recovery is the same one step as the original Windows setup: re-import `ClaudeConsole-Windows.lp5` (profile menu → Import). That recreates the application entry and the full layout. If the plugin log then shows `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` at service start *without* a dev `.link` in play, that duplicate is benign — the first load, via the application path, succeeded; working keys confirm it.

The plugin's own log — handy for any of these — is at `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeConsole.log`.

## Tests

```bash
bash tests/run-all.sh
```

Runs the C# unit tests (xUnit — injection guard, IPC file permissions, stale-file pruning, TTY
normalisation, voice project matching) and the bridge script tests (the two bash writers, checked
against a temp IPC root for paths, payloads and `0700`/`0600` permissions).

Safe to run at any time: the test project builds the plugin with `SkipPluginLink=true`, so a test
run never writes the dev `.link` into your live Logi plugin directory and never reloads the Logi
Plugin Service.

## Building & packaging

`dotnet build` hot‑reloads the plugin during development — it writes a dev `.link` into the live Logi plugin directory and restarts the service. To build **without** touching your installed plugin, add `-p:SkipPluginLink=true` (a full build, resources and all; it just skips the link + reload). `tools/voice/build.sh` builds the voice helper + bundles a self‑contained `whisper-cli` (ad‑hoc signed for dev); `tools/voice/sign-and-notarize.sh` produces the Developer‑ID‑signed, notarized release build. To produce a Marketplace package (`.lplug4`) and the full bundling/signing steps, see **[SUBMISSION.md](SUBMISSION.md)**.

## Uninstall / clean reinstall

Claude Console's footprint spans Logi's store, `~/.claude/claude-console/` (incl. the ~142 MB speech model), `/tmp`, a Microphone permission, and — if you wired the live bridge — `~/.claude/settings.json`.

**1. Remove the plugin + profile — this is the actual uninstall (Logi Options+).** In Logi Options+, **right‑click the Claude Console plugin → Uninstall** (or `logiplugintool uninstall ClaudeConsole`), and delete the imported **Claude Console — Keypad** profile. For most people — anyone who never used Voice — this is all you need.

**2. Clear the leftover app data — optional cleanup (scripted).** Step 1 does **not** remove the voice runtime, the ~142 MB speech model, or the Microphone permission — Logi Options+ can't see them, so they're left behind. This script clears exactly those leftovers; **it never removes the plugin.** It prints its targets and asks before deleting:

```bash
bash scripts/uninstall.sh            # confirm, then remove
bash scripts/uninstall.sh --dry-run  # preview only
```

It removes `~/.claude/claude-console/` (voice helper, whisper, the speech model, your `prompts.json`, and the auto-installed bridge scripts), the `/tmp/claude-console` IPC files, the Microphone grant (`tccutil reset`), any crash‑disable marker, and a dev `.link` if present.

**3. Live‑status bridge (manual).** The plugin auto-wired a `statusLine` + four `claude-console` hooks into `~/.claude/settings.json` on first run. Restore your pre-install config from the backup it made — `~/.claude/settings.json.claude-console.bak` — or just delete the `statusLine` block and the four `claude-console` hook entries by hand. (Do this after step 2, since removing the scripts leaves those entries pointing at nothing.)

For a **clean reinstall**, do 1–3, then reinstall from [Releases](https://github.com/rshankras/claude-console/releases) and re‑import the profile.

## License

[MIT](LICENSE). Bundled third‑party components (whisper.cpp, the Whisper model) are MIT‑licensed. See [EULA.md](EULA.md).
