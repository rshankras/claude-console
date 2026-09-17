# Vizhi Desktop

**Physical hardware controls for the ChatGPT / Codex desktop app on the Logitech MX Creative Keypad.**

> Answer your agent without going to it.

Vizhi Desktop watches the desktop app through the macOS Accessibility API and puts its approval
prompt on a physical key. When Codex needs permission to run something, the key lights — amber for
routine work, red when the command it is asking about looks destructive — and one press answers it
**without the app ever coming to the front**. Your editor keeps focus. Nothing is typed into the
wrong window, because nothing is typed at all.

Built on the same engine as [Claude Console](../../../README.md) and
[Vizhi for Codex](../VizhiCodex/README.md): one core, one adapter per surface, one package per
product. *(Not an OpenAI product; ChatGPT and Codex are trademarks of OpenAI.)*

> **This one coexists.** Unlike the two terminal consoles — which both bind to Terminal.app and
> therefore cannot both be active — Vizhi Desktop binds to the ChatGPT app's own bundle, which
> nothing else claims. You can run it alongside Claude Console or Vizhi for Codex.

## Status

**Preview, macOS package only.** The core Accessibility mechanism is verified end to end on a live app:
reading the approval card unfocused, pressing Allow/Deny with another app frontmost and no focus
theft, writing and submitting the composer, and switching the app between its ChatGPT and Codex
modes — all while the app sat in the background. Version **0.12.0** adds native Voice Chat and the
Adaptive 3 layout. Voice uses documented exact button labels and enables only when those controls
are observed. Native start/end, first-use setup, and microphone behavior still need a live hardware
pass; automated validation uses synthetic app states and does not record audio. The user's current
app still reports **No Voice** after 0.12.1, and Voice Draft transcription has succeeded while
composer insertion failed. Version **0.12.2** adds clipboard recovery for refused drafts; it does
not claim to repair native Voice detection or direct insertion. Copy Answer remains
unavailable pending a verified assistant-response selector. A fail-closed Windows UI
Automation foundation exists, but application-identity reconnaissance and live validation have
not been run, so Windows packaging remains deliberately disabled.

## Setup — the part that matters

Installing gives you an auto-imported page that appears **when the ChatGPT app is frontmost**.
That page is useful, but it is not the point of this product, because a Logi application profile
is only active while its application is in front — and the whole idea here is answering the agent
while you are somewhere else.

**So the first thing to do after installing is place four keys on your default profile**, which is
active no matter what you are looking at:

| Key | What it gives you |
|---|---|
| **Activity** | Working / Waiting / Ready at a glance — and *Hidden* when the app's window is locked or gone, because "cannot see" is not the same as "nothing to do" |
| **Approve** | Answers the pending request. Amber for routine, **red** when the command looks destructive — press **Show ChatGPT** if you want the exact wording before deciding |
| **Deny** | The other half. Lit only while something is actually pending |
| **Show ChatGPT** | The escape hatch: brings the app forward when you want to look before deciding |

In Logi Options+, select your default profile, then drag those four actions from **Vizhi Desktop**
onto whichever keys you like.

**Why you have to do this by hand:** your default profile is your configuration. A plugin that
imported itself over it would wipe whatever you already had there, so this product doesn't. It is
one placement, once.

## Updates

Updates refresh the installed automation helper when its packaged contents change. A failed
copy or signature check leaves the previous helper in place so the next load can retry.
New packaged layouts are added alongside existing profiles. Your selected default stays selected;
choose the new layout in Options+ when you want to adopt it.

## The auto-imported pages

Bound to the app, so they appear while ChatGPT is frontmost.

The profile is **mode-aware**. ChatGPT and Codex share one app and one profile; common keys stay
put, while contextual keys follow the mode of the focused ChatGPT window. If a ChatGPT window and
a Codex window are both open, the frontmost one wins. An unavailable control says so and does
nothing rather than searching another window.

