# Claude Console

**Physical hardware controls for [Claude Code](https://claude.com/claude-code) on the Logitech MX Creative Keypad. macOS and Windows.**

> Press a button. Ship code.

Claude Console turns the MX Creative Keypad's nine LCD keys into a control surface for Claude Code: a key per session, one‑press prompts and git actions, answering its permission prompts, terminal navigation, screenshots into the conversation, and **fully offline voice** — dictate a prompt or jump into a project by speaking its name. No cloud, no API keys.

---

## Features

- **A key per session** — run Claude in several Terminal tabs and each gets its own key: the project name on a black face, and a bar along the bottom saying what it's doing — **Thinking**, **Waiting**, **Allow?**, **Complete**. Press one to focus that tab and point every other key at it; the pinned session's bar is highlighted.
- **Answer permission prompts** — **Yes** confirms the request Claude is waiting on, **No** dismisses it, and neither guesses: with nothing to approve they beep and do nothing. **Up / Down / Return** walk any menu. [Details](#answering-claudes-questions).
- **See what needs an answer** — both answer keys light **amber** when Claude wants permission; Yes turns **red** when approving would run a destructive command (`git push`, `rm -rf`, `sudo`…), while No stays amber. The session's own key reads **Allow?**. [Legend](#the-approval-badge).
- **Live status** — Model, live cost and context usage read straight from Claude Code's status line (opt‑in: [the keys are the switch](#the-live-status-bridge)).
- **One‑press prompts** — Fix Bug, Write Tests, Explore, Explain, Refactor, Review, Optimize, Security, Document, Deploy. One‑word keys, **full structured prompts** underneath — all [customizable](#customizing-prompt-keys), including **draft** keys you edit before sending.
- **Git, through Claude** — Commit, Diff, Push, Create PR, Status, Log.
- **Screenshot into the conversation** — press, drag a region, and the image is handed to the *current* session with the cursor waiting for your question. [Details](#screenshot).
- **Offline voice** — **Dictate** sends what you said straight away; **Voice Draft** leaves it in the input box so you can fix mis‑hearings first; **Go to Project** opens a project by its spoken name. [whisper.cpp](https://github.com/ggerganov/whisper.cpp) transcribes on your machine.
- **Terminal & session nav** — activate Terminal, new tab, new Claude session, next/prev tab, plus **New Claude (Window)** and **Next/Prev Window** if you prefer windows.
- **Model & modes** — **Model** opens the `/model` picker and shows the current model live; **Mode** cycles normal → auto‑accept edits → plan; plus Compact, Context, Clear, Exit, **Tab** (accept an autocomplete and run it).
- **Types where it should** — every key finds Claude's own Terminal tab and types there, so a press can't land in Slack or a browser because you glanced away. Can't find it? It beeps and types nothing.

See [PRIVACY.md](PRIVACY.md) — everything runs on your own machine.

## Requirements

**Both platforms**

- **Logitech MX Creative Keypad** + **Logi Options+ 6.4 or newer** (installs the *Logi Plugin Service*). 1.4.0 onwards is built for the .NET 10 runtime that ships with Plugin Service 6.4 — on an older Options+ the plugin will not load, so let Options+ update itself first. (Staying on an older Options+? The last build for it was 1.3.4 — ask via [Support](https://vizhi.dev/faq/).)
- **[Claude Code](https://claude.com/claude-code)** CLI, installed **natively** (WSL sessions are not visible to the plugin — see [Windows notes](#windows-notes)).

**macOS**

- **Apple Silicon** (whisper.cpp uses Metal).
- Terminal.app. Every typing key targets Claude's own Terminal tab.

**Windows**

- **Windows 10/11**, x64 or arm64.
- **Windows Terminal** for the tab/window navigation keys. Typing works in Windows Terminal, classic conhost **and** VS Code's integrated terminal — injection addresses the console directly, not the focused window — but the nav keys drive `wt.exe`, so they need Windows Terminal.
- The keypad layout is **imported by hand**, not auto-installed — see [Windows notes](#windows-notes).

To build from source, see [docs/development.md](docs/development.md).

## Windows notes

The Windows build reaches Claude a different way than macOS does, and a few differences are worth knowing before you install.

**Typing is if anything safer than on macOS.** macOS focuses Claude's Terminal tab and types into it in one atomic AppleScript run. Windows writes key events straight to the target session's *console handle*, which has no relationship to the focused window at all — a keypress reaches the intended Claude session or nothing whatsoever, and it cannot leak into another application even in principle.

**Import the Windows layout.** Same as on macOS — the plugin is universal and carries no layout — but the file is Windows-specific, because a profile is bound to an application and the host here is Windows Terminal: Options+ → your keypad → profile menu (`⋯`) → **Import** → pick [`profiles/ClaudeConsole-Windows.lp5`](profiles/ClaudeConsole-Windows.lp5). (You can also just drag Claude Console's actions onto keys yourself; nothing depends on the profile.)

**The keypad can't follow your eyes between tabs.** Windows Terminal exposes no supported way to ask which tab is in front, so with several idle sessions open the plugin can't tell which one you're looking at. One session needs no pin and just works; so does "exactly one session is waiting on you". Beyond that, **press a session key first** — pinning is exact, and every subsequent key goes to that session until you pin another or press the same key again to release it. Pressing a session key also brings its tab to the front.

**When Windows Terminal is not open, terminal-dependent keys refuse safely.** They beep and post a "Windows Terminal required" warning in Options+ instead of issuing a `wt.exe` command that silently does nothing or opens an unrelated window. **New Claude (Window)** remains available because its job is to create that first Windows Terminal window. Direct typing keys do not depend on `wt.exe`. Running Claude in a classic Command Prompt or PowerShell console? Set **Settings → System → For developers → Terminal** to **Windows Terminal** and start a new session.

**Voice works on Windows** since 2.2.0 — the package carries a Windows whisper bundle, and the model downloads on first use like on macOS.

**Not available on Windows:** Next/Previous *Window* (an OS-level gesture `wt.exe` cannot express — those keys log and do nothing), and the macOS on‑screen *Turn on* / *Turn off* dialog for live status (the second press is the switch). Opening a project always uses a fresh tab rather than reusing an idle one, because Windows Terminal offers no way to tell a busy tab from an idle one and guessing wrong would type into a live session.

**Elevated sessions cannot be controlled.** Options+ runs unelevated, and Windows blocks the console attach across integrity levels. Run Claude unelevated.

**Before uninstalling on Windows, turn live status off** (hold a live key → *Turn off*). The cleanup script that does this on macOS is not installed on Windows yet (#55), and an Options+ uninstall leaves the hooks in `~/.claude/settings.json`. Current wiring checks that the helper still exists, so a leftover is a silent no-op rather than an error, but it is still a leftover.

## Install (released plugin)

Install from the **Logi Marketplace** inside Options+, or download the latest `ClaudeConsole_<ver>.lplug4` from [vizhi.dev](https://vizhi.dev/claude-console/#install), then:

1. **Double-click it** — Logi Options+ registers the plugin. (Or, with the Logi Plugin Tool: `logiplugintool install ./ClaudeConsole_<ver>.lplug4`.) If macOS blocks it, right-click → **Open**, or run `xattr -dr com.apple.quarantine ClaudeConsole_<ver>.lplug4`.
2. **Put the keys on Terminal.** The plugin is *universal*: it binds to no application and ships no layout of its own, so nothing appears on the keypad until you give it keys. The quickest way is the ready-made layout — see [Import the ready-made layout](#import-the-ready-made-layout) just below (two clicks). Or build your own: in Options+ add **Terminal** as an application, then drag any **Claude Console** actions onto its profile.
3. On first use, grant **Accessibility** to the Logi Plugin Service (so it can type into your terminal). For **voice**, press the Dictate key and grant **Microphone** when prompted — the helper and speech model install themselves on first use. The **Screenshot** key asks for **Screen Recording** the first time.

> The live **Cost / Context / Activity** keys (and the Model key's readout) are **opt-in**: the plugin never edits your Claude Code settings on its own — they read **Set up** until you press one of them, and the press itself is the prompt. See [The live status bridge](#the-live-status-bridge).

## Import the ready-made layout

Rather than mapping nine keys by hand, import the ready-made profile to get the full layout instantly. **This is the normal setup, not a fallback** — since 2.2.0 the plugin is universal (no application binding, no packaged profile), so the layout is something you import, exactly once.

1. Download **`ClaudeConsole-Keypad.lp5`** from the [keypad layouts page](https://www.rshankar.com/keypad-profiles/), or take it from [`profiles/`](profiles/) in this repo. On Windows use **`ClaudeConsole-Windows.lp5`**.
2. In **Logi Options+** → your **MX Creative Keypad**, open the profile menu (the `⋯` / profile dropdown) → **Import Profile** → pick the `.lp5`.
3. It imports as a **Terminal** profile, five pages deep, so it activates whenever Terminal.app is frontmost. Rearrange or rebind any key afterward.

| Page | Keys (top row → bottom row) |
|------|------------------------------|
| 1 | **Session 1 · Session 2 · Session 3** / Clear · **No** · **Yes** / Esc · Tab · **Dictate** |
| 2 | Cost · Model · Compact / Up · Return · Down / Scroll Up · Scroll Down |
| 3 | Explore · Explain · Review / Optimize · Refactor · Write Tests / Document · Fix Bug · Security |
| 4 | Go to Project · New Tab · Next Tab / Prev Tab · New Claude · Exit |
| 5 | Commit · Create PR · Diff / Log · Push · Status |

Notes:
- Install the plugin first (step 1 above) so the imported keys resolve to real actions.
- Import once. Reinstalling or updating the plugin never touches your profile — it belongs to Terminal's entry in Options+, not to the plugin — so the keys simply light up again after an update.
- The profile is bound to Apple's **Terminal.app**, and so are the keys themselves: every typing key focuses Claude's Terminal.app tab before it types — it will not type into iTerm2/Ghostty/Warp or any other app. Sessions running in another terminal are **not shown on the Session keys** at all (the plugin log names each one it skipped). iTerm2 support is on the roadmap.
- Session 4–6, Screenshot, Voice Draft, Context, Activity, Mode, Terminal and the window keys exist as actions but aren't on the shipped pages — drag them on wherever you like.
- Already have a Terminal profile you like? Skip the import and drag the **Claude Console** actions onto it instead.
- **Don't open the live keys in the Options+ icon editor** (sessions, Yes/No, Model/Cost/Context/Activity): it freezes them into a snapshot. [Why, and the fix](docs/troubleshooting.md#the-session-keys-are-stuck).

## The live status bridge

The live keys read state files under a private `/tmp/claude-console/` directory that Claude Code writes via a status‑line handler (Cost / Model / Context) and five hooks (Activity, and the approval badge). Everything in it is owner‑only (0700 dirs / 0600 files), so your prompts and session state are never readable by other users on the Mac.

**Turning this on edits your Claude Code settings, so adding the wiring happens only when you ask — installation never opts you in.** The switch is the key itself: press a live key once and it flashes *Press again*, posts a card in Options+ stating the change and, on macOS, asks on screen — **Not now** / **Turn on**. *Turn on*, or a second press of the same key within 15 seconds, merges a `statusLine` handler + five hooks into `~/.claude/settings.json`. It only **appends** hooks that aren't already there, **chains** an existing `statusLine` (yours still renders), and takes a **rolling backup** — `settings.json.claude-console.bak` is rewritten before *every* change the plugin makes. An update may replace only commands already recognisably owned by Claude Console with their current missing-handler-safe form; it never adds wiring or touches user commands. On macOS the keys come alive with each running session's **next activity** — no restart; on Windows start a **new Claude Code session**. Whenever live status is off, the **Yes / No** keys read the same **Set up** / **Off** word the live keys do, and every session key's state bar reads **Set up** / **Status off**: the approval badge, the answer keys and the state bars all depend on this wiring, and without it nothing they could show would be current.

**To take it back out:** hold a live key (a long press) and choose **Turn off** — or hold it again within 15 s — or, without the keypad, `bash ~/.claude/claude-console/scripts/uninstall.sh --unwire`. Either removes *only* the plugin's entries and leaves a marker (`~/.claude/claude-console/no-autowire`) so the keys read **Off** rather than **Set up**. Pressing a live key turns it back on.

Without the hooks the **Activity** key reads **Ready** and never changes; the **Context** key needs only the status line, and turns **amber at 75%** / **red at 90%** so you compact before an auto‑compaction. To wire it by hand instead, or to see exactly what is written, see [docs/live-status-bridge.md](docs/live-status-bridge.md).

## Several sessions at once

Run Claude in more than one Terminal tab and each session gets **its own key** in the **Sessions** group: the project (directory) name on a black face and a state bar along the bottom — **Thinking** while it works, **Waiting** when it has stopped for you, **Allow?** when a permission prompt is up, **Complete** when the turn is done. Press one to jump to that tab.

Pressing a session key also **pins every other key to it**, which is the point: you can approve a prompt in session 2 while looking at session 1, or while reading a browser. The pinned session's bar is highlighted; the others are grey. The pin **holds** until you press another session key, press the same key again to release it, or that session exits — switching Terminal tabs does not move it. With only one session running nothing changes; if you haven't picked a session and exactly one is waiting on you, that's the one that gets your **Yes**. When it's genuinely ambiguous the plugin won't guess; it beeps instead of answering the wrong Claude.

Notes:
- **A pin aims the keys; it doesn't freeze the readouts.** Cost, Model and Context show the session in the tab you're looking at, while Yes/No, Tab and the typing keys act on the pinned one — so you can watch one session while answering another. The amber badge on Yes always describes the session the answer keys will actually answer. (On Windows there's no way to detect the frontmost tab, so both follow the pin.)
- **Slots are stable.** A session keeps its key for as long as it lives — close one and the others stay put, so you don't approve the wrong session out of muscle memory. The freed key is reused by the next session you start. Six sessions get keys; the shipped layout shows three — drag **Session 4–6** onto a page if you run more.
- A new session takes a key **immediately** (labelled "Claude Code" until it reports its project), and a closed tab clears within about two seconds. **Go to Project** releases the pin, since it starts a session somewhere new.
- Terminal.app only, like the rest of the plugin.

## Using voice

- **Dictate** — press (you'll hear a *Tink*), say your prompt, press again. It transcribes locally, types the text into Claude's Terminal tab, **and sends it**.
- **Voice Draft** — same flow, but the transcript is only **typed, not sent**: it sits in Claude's input box so you can fix anything whisper misheard, then submit with **Return** (keyboard or the keypad's Return key). Use Dictate for quick prompts, Voice Draft for anything long enough to mis-transcribe.
- **Go to Project** — press, say a project name (e.g. *"indie app autopilot"*), press again. Opens a new tab in that project running `claude`; reuses an idle shell tab, or opens a new one if `claude` is already running.

  **Where it looks:** projects any session is already open in, plus every git repository within three levels of your home folder — and, once a folder is seen to hold several repositories, its other subfolders too. If your projects live somewhere unusual, list the folders that contain them in `~/.claude/claude-console/project-roots`, one per line (`~` allowed, `#` for comments); every subfolder of those becomes matchable and the automatic search is skipped.

Start and stop a recording with the **same** key — each voice key is its own start/stop toggle. Pressing a *different* voice key while one is recording **stops** it rather than starting a second recording, and the transcript still goes where the key you started with intended. While a transcript is being produced, a further press is ignored with a beep.

First use prompts once for **Microphone** permission (granted to the helper, not the daemon). **A failed dictation says so on the key**, with a beep: *Mic denied* (allow **ClaudeVoiceHelper** in System Settings → Privacy & Security → Microphone — or `tccutil reset Microphone com.rshankar.claudeconsole.voicehelper` and re-grant on the next press; a rebuilt or re-signed helper resets the grant), *No speech* (it recorded but heard nothing usable), *Model loading* (the ~142 MB `base.en` model is still downloading — it's fetched and checksum‑verified on first use; to pre‑seed it, drop `ggml-base.en.bin` at `~/.claude/claude-console/whisper/`).

## Answering Claude's questions

When Claude asks something, answer from the keypad instead of the keyboard:

- **Yes / No** — answer the **permission prompt** Claude is waiting on: **Yes** confirms it (the highlighted option), **No** dismisses it without running the tool. They act only when the plugin can *see* a pending approval; with nothing to approve they **beep and do nothing** rather than guess — typing a word at a hidden menu, or sending a bare Return at an idle prompt, is how the wrong thing gets approved. For plain‑text questions, type into the session. While [live status](#the-live-status-bridge) is off they read **Set up** / **Off** on a grey tile instead of green and red, and a press posts a card in Options+ saying which key turns it on — without the wiring they cannot see a prompt at all.
- **Up / Down / Return** — navigate and confirm any selection menu: multiple‑choice questions (`AskUserQuestion`), plan‑mode confirmation, or a permission prompt whose *other* options you want.

### The approval badge

**The keys tell you that an answer is pending and what Yes would approve.** When Claude asks for permission, both **Yes** and **No** carry a small filled dot in their top-right corner and the session's own key reads **Allow?**:

| Badges | Meaning | What to do |
|-------|---------|------------|
| *(none on either key)* | Nothing is waiting for an answer. | — |
| 🟡 **Amber on Yes and No** | Waiting on you, and approving is **routine** — reading a file, running a test, an edit. | Press **Yes** or **No**. |
| 🔴 **Red on Yes**, 🟡 **amber on No** | Yes would run something **destructive or outward-facing**; No would reject it. | Look before pressing **Yes**, or press **No** to reject. |

Red is triggered by the pending command matching one of the patterns in [`src/Core/RiskClassifier.cs`](src/Core/RiskClassifier.cs) — `sudo`, `rm -rf`, `git push`, `git reset --hard`, `git clean -fd`, `--force`, `dd of=`, `mkfs`, `chmod 777`, `drop table`, `delete from`, piping a download into a shell, `kubectl delete`, `terraform apply`/`destroy`, `npm publish`, `gh release create`, `killall`, `shutdown`…

Two things worth knowing:

- It's a **hint, not a gate.** Claude Code's own prompt is still what actually holds the command, and the classifier deliberately leans toward warning you unnecessarily — a badge that stayed quiet on a real `git push --force` would be worse than one that cries wolf. (Bare generic flags like a lone `-f` are *not* flagged: a badge that lights on every other command teaches you to ignore it.)
- The badge describes the session the answer keys are **pinned to**; with several sessions, the **Allow?** bar shows *which* one wants attention.

The badge clears itself as soon as the session stops waiting, or the moment you answer from the keypad — a rejection fires no hook, so the plugin clears it on its own. It's powered by a `PermissionRequest` hook the plugin wires up for you (see [The live status bridge](#the-live-status-bridge)); without it Yes/No have nothing to see — they read **Set up** / **Off** and a press beeps and explains itself in Options+.

These keys focus Claude's Terminal tab automatically before typing (verified by its TTY), so they work even when another app is frontmost — if Terminal isn't running or the tab is gone, they beep and type nothing.

## Screenshot

**Screenshot** (Core group) captures a region of the screen with the system's own picker (Shift+Cmd+4 style) and hands the image to the **current conversation**: the file path is typed with a short instruction, **without pressing Return**, so you add your question and send. First use asks for **Screen Recording** for the Logi Plugin Service; if a capture later produces nothing, that grant was refused. On Windows it needs a Windows Terminal session like the navigation keys.

## Accepting autocomplete, switching modes, scrolling

- **Tab** — completes Claude Code's highlighted suggestion (a `/slash` command or an `@file` mention) **and submits it** in one press. Because it always presses Return, use **Up / Down / Return** if you want to complete *without* sending.
- **Mode** — sends **Shift+Tab**, which cycles Claude Code's input modes: **normal → auto‑accept edits → plan**.
- **Scroll Up / Scroll Down** — page through the transcript (Page Up / Page Down). In classic mode that scrolls Terminal's scrollback (keep a generous limit in Terminal ▸ Settings ▸ Profiles ▸ Window); in `/tui fullscreen` mode Claude Code scrolls its own buffer.

All of these focus Claude's Terminal tab before sending keys.

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
- **`icon`** — an embedded icon basename. Since 2.2.0 every action icon is the same copper monochrome (colour is reserved for state), so the name only picks the glyph: `fix_bug`, `write_tests`, `explore`, `explain`, `refactor`, `review`, `optimize`, `security`, `document`, `deploy`, `commit`, `diff`, `push`, `create_pr`, `status`, `log`, `project`, `terminal`, `screenshot`, `compact` (an unknown name falls back to text).
- **`submit`** *(optional, default `true`)* — set `false` to make a **draft key**: it types the prompt but doesn't press Return, so you can edit or finish the sentence before sending it.

Reload the plugin to pick up edits (restart Logi Options+ / `killall LogiPluginService`). Delete the file to restore the built-in defaults. **Your edits are permanent** — once you change anything in the file, no plugin update will overwrite it; only an untouched factory `prompts.json` is upgraded in place when a release improves the defaults.

## Key map

| Group | Keys |
|-------|------|
| **Sessions** | Session 1-6 — one key per running Claude session: project name + state bar (press to focus it and pin the other keys to it) |
| **Core** | Model* · Cost* · Activity* · Context* · Screenshot · Esc · Mode · Tab · Compact · Clear · Exit |
| **Answer** | Yes · No · Up · Down · Return |
| **Prompts** | Fix Bug · Write Tests · Explore · Explain · Refactor · Review · Optimize · Security · Document · Deploy |
| **Git** | Commit · Diff · Push · Create PR · Status · Log |
| **Scroll** | Scroll Up · Scroll Down |
| **Terminal** | Terminal · New Tab · New Claude · Next Tab · Prev Tab · New Claude (Window) · Next Window · Prev Window · **Go to Project** (voice) |
| **Universal** | **Dictate** · **Voice Draft** |

*\* live display, updates from the status line once [live status](#the-live-status-bridge) is on.*

## How it works

```
MX Creative Keypad → Logi Plugin Service → C# plugin (BridgeManager)
                                                  ↕  file IPC in /tmp/claude-console (owner-only)
Claude Code ← status line (bash) + voice helper (Swift + whisper.cpp)
```

File‑based IPC under a private `/tmp/claude-console/` root (0700 dirs / 0600 files); action keys focus Claude's Terminal tab (verified by TTY) and type via `osascript`; voice records through a notarized helper app that owns its own Microphone permission, then transcribes with a bundled, self‑contained `whisper-cli` (no Homebrew at runtime). This repo builds **one package per agent** from a shared engine — Claude Console for Claude Code and **[Vizhi for Codex](src/Products/VizhiCodex/README.md)** for OpenAI's Codex CLI; both can be installed side by side. Architecture in [docs/multi-agent-architecture.md](docs/multi-agent-architecture.md); packaging in [SUBMISSION.md](SUBMISSION.md).

## Troubleshooting

The four most common, in full with the rest in [docs/troubleshooting.md](docs/troubleshooting.md):

- **Nothing appears on the keypad after installing.** Expected — the plugin is universal and ships no layout. [Import the ready-made layout](#import-the-ready-made-layout).
- **Keys show only an exclamation mark / plain text right after building from source.** A dev `.link` and an installed `.lplug4` are both registered; keep one.
- **The session keys are stuck** (one session ever shows, the badge never lights, but presses work). A key opened in the Options+ icon editor is frozen into a snapshot — `bash scripts/unfreeze-keys.sh --apply`.
- **Windows: session/navigation keys beep, or Options+ says "Windows Terminal required".** Open Windows Terminal and start the agent there; elevated sessions can't be reached.

The plugin's log is at `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeConsole.log` (Windows: `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\ClaudeConsole.log`).

## Uninstall

In Logi Options+, **right‑click the Claude Console plugin → Uninstall** and delete the imported **Claude Console — Keypad** profile; then run the cleanup script the plugin left outside its own package:

```bash
bash ~/.claude/claude-console/scripts/uninstall.sh            # confirm, then remove
bash ~/.claude/claude-console/scripts/uninstall.sh --dry-run  # preview only
```

It unwires the live status hooks surgically, removes `~/.claude/claude-console/` (voice helper, speech model, your `prompts.json`), the IPC files, the Microphone grant and any dev `.link`, and asks before deleting. What it touches, why the order matters, and the Windows caveat: [docs/uninstall.md](docs/uninstall.md).

**Options+ cannot do any of that for you.** Its uninstall removes the plugin and nothing else, on macOS as on Windows: the hooks, the status line and `~/.claude/claude-console/` stay (#55). On macOS the hooks notice on their own (#73): once the plugin's folder has been gone for over a minute, the next Claude Code event takes the plugin's entries out of `settings.json` surgically — same rolling backup as *Turn off*, your own entries untouched, the Off marker set — and leaves `~/.claude/claude-console/unwired-after-uninstall` to say so. `~/.claude/claude-console/` itself stays for the script above. If you would rather not wait, hold a live key and choose **Turn off** *before* uninstalling; on Windows that is still the only way, and the entries left behind are a silent no-op rather than a "hook error" on every turn because each one checks that its exe exists before running it.

## Developing

Build, test, package and release notes are in [docs/development.md](docs/development.md); contributor rules in [CLAUDE.md](CLAUDE.md).

## Feedback

Found a bug, missing a key you'd use daily, or running a terminal this doesn't support yet?
See [Support on vizhi.dev](https://vizhi.dev/faq/) — feature requests and "this confused me"
reports are equally welcome.

## License

Proprietary — see the [EULA](https://vizhi.dev/eula/) ([EULA.md](EULA.md) in this repository) and
the [privacy policy](https://vizhi.dev/privacy/). Bundled third‑party components (whisper.cpp, the
Whisper model) are MIT‑licensed; their licence texts ship with the plugin.
