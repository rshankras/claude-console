# Marketplace Listing Copy

The exact text entered in the Logitech Marketplace submission form ([marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute)). **The 2.2.2 copy below is current** (submitted 2026-09-13; the package and these texts as submitted are in `~/Downloads/Claude Console/ClaudeConsole-2.2.2-install/`); the 2.2.1 and 2.2.0 release notes it replaced follow it, and the 2.0.x copy is kept at the bottom for reference. Process and packaging steps live in [SUBMISSION.md](../SUBMISSION.md).

## Auto-filled fields (from `LoupedeckPackage.yaml`)

These are read out of the uploaded `.lplug4` — the form fills them itself. To change any of them, edit `src/package/metadata/LoupedeckPackage.yaml` and repack; don't look for a form field.

| Field | Value |
|---|---|
| Name | Claude Console |
| Author | S.Ravi Shankar |
| Operating system | macOS, Windows (from `pluginFolderMac` + `pluginFolderWin`) |
| Capabilities | `HasNoApplication` — universal, binds no application (#23) |
| Version | 2.2.2 |
| Content licence | Proprietary — https://vizhi.dev/eula/ (first submitted this way in 2.2.2; 2.2.1 as submitted said MIT — if the form's licence list has no "Proprietary" entry, pick the nearest and keep the EULA URL) |
| Support | https://vizhi.dev/faq/ (from 2.2.2; 2.2.1 as submitted: the keypad-profiles issue tracker, which the FAQ still links to) |
| Homepage | https://vizhi.dev/claude-console/ (from 2.2.2; 2.2.1 as submitted: the keypad-profiles site) |
| Copyright | Copyright © 2026 S.Ravi Shankar. All rights reserved. |

Every user-facing link — these fields, the three card buttons baked into the plugin DLL, the README download links — points at **vizhi.dev** as of 2026-09-09. The source repository will be closed, so nothing a user can reach may point at github.com/rshankras/claude-console (#68, #71: every card button was a 404 for a week when it went private). The vizhi.dev anchors the DLL uses (`claude-console/#live-status-bridge`, `faq/#windows`, `faq/#voice`) are baked into shipped packages: treat them as frozen, or add redirects before renaming.

## Teaser card description — limit 120 characters

2.2.2 as submitted, 97 characters. This is the wording the portal already held, and it differs from the 2.0.x teaser this doc used to record; it was kept unchanged, although it names Apple Terminal only:

```
Physical hardware controls for Claude Code in Apple Terminal on the MX Keypad or Creative Console
```

The teaser this doc recorded before (108 characters), which names both terminals:

```
Physical hardware controls for Claude Code in Apple Terminal or Windows Terminal, on the MX Creative Keypad.
```

## Detail page description — limit 500 characters

Markdown supported: **bold**, *cursive*, `-` lists, `1.` lists, `[links](url)`, emojis. 2.2.0 rewrite: the Answer line now describes what the keys actually do since #21, and "Voice" is dictation rather than a key name. 2.2.2 (482 characters): the Live status line reads *cost, context & activity* instead of *model, cost & context*, because the Model key stopped being a live display (#86). The portal holds the list separators as plain ` - ` hyphens, not em dashes, so that is how it is recorded here:

```markdown
**Physical hardware controls for Claude Code. Press a button. Ship code.**

- **A key per session** - project & state on each; press to focus
- **Answer prompts** - Yes approves, No rejects; amber when asked, red when risky
- **Offline voice** - dictate a prompt or open a project by name
- **Live status** - cost, context & activity keys, opt-in
- **One-press prompts** - Fix Bug, Write Tests, Review
- **Git** - commit, diff, push, PR

Works in Apple Terminal or Windows Terminal.
```

## Release notes — limit 1000 characters

2.2.2 (submitted 2026-09-13), 943 characters. Leads with the Windows Yes / No fix (#74, Windows retest item 2). The first draft ran to 1,028 characters and was trimmed to 980; the heading was then shortened to `**2.2.2**` in the form before submitting:

```markdown
**2.2.2**

- **Windows: Yes / No answer again.** Every press had been refused.
- **Yes / No find the session with a prompt up**, even while other sessions sit idle.
- **Windows: live status turns on without a restart**, as on macOS, and keeps working after Claude Code updates itself.
- **Voice says when it could not type** (*No target*, *Not typed*), announces the speech model download, and shows *Starting* on Windows until the mic is ready.
- **Go to Project** reaches names containing "the", "open" or "claude", and says *No match*.
- **The Model key opens the model picker** with one icon for every model.
- **Settings edits leave the rest of your settings file exactly as it was.** On macOS the hooks remove themselves after an uninstall.
- **Windows:** New Tab and New Claude open in your home folder, no separate .NET runtime is needed, and the package is smaller.

Keypad layouts are unchanged. Download them from the homepage link.
```

## Release notes — 2.2.1 (superseded)

2.2.1 (submitted 2026-09-04), 841 characters. Leads with the two answer-key changes a user notices first; the last line sends them to the homepage for the layouts because the GitHub release is private:

```markdown
**2.2.1 — answers to the QA retest of 2.2.0.**

- **Yes / No say when they cannot work.** With live status off they read *Off*, and a press explains how to turn it on instead of beeping.
- **A No press clears the pending cue** at once. Both answer keys now show it, and Yes turns red for a risky request.
- **The bundled voice helper now replaces an older copy** on macOS.
- **Leftover hooks stay silent.** If you uninstall without turning live status off, the entries left in your Claude Code settings no longer error on every turn.
- **Windows:** hook processes end themselves after 8 seconds and are capped, so they can no longer pile up and block an uninstall.
- No warnings on load; the shipped symbols carry no build paths.

Keypad layouts are unchanged. Download them from the homepage link and import once onto your Terminal profile.
```

The PM offered (2026-09-04) to help with the description wording and asked for a setup FAQ on the listing; the copy above plus a draft FAQ went to her as `Claude-Console-Marketplace-copy-and-FAQ.docx`. The FAQ's home is the public profiles site, which is also where #68's card links should land.

## Release notes — 2.2.0 (superseded)

2.2.0 is the first release Marketplace users see as an *update*, so this is a real what's-changed rather than the 2.0.0 introduction. Leads with the two things that change their setup — the universal plugin (they must import the layout) and opt-in live status. 974 characters:

```markdown
**2.2.0 — the QA retest release.**

- **Now a universal plugin**: it binds to no application. After installing, import the keypad layout from the GitHub release (linked below) onto your Terminal profile, or drag the Claude Console actions where you want them.
- **Live status is opt-in.** The plugin no longer edits your Claude Code settings on its own — press a live key and confirm; hold it to turn back off.
- **The No key now rejects.** Yes confirms the permission prompt, No dismisses it, and neither acts when there's nothing to approve.
- **Typing works on every keyboard layout** — no more wrong characters on AZERTY or QWERTZ.
- **Offline voice works from a package install** on macOS and Windows, and a failed dictation now says why.
- **New keypad design** from Logitech: session keys show project + state, coloured approval keys, refreshed icons.
- Fixes for stuck "working" indicators, idle CPU, session slots taken by terminals the keys can't drive, and more.
```

**Ask QA to confirm** (worth stating in the submission notes): the SDK exposes no plugin-wide settings page (`PluginPreferenceType` is `{None, Account}`), so a deliberate key press with an on-screen dialog is the closest discoverable consent control it offers for the `settings.json` edit (#31).

## Lessons for next time

- **Don't paste from a terminal window.** Selection picks up invisible indent and trailing spaces that count against the limit — a 491-char draft overflowed the 500-char field that way. Put the text on the clipboard directly (`pbcopy < file`) and ⌘V into the form.
- **Count characters, not bytes**: `wc -m` with a UTF-8 locale (em-dashes are 1 character but 3 bytes; `wc -c` overcounts). Leave ≥15 characters of headroom in case the form counts newlines differently.
- **Feature order is the message**: sessions first, voice second — the differentiators no other keypad plugin has (per-session keys, amber/red approval lighting, fully offline voice).
- Always name the supported terminals (Apple Terminal / Windows Terminal) — "your terminal" wrongly implies iTerm2/Ghostty/Warp work.
- Homepage and Support URLs have their own form fields, so don't spend description characters on links.

---

## Archive — the 2.0.x copy

Entered for the 2.0.0 submission on 2026‑08‑12 and resubmitted unchanged as 2.0.1 on 2026‑08‑13 after QA flagged the bundled `PluginApi.dll`.

## Teaser card description — limit 120 characters

Used 108 (verified against the live form at the 2.0.1 resubmission):

```
Physical hardware controls for Claude Code in Apple Terminal or Windows Terminal, on the MX Creative Keypad.
```

Runner-up kept for reference (113): `Control Claude Code in Apple Terminal or Windows Terminal from the MX Creative Keypad. Press a button. Ship code.`

## Detail page description — limit 500 characters

Markdown supported: **bold**, *cursive*, `-` lists, `1.` lists, `[links](url)`, emojis. Used 483:

```markdown
**Physical hardware controls for Claude Code. Press a button. Ship code.**

- **A key per session** — 3 sessions on the grid with project, state & context; press to focus
- **Offline voice** — dictate prompts locally, no cloud
- **Live status** — model, cost & context keys
- **One-press prompts** — Fix Bug, Write Tests, Review & more
- **Answer Claude** — amber on approval, red when risky
- **Git** — commit, push, PR

Works with Claude Code in Apple Terminal or Windows Terminal.
```

## Release notes — limit 1000 characters

First submission, so written as an introduction rather than a changelog (Marketplace users never saw 1.x). Future submissions should switch to a real what's-changed format. No version heading — the page already shows the version from the package. Used 967:

```markdown
Claude Console turns the MX Creative Keypad's nine LCD keys into a control surface for Claude Code.

- **A key per session** — up to 3 Claude sessions, each key showing project, state & context; press one to focus it
- **Live status** — model, running cost & context, straight from Claude Code
- **One-press prompts** — Fix Bug, Write Tests, Review & more, customizable
- **Answer Claude** — Yes/No and menu navigation from the keypad; keys go amber when Claude asks for approval, red when the command is risky
- **Git** — commit, diff, push, create PR
- **Offline voice** — dictate a prompt or jump to a project by name; transcription runs locally, no cloud, no API keys

Setup: install, then import the ready-made keypad layout from the GitHub release — or drag the Claude Console actions onto your own Terminal profile. Live keys are opt-in: one press turns them on. Works with Apple Terminal (macOS) or Windows Terminal; sessions in other terminals are not shown.
```