**Page 1 · Conversations — the home page.** Three large, readable keys are your three most recent
conversations. Each card keeps the title in its upper area and reports **Ready**, **Thinking**,
**Allow?**, or **Complete** in a coloured bar below it. Complete means ChatGPT marked the finished
result unread; a conversation already open when it finishes returns directly to Ready. Positions
stay stable while the sidebar reorders. The middle row is **All Chats · New Chat · Search** in
ChatGPT and **All Chats · New Chat · Changes** in Codex. Changes becomes **No Changes** and is disabled
until the focused Codex task exposes a diff. All Chats opens a paged folder containing every
conversation the focused window makes visible; press one to jump. The bottom row answers the one
that's waiting: **Approve · Deny · Voice Chat**. Idle approval glyphs are grey; pending requests add a risk badge.
The approval row keeps its position when app modes change.

**Page 2 · Controls.** The top row is always **Mode · Stop · Voice Draft**. Mode shows the
current mode with switching arrows; pressing it selects the other mode. The remaining verified
controls adapt by mode: **Projects · Plugins · Scheduled · Explore** in ChatGPT and
**Permissions · Attach Files · Pull Requests · Quick Chat** in Codex. The final two positions are
**Send** and **Copy Answer / Changes**. Copy Answer currently reads **No Answer**: this release
does not have a verified way to distinguish the latest assistant response from other Copy buttons.

**Page 3 · Workflows.** The nine stable positions become general conversation workflows in
ChatGPT (**Summarize, Explain, Rewrite, Draft, Compare, Research, Brainstorm, Plan, Continue**) and
development workflows in Codex (**Review PR, Debug, Refactor, Write Tests, Explain Diff, Fix CI,
Security, Update Dependencies, Continue**). Workflows missing a target are drafts: Vizhi fills the
composer and brings the app forward for editing instead of sending an incomplete request. Each
key has a **DRAFT** or **SEND** strip; existing composer text is preserved and the workflow shows
**Draft Exists**. New Codex defaults draft Review PR, Debug, and Refactor because their targets
need to be supplied. Your existing workflow JSON retains its own submission choices.

## Start with one draft

1. Open the intended conversation and go to **Controls**.
2. Press **Voice Draft · DRAFT**, speak, then press it again to finish dictation. The key shows
   **PRESS TO STOP** while recording and **Transcribing · WAIT** while processing.
3. Read and edit the transcript in the app. If **Paste Draft · CMD+V** appears, insertion was not
   confirmed and the transcript was copied to your clipboard. Check for any text already inserted,
   click the intended composer, and paste with **Cmd+V** as needed. This replaces your previous
   clipboard contents. Press **Send** when the draft is ready.
4. Use **Stop** to interrupt a running response. Its square icon is distinct from navigation.

**Dictate & Send** remains an optional action and keeps working in older profiles. It sends the
transcript when you stop dictating. Workflows marked SEND also submit immediately;
DRAFT workflows wait for your edit. These keys address the window targeted when the operation
runs, so keep the intended conversation open through transcription. Existing draft text blocks
insertion; move or finish that draft before starting another workflow or dictation.
Voice Draft errors stay visible until your next attempt. **Not typed** means insertion failed and
clipboard recovery did not succeed; **No speech** means no usable transcript was produced. The
clipboard fallback applies only to Voice Draft, never to Dictate & Send.

Send does not replace text. It requires a non-empty draft, one composer, and an enabled exact
Send control in the composer's local container. It refuses while an approval or Stop control
is present, on ambiguous/incomplete surfaces, or if its window/composer/draft changes during
validation. **No Draft** means no eligible draft; **Not Sent** means the guarded operation failed.
This cannot be treated as an atomic app API: live multi-window testing remains required.

## Talk with native Voice

