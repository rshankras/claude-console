# Marketplace Listing Copy

The exact text entered in the Logitech Marketplace submission form ([marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute)) for the 2.0.0 submission on 2026‑08‑12, resubmitted unchanged as 2.0.1 on 2026‑08‑13 after QA flagged the bundled `PluginApi.dll` (packaging fix only — see CHANGELOG). Reuse and adapt for future submissions; process/packaging steps live in [SUBMISSION.md](../SUBMISSION.md).

## Auto-filled fields (from `LoupedeckPackage.yaml`)

These are read out of the uploaded `.lplug4` — the form fills them itself. To change any of them, edit `src/package/metadata/LoupedeckPackage.yaml` and repack; don't look for a form field.

| Field | Value |
|---|---|
| Name | Claude Console |
| Author | S.Ravi Shankar |
| Operating system | macOS, Windows (from `pluginFolderMac` + `pluginFolderWin`) |
| Version | 2.0.1 (2.0.0 + the PluginApi.dll packaging fix) |
| Content licence | MIT — https://opensource.org/licenses/MIT |
| Support | https://github.com/rshankras/claude-console/issues |
| Homepage | https://github.com/rshankras/claude-console |
| Copyright | Copyright © 2026 S.Ravi Shankar. All rights reserved. |

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

First submission, so written as an introduction rather than a changelog (Marketplace users never saw 1.x). Future submissions should switch to a real what's-changed format. No version heading — the page already shows the version from the package. Used 936:

```markdown
First Marketplace release. Claude Console turns the MX Creative Keypad's nine LCD keys into a control surface for Claude Code, on macOS and Windows.

- **A key per session** — run up to 3 Claude sessions, each key showing its project, state & context; press one to focus it
- **Live status** — model, running cost & context usage, straight from Claude Code
- **One-press prompts** — Fix Bug, Write Tests, Review & more, fully customizable
- **Answer Claude** — Yes/No and menu navigation from the keypad; keys light amber when Claude asks for approval, red when the pending command is risky
- **Git** — commit, diff, push, create PR
- **Offline voice** — dictate a prompt or jump to a project by speaking its name; transcription runs locally, no cloud, no API keys

Setup: install, then import the ready-made keypad layout from the GitHub release (one file, two clicks) — or drag the Claude Console actions onto your own Terminal profile. Works with Claude Code in Apple Terminal (macOS) or Windows Terminal — sessions in other terminals are not shown.
```

## Lessons for next time

- **Don't paste from a terminal window.** Selection picks up invisible indent and trailing spaces that count against the limit — a 491-char draft overflowed the 500-char field that way. Put the text on the clipboard directly (`pbcopy < file`) and ⌘V into the form.
- **Count characters, not bytes**: `wc -m` with a UTF-8 locale (em-dashes are 1 character but 3 bytes; `wc -c` overcounts). Leave ≥15 characters of headroom in case the form counts newlines differently.
- **Feature order is the message**: sessions first, voice second — the differentiators no other keypad plugin has (per-session keys, amber/red approval lighting, fully offline voice).
- Always name the supported terminals (Apple Terminal / Windows Terminal) — "your terminal" wrongly implies iTerm2/Ghostty/Warp work.
- Homepage and Support URLs have their own form fields, so don't spend description characters on links.
