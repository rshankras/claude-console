# Marketplace Listing Copy — Vizhi for Codex

Draft text for the Logitech Marketplace submission form
([marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute)) for **Vizhi
for Codex 1.5.3**. Same structure and field limits as Claude Console's
[marketplace-listing.md](marketplace-listing.md); process and packaging live in
[SUBMISSION.md](../SUBMISSION.md).

Character counts below are `wc -m` with a UTF-8 locale, verified against the drafts in this file.
Read that file's **Lessons for next time** before pasting into the form — especially: never paste
from a terminal window (invisible indent counts against the limit), use `pbcopy`.

## Auto-filled fields (from `LoupedeckPackage.yaml`)

Read out of the uploaded `.lplug4`; the form fills them itself. To change any, edit
`src/Products/VizhiCodex/package/metadata/LoupedeckPackage.yaml` and repack.

| Field | Value |
|---|---|
| Name | Vizhi for Codex |
| Author | S.Ravi Shankar |
| Operating system | macOS, Windows (from `pluginFolderMac` + `pluginFolderWin`) |
| Version | 1.5.3 |
| Content licence | MIT — https://opensource.org/licenses/MIT |
| Support | https://github.com/rshankras/claude-console/issues |
| Homepage | https://github.com/rshankras/claude-console |
| Copyright | Copyright © 2026 S.Ravi Shankar. All rights reserved. |

> The repository is named `claude-console` because one engine builds both plugins; its front page
> says so and links to this product's own README. If a reviewer asks, that is the answer.

## Teaser card description — limit 120 characters

**104 characters** (16 spare):

```
Hardware controls for OpenAI Codex CLI in Apple Terminal or Windows Terminal, on the MX Creative Keypad.
```

Runner-up (118): `Drive OpenAI Codex CLI from the MX Creative Keypad — sessions, approvals, offline voice. Press a button. Ship code.`

## Detail page description — limit 500 characters

Markdown supported: **bold**, *cursive*, `-` lists, `1.` lists, `[links](url)`, emojis.
**470 characters** (30 spare):

```markdown
**Physical hardware controls for OpenAI Codex CLI. Press a button. Ship code.**

- **A key per session** — 3 Codex sessions with project, state & context; press to focus
- **Offline voice** — dictate prompts locally, no cloud
- **Answer Codex** — amber on approval, red when risky
- **One-press prompts** — Fix Bug, Write Tests, Review & more
- **Screenshot** — capture a region into the conversation
- **Git** — commit, push, PR

Install one console plugin per machine.
```

## Release notes — limit 1000 characters

First Marketplace release for this product, so written as an introduction rather than a
changelog. No version heading — the page shows the version from the package. **977 characters** (23 spare):

```markdown
First Marketplace release. Vizhi for Codex turns the MX Creative Keypad's nine LCD keys into a control surface for OpenAI's Codex CLI, on macOS and Windows.

- **A key per session** — run up to 3 Codex sessions, each key showing its project, state & context; press one to focus it
- **Answer Codex** — Yes/No and menu navigation from the keypad; keys light amber when Codex asks approval, red when the pending command is risky (macOS)
- **One-press prompts** — Fix Bug, Write Tests, Review & more, fully customizable
- **Screenshot** — capture a screen region into the running conversation
- **Git & /review** — commit, diff, push, PR, and Codex's own review picker
- **Offline voice** — dictate a prompt or jump to a project by speaking its name; local transcription, no cloud

**Install only one console plugin per machine** — this and Claude Console both bind the terminal, and only one can be active.

Setup: install, then import the ready-made keypad layout (`VizhiCodex-Keypad.lp5`) from the GitHub release — or drag the Vizhi for Codex actions onto your own Terminal profile.
```

## Notes for the reviewer / support answers

Things a QA reviewer is likely to hit, with the honest answer ready:

- **Two plugins, one terminal.** If Claude Console is already installed on the test machine, one
  of the two will be unreachable — the plugin service activates a single plugin per application.
  This is why the release notes lead with it. Test on a machine with only one installed.
- **The keys need Codex running in a terminal.** No sessions, no live keys — by design, and the
  keypad says so rather than inventing state.
- **macOS asks for permissions**, once each: Accessibility (typing into Terminal), Microphone
  (voice), Screen Recording (screenshot).
- **macOS also asks Codex to trust the plugin's hooks** — run `/hooks` inside Codex and approve.
  That is Codex's own security model; the plugin can't and shouldn't bypass it.
- **Windows has one deliberate gap**: approval lighting is unavailable, because Codex's hook
  runner does not spawn processes on that platform (upstream; see
  [the spike](spike-windows-codex-hooks.md)). State comes from Codex's own session transcript
  instead. The capability is declared absent rather than faked.
- **Not affiliated with OpenAI.** "Codex" is OpenAI's product; the listing and README both say so.

## Before submitting — checklist

- [x] Package contains exactly one DLL, no `PluginApi.dll` (what got Claude Console 2.0.0
      rejected). Verify: `unzip -l VizhiCodex_<ver>.lplug4 | grep '\.dll'`
- [x] `PRIVACY.md` and `EULA.md` cover this product and both platforms
- [x] Repo front page names this product and links to its README
- [x] Fresh-install test passed (register → import layout → keys live)
- [ ] Screenshots/artwork for the listing page
- [ ] EULA reviewed by counsel (open item in SUBMISSION.md)