Native Voice is the app's live spoken conversation. ChatGPT manages its microphone, audio,
account, and permissions; this key does not start the offline transcription helper or require
an API key. Voice is supported in Chat, Work, and Codex subject to availability, including existing
Codex tasks as that capability rolls out. See the [official Voice documentation](https://learn.chatgpt.com/docs/features/voice).

1. Open the chat or task you want to discuss, then press **Voice Chat · TALK** on Adaptive 3.
2. Complete any first-use voice setup or microphone permission prompts in ChatGPT. **Check App**
   means the key requested a transition; it is not a claim that recording started.
3. When the app exposes its stop-voice control, the key becomes **End Voice · ACTIVE**. Press it
   to end the session. ACTIVE describes the session, not whether its microphone is muted.
4. **Stop** on Controls continues to interrupt a task; it does not end the voice conversation.

**No Voice** means this window exposes no unambiguous supported voice control. Check feature
availability and the app's Voice settings. There is no guessed keyboard shortcut fallback.
Only the focused/main app window is addressed; return to the window with the voice session to
end it. A changed or ambiguous state refuses the press instead of toggling the opposite action.

The plugin serializes its native and dictation keys: local capture/transcription blocks native
start, and an observed active native session blocks a new local dictation. Existing local capture
can always be stopped. These checks cannot coordinate atomically with voice started outside Vizhi
or in another app window. Mute/unmute and long-press gestures are deferred until verified.

## Choose a layout

- **Vizhi Adaptive 3** keeps Approve · Deny and adds native Voice Chat on the home page. It is the packaged default
  for new installations. Updates preserve the currently selected profile; choose Adaptive 3 in
  Options+ to adopt the new layout.
- Earlier **Vizhi Adaptive 2** and customized profiles are retained with their existing assignments.
- **Vizhi Everyday** replaces only the home approval row with **Voice Draft · Send · Stop**.
  Voice Draft is the dictation-to-draft action. The other two pages are the same. Import
  [VizhiDesktop-Everyday.lp5](package/optional-profiles/VizhiDesktop-Everyday.lp5) explicitly in
  Options+; it is stored outside the auto-import folder and never selected automatically.

Both profiles follow ChatGPT/Codex mode. Everyday is a user-selected alternative; switching
app modes never swaps approval keys into the dictation row. Existing customized profiles are retained.

## Short chat labels and workflow favorites

Configuration lives in `~/.claude/claude-console/`. Reload Vizhi Desktop or restart Logi Plugin
Service after editing these files. Examples are in [package/examples](package/examples).

**Conversation labels:** create `desktop-conversation-labels.json` with exact app titles:

```json
{
  "ChatGPT": { "Planning the September trip": "Trip" },
  "Codex": { "Review Vizhi Desktop integration": "Vizhi" }
}
```

Aliases appear on conversation cards, in All Chats, and on identifiable approval captions. They do not rename chats or change the
original title used to navigate or verify approvals. Missing, invalid, empty, or duplicated
aliases fall back to the original title. Mode names and title keys are case-sensitive.

**Workflow favorites:** edit `desktop-chatgpt-workflows.json` and `desktop-workflows.json`
(Codex). The first nine usable entries fill the nine positions in order. Each entry has `id`,
`label`, `icon`, `prompt`, and `submit`; `false` drafts, `true` sends. Existing files are never
rewritten during upgrades. Use an explicit `submit` value when creating a favorite; older
entries without one retain their existing send behavior.

**Review Changes** and **Run Tests** are also available as separate optional actions in Options+.
Review Changes scopes itself to uncommitted work and stops if the tree is clean. Review PR
requires a PR identifier. Run Tests executes existing tests and reports evidence; Write Tests
asks for new coverage. To put an optional action on the adaptive workflow page, replace one
entry in the Codex JSON with its example from `desktop-workflow-extras.json`.

## What it will not do

- **No "Always allow" key.** The app offers it; this keypad does not bind it. Standing permission
  to run anything should not be one elbow away on a physical device.
- **No cost or context keys.** The desktop app publishes neither, and this keypad never shows a
  number the agent did not report. (The terminal consoles show cost for Claude Code because Claude
  Code reports it.)
- **No generic Copy fallback.** An arbitrary Copy control may belong to a user message, code
  block, or older response. Copy Answer stays unavailable until message ownership is verified.

## Permissions

- **Accessibility** — for the Logi Plugin Service, so the plugin can read and press the app's
  controls. macOS will ask once.
- **Microphone** — only if you use the Voice key, and granted to the small bundled helper rather
  than to the service. Shared with the other consoles: one helper, one grant, one speech model.

## Requirements

- macOS 13+, Apple Silicon or Intel
- The ChatGPT desktop app, signed in
- Logi Options+ with the Logi Plugin Service 6.4 or newer
- MX Creative Keypad
