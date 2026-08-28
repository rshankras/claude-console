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

**Import the Windows layout.** Same as on macOS — the plugin is universal and carries no layout — but the file is Windows-specific, because a profile is bound to an application and the host here is Windows Terminal: Options+ → your keypad → profile menu (`⋯`) → **Import** → pick [`profiles/ClaudeConsole-Windows.lp5`](profiles/ClaudeConsole-Windows.lp5). (You can also just drag Claude Console's actions onto keys yourself; nothing depends on the profile.)

**The keypad can't follow your eyes between tabs.** Windows Terminal exposes no supported way to ask which tab is in front, so with several idle sessions open the plugin can't tell which one you're looking at. One session needs no pin and just works; so does "exactly one session is waiting on you". Beyond that, **press a session key first** — pinning is exact, and every subsequent key goes to that session until you pin another or press the same key again to release it. Pressing a session key also brings its tab to the front.

**Not available on Windows:** Next/Previous *Window* (an OS-level gesture `wt.exe` cannot express — those keys log and do nothing). Opening a project always uses a fresh tab rather than reusing an idle one, because Windows Terminal offers no way to tell a busy tab from an idle one and guessing wrong would type into a live session.

**Elevated sessions are unreachable.** Options+ runs unelevated, and Windows blocks the console attach across integrity levels. Run Claude unelevated.

## Install (released plugin)

Download the latest `ClaudeConsole_<ver>.lplug4` from [**Releases**](https://github.com/rshankras/claude-console/releases), then:

1. **Double-click it** — Logi Options+ registers the plugin. (Or, with the Logi Plugin Tool: `logiplugintool install ./ClaudeConsole_<ver>.lplug4`.) If macOS blocks it, right-click → **Open**, or run `xattr -dr com.apple.quarantine ClaudeConsole_<ver>.lplug4`.
2. **Put the keys on Terminal.** The plugin is *universal*: it binds to no application and ships no layout of its own, so nothing appears on the keypad until you give it keys. The quickest way is the ready-made layout — see [Import the ready-made layout](#import-the-ready-made-layout) just below (two clicks). Or build your own: in Options+ add **Terminal** as an application, then drag any **Claude Console** actions onto its profile.
3. On first use, grant **Accessibility** to the Logi Plugin Service (so it can type into your terminal). For **voice**, press the Voice key and grant **Microphone** when prompted — the helper and speech model install themselves on first use.

> Everything works straight from the download — including the live **Model / Cost / Context / Activity** keys. On first load the plugin installs its status-line + hook scripts and **edits `~/.claude/settings.json`** to wire them in, so the live keys light up on your **next Claude Code session** with no setup. It only adds its own entries and backs the file up first; `uninstall.sh --unwire` takes them out again. (Details, and how to opt out, in [The live status bridge](#the-live-status-bridge) below.)

## Import the ready-made layout

Rather than mapping nine keys by hand, import the ready-made profile to get the full layout instantly. **This is the normal setup, not a fallback** — since 2.2.0 the plugin is universal (no application binding, no packaged profile), so the layout is something you import, exactly once.

1. Download **`ClaudeConsole-Keypad.lp5`** from [**Releases**](https://github.com/rshankras/claude-console/releases) (alongside the `.lplug4`), or take it from [`profiles/`](profiles/) in this repo. On Windows use **`ClaudeConsole-Windows.lp5`**.
2. In **Logi Options+** → your **MX Creative Keypad**, open the profile menu (the `⋯` / profile dropdown) → **Import Profile** → pick the `.lp5`.
3. It imports as a **Terminal** profile with the keys populated — Sessions on top, Clear / Voice / Esc, Yes / No / Tab, more pages behind — so it activates whenever Terminal.app is frontmost. Rearrange or rebind any key afterward.

Notes:
- Install the plugin first (step 1 above) so the imported keys resolve to real actions.
- Import once. Reinstalling or updating the plugin never touches your profile — it belongs to Terminal's entry in Options+, not to the plugin — so the keys simply light up again after an update.
- The profile is bound to Apple's **Terminal.app**, and so are the keys themselves: every typing key (prompts, answers, voice) focuses Claude's Terminal.app tab before it types — it will not type into iTerm2/Ghostty/Warp or any other app.
- Already have a Terminal profile you like? Skip the import and drag the **Claude Console** actions onto it instead.

## Repository layout

This repo builds **one package per agent** from a shared engine — Claude Console for Claude Code,
and **[Vizhi for Codex](src/Products/VizhiCodex/README.md)** for OpenAI's Codex CLI on the same core.

> Both consoles can be installed side by side: since 2.2.0 neither binds an application, so the
> Logi Plugin Service has nothing to arbitrate between them — put each product's keys on whatever
> profile you like. (Before 2.2.0 both claimed Terminal.app and only one could win.) `src/Core` is the engine (keypad, session grid, terminal
targeting, voice, install/repair), `src/Agents/<agent>` is a small adapter per agent, and
`src/Products/<name>` is a thin product that pairs them with its own branding, profile and
version. Products version and release independently.

The design, and why it is a shared engine rather than a fork, is in
[docs/multi-agent-architecture.md](docs/multi-agent-architecture.md); contributor notes are in
[CLAUDE.md](CLAUDE.md).

## Install (build from source)

```bash
# 1. Build the plugin — links + hot-reloads into the Logi Plugin Service
cd src/Products/ClaudeConsole
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

**This is set up automatically — and it edits your Claude Code settings to do it.** On first load the plugin writes both scripts to `~/.claude/claude-console/scripts/` and merges a `statusLine` handler + five hooks into `~/.claude/settings.json`. It's careful about it: only **appends** a hook when it isn't already present, **chains** an existing `statusLine` (records yours and runs it through, so your custom status bar still renders) rather than overwriting it, and takes a **rolling backup** — `settings.json.claude-console.bak` is rewritten immediately before *every* change the plugin makes, so it is always the state one change ago, never a stale snapshot. The live keys come alive on your **next Claude Code session** — Claude Code reads hooks/statusLine at session start, so a session already running won't pick them up.

**To take it back out, or to keep it out:**

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh --unwire
```

That removes *only* the plugin's entries (your own hooks and status bar are left exactly as they were, a chained status line is put back), and sets the opt-out so the plugin doesn't wire it again. The Cost / Context / Activity keys show dashes from then on; everything else keeps working. To wire it again, delete `~/.claude/claude-console/no-autowire` and reload the plugin. To opt out *before* first load, create that file first.

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
- **Press the pinned session's key again to release it** — the keys go back to following whichever tab is in front. The pin also survives a plugin reload, is released automatically if its session exits so the keys are never stranded on a closed tab, and **Go to Project** drops it too, since that starts a session somewhere new.
- **Six sessions get keys; the shipped layout shows three.** Session 4, 5 and 6 exist as actions — drag them onto any page if you regularly run more than three at once. A session's key **never moves for as long as it lives**: when the middle of three exits, the other two stay exactly where your fingers expect them and the freed key waits for the next new session. That stability is deliberate, so a fourth session assigned to slot 4 stays there rather than sliding into the gap — if it isn't on your layout, add the key.
- **A pin aims the keys; it doesn't freeze the readouts.** Cost, Model and Context always show the session in the tab you're looking at, while Yes/No, Tab and the typing keys act on the pinned one — so you can watch one session while answering another. The highlighted session key tells you which is pinned, and the amber approval badge always describes the session the answer keys will actually answer. (On Windows there's no way to detect the frontmost tab, so both follow the pin.)
- A new session takes a key **immediately** (labelled "Claude" until it first renders a status line), and a closed tab clears within about two seconds.
- Six sessions fit; beyond that the extras run fine, just without a key.
- Terminal.app only, like the rest of the plugin.

## Using voice

- **Voice key** — press (you'll hear a *Tink*), say your prompt, press again. It transcribes locally, types the text into Claude's Terminal tab, **and sends it**.
- **Voice Draft key** — same flow, but the transcript is only **typed, not sent**: it sits in Claude's input box so you can fix anything whisper misheard, then submit with **Return** (keyboard or the keypad's Return key). Use Voice for quick prompts, Voice Draft for anything long enough to mis-transcribe.
- **Go to Project** — press, say a project name (e.g. *"indie app autopilot"*), press again. Opens a new tab in that project running `claude`; reuses an idle shell tab, or opens a new one if `claude` is already running.

  **Where it looks:** projects any session is already open in, plus every git repository within three levels of your home folder — and, once a folder is seen to hold several repositories, its other subfolders too (so a checkout-in-progress next to your repos still matches). Nothing needs configuring for a normal layout. If your projects live somewhere unusual, list the folders that contain them in `~/.claude/claude-console/project-roots`, one per line (`~` allowed, `#` for comments); every subfolder of those becomes matchable and the automatic search is skipped. When nothing matches, the plugin log names how many candidates it searched and where they came from.

Start and stop a recording with the **same** key — each voice key is its own start/stop toggle.
Pressing a *different* voice key while one is recording **stops** it rather than starting a second
recording, and the transcript still goes where the key you started with intended: a dictation stopped
with **Go to Project** is still sent to Claude, not treated as a project name. While a transcript is
being produced, a further press is ignored with a beep rather than discarding the result.

First use prompts once for **Microphone** permission (granted to the helper, not the daemon). The plugin also needs **Accessibility** permission for the Logi Plugin Service (to type into the terminal).

**The Voice key flashes red "Mic denied" (with a beep):** the helper was refused the microphone. Allow **ClaudeVoiceHelper** in System Settings → Privacy & Security → Microphone, or reset the permission and re-grant on the next press. This is also what you'll see after re-signing or rebuilding the helper: macOS ties the grant to the code signature, so a rebuild resets it — and until 2.2.0 that failed *silently* (nothing typed, no beep, no prompt), which is why the key now says so. "No speech" means it recorded but heard nothing usable; "Model loading" means the speech model is still downloading — try again shortly.

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

Red is triggered by the pending command matching one of the patterns in [`src/Core/RiskClassifier.cs`](src/Core/RiskClassifier.cs) — `sudo`, `rm -rf`, `git push`, `git reset --hard`, `git clean -fd`, `--force`, `dd of=`, `mkfs`, `chmod 777`, `drop table`, `delete from`, piping a download into a shell, `kubectl delete`, `terraform apply`/`destroy`, `npm publish`, `gh release create`, `killall`, `shutdown`…

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

**Nothing appears on the keypad after installing.** That is expected: the plugin is universal (2.2.0+) — it binds to no application and ships no layout. Import [the ready-made layout](#import-the-ready-made-layout), or add Terminal as an application in Options+ and drag the Claude Console actions on. The actions are listed under **Claude Console Actions** in the Options+ action panel; if that list is missing, the plugin did not load — check the log.

**Before 2.2.0: the Claude Console icon vanished from Options+ after a reinstall, or never appeared after a first sideloaded install.** Those versions registered their own application entry in Options+, and the service could lose it (a reinstall dropped it from memory, a sideloaded install never created it). 2.2.0 removed the entry altogether, so there is nothing to lose; the layout lives on Terminal's own entry, which is yours. If you are on an older version, update — or restart the Logi Plugin Service (`killall LogiPluginService`) and then Options+ so it re-reads the registration from disk.

**The log says `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` at service start.** On its own this is benign boot noise, not a failure: the service loads every sideloaded plugin twice at startup (once from its internal plugin record, once from the folder scan) and the second attempt logs this while refusing the duplicate — every sideloaded plugin on the machine shows the same pair. It only signals a real problem when paired with a dev `.link` (see above).

**The session keys are stuck — only one session ever shows, the context % never moves, and the Yes/No badge never lights, but everything still *works* when pressed.** A key you have opened in the **Logi Options+ icon editor** stops being live. The editor saves a `.ict` next to the profile — a *snapshot* of that key, a baked image plus the literal text that was on it at that moment ("Session 2") — and from then on the service draws the snapshot and never asks the plugin for an image. The giveaway is that pressing the key does the right thing (the plugin knows about the session) while the picture never changes; a profile created fresh renders live, because it has no `.ict` files. Restore live rendering with:

```bash
bash scripts/unfreeze-keys.sh          # lists what's frozen
bash scripts/unfreeze-keys.sh --apply  # backs them up to your Desktop, then removes them
```

Deliberate icon customizations on those keys are lost — that's the trade. **Avoid customizing the live keys** (the session slots, Yes/No, and the Model / Cost / Context / Activity displays); the static keys are safe to restyle.

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

**1. Remove the plugin + profile — this is the actual uninstall (Logi Options+).** In Logi Options+, **right‑click the Claude Console plugin → Uninstall** (or `logiplugintool uninstall ClaudeConsole`), and delete the imported **Claude Console — Keypad** profile.

**2. Run the cleanup script — not optional if this was the last Logitech plugin of ours.** Options+ removes the plugin but **leaves its application registration behind**, still claiming Terminal.app: Claude Console stays listed in Options+ as if the uninstall had failed, and opening Terminal switches the keypad to a layout whose keys point at actions that no longer exist (a keypad of exclamation marks). Another of our plugins sweeps that on its next load; if none is left, nothing does. The plugin installs this script *outside* the package precisely so it survives the uninstall — you don't need the repo:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh            # confirm, then remove
bash ~/.claude/claude-console/scripts/uninstall.sh --dry-run  # preview only
```

It sweeps the orphaned registration (only entries whose plugin is gone, never another vendor's), then removes `~/.claude/claude-console/` (voice helper, whisper, the ~142 MB speech model, your `prompts.json`, and the auto-installed scripts — including itself), the `/tmp/claude-console` IPC files, the Microphone grant (`tccutil reset`), any crash‑disable marker, and a dev `.link` if present. It prints its targets and asks before deleting; **it never removes the plugin.** Then restart the service so Options+ forgets the entry: `killall LogiPluginService`.

**3. Live‑status bridge — done by step 2.** The cleanup script removes the plugin's `statusLine` + hook entries from `~/.claude/settings.json` surgically (your own entries stay; a chained status line is put back) before it deletes the scripts they point at. If you only want the wiring gone and the plugin kept, run it with `--unwire` instead. Don't restore `settings.json.claude-console.bak` by hand to undo the plugin — it is a rolling backup of the state one change ago, useful if a write went wrong, not a pre-install snapshot.

For a **clean reinstall**, do 1–3, then reinstall from [Releases](https://github.com/rshankras/claude-console/releases) and re‑import the profile.

## Feedback

Found a bug, missing a key you'd use daily, or running a terminal this doesn't support yet?
[Open an issue](https://github.com/rshankras/claude-console/issues) — feature requests and
"this confused me" reports are equally welcome, and issues double as the roadmap.

## License

[MIT](LICENSE). Bundled third‑party components (whisper.cpp, the Whisper model) are MIT‑licensed. See [EULA.md](EULA.md).
