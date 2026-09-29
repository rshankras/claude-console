# Marketplace Listing Copy — Vizhi Desktop

Text for the Logitech Marketplace submission form
([marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute)) for **Vizhi
Desktop 1.0.0**, its first submission — **submitted 2026-09-29** with exactly the text below. Same structure and field limits as
[marketplace-listing.md](marketplace-listing.md) and [marketplace-listing-vizhi.md](marketplace-listing-vizhi.md);
process and packaging live in [SUBMISSION.md](../SUBMISSION.md).

Character counts are Python `len()` (code points, same as `wc -m` in a UTF-8 locale). Copy with `pbcopy`,
never from a terminal window (invisible indent counts against the limit).

## Auto-filled fields (from `LoupedeckPackage.yaml`)

| Field | Value |
|---|---|
| Name | Vizhi Desktop |
| Author | S.Ravi Shankar |
| Operating system | macOS only (no `pluginFolderWin`) |
| Version | 1.0.0 |
| Content licence | Proprietary — https://vizhi.dev/eula/ |
| Support | https://vizhi.dev/faq/ |
| Homepage | https://vizhi.dev/vizhi-desktop/ |

Package: `VizhiDesktop_1.0.0.lplug4`, sha256 `99785a0c05d387e7b8ad6f2b27790eec26d953611b082578224a7a035a837af7`,
GitHub release `vizhi-desktop/v1.0.0` (main `7011148`).

## Teaser card description — limit 120 characters

**107 characters** (13 spare):

```
Keypad controls for the ChatGPT desktop app on Mac: your chats, Codex approvals, dictation and screenshots.
```

## Detail page description — limit 500 characters

**490 characters** (10 spare):

```markdown
**Keypad controls for the ChatGPT desktop app on Mac, in ChatGPT and Codex modes.**

- **Recent chats** — your three latest chats as live keys; press to open
- **Approve or deny** — answer Codex requests from the keypad; press twice to confirm
- **Offline voice** — dictate, check, then Send
- **Screenshot & paste** — add a screen area or copied text to the chat
- **One-press prompts** — Summarize, Explain, Review Code, Run Tests & more

Coexists with Claude Console and Vizhi for Codex.
```

## Release notes — limit 1000 characters

### 1.0.0 — first submission

Written as an introduction. No version heading — the page shows the version from the package.
**994 characters** (6 spare):

```markdown
First Marketplace release. Vizhi Desktop makes the MX Creative Keypad a control surface for the ChatGPT desktop app on macOS, in both ChatGPT and Codex modes.

- **Recent chats** — your three latest chats as live keys showing Ready, Working or Allow?; press one to open it
- **Approve and Deny** — answer Codex permission requests from the keypad, in the same place as Yes and No on the other Vizhi plugins; press twice to confirm, and it only ever allows once
- **Offline voice** — dictate into the chat with local transcription, then check and Send
- **Screenshot, Paste into Chat and Copy Reply** — bring a screen area or copied text in, take the finished answer out
- **One-press prompts and Codex tasks** — Summarize, Explain, Review Code, Run Tests, View Changes & more
- **All Chats and Find Chat** — reach older conversations

Setup: install, and the Vizhi Home layout appears on the keypad whenever the ChatGPT app is in front. Requires the ChatGPT desktop app on an Apple Silicon Mac.
```

## Notes for the reviewer / support answers

- **This plugin is bound to the ChatGPT app** (bundle `com.openai.codex`), unlike the two universal
  terminal plugins. Its keys show only while ChatGPT is in front; that is intended. It installs its own
  layout (**Vizhi Home**), so nothing needs importing.
- **It needs the ChatGPT desktop app**, signed in, on an Apple Silicon Mac. With no app, the keys say Open App.
- **Approve and Deny need two presses** and only ever choose "Allow once". Without a pending request they
  are shown but do nothing; an amber dot means a request is waiting, red that it looks risky.
- **macOS asks for permissions**, once each: Accessibility (read and operate the ChatGPT window), Microphone
  (voice), Screen Recording (Screenshot).
- **Voice** downloads the shared ~148 MB speech model on first use; nothing is sent anywhere.
- **Signing:** the Accessibility helper and voice helper are Developer-ID signed and notarized.
- **Coexists** with Claude Console and Vizhi for Codex.
