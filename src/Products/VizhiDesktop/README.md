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

**Preview, macOS package only. Version 0.17.17 groups code review in Tasks:** two pages, Home and Tools.
Screenshot is the middle-right Home key in both modes. Find Chat remains middle-right on Tools in ChatGPT; Codex uses Tools → Tasks → View Changes.
Only the stock Tools navigation binding and untouched review label migrate, with backups.
Custom assignments and the optional Adaptive 3 layout are preserved.
Spoken workflows now show **Your request** first, then a shorter labeled task instruction.
Debug investigates before fixing and can report no bug found. Unchanged stock recipes upgrade
with a settings backup; custom recipes remain intact. The speak → finish → review → Send
interaction is unchanged. See the [spoken prompt notes](../../../docs/vizhi-desktop-spoken-prompts-2026-09-22.md).
Copy Reply now identifies the conversation separately from other web-content areas such as
diff previews. It refuses two identifiable conversations and excludes auxiliary Copy controls.
See the [multiple-area copy correction](../../../docs/vizhi-desktop-copy-webareas-2026-09-22.md).
The opt-in, expiring [Copy Reply diagnostics](../../../docs/vizhi-desktop-copy-diagnostic-2026-09-22.md)
remain off by default.
All Mac composer text entry now uses one whole-text insertion path, including older
Dictate & Send assignments and recovery. It verifies the result and preserves clipboard
contents when a native paste is needed. The old 20-character typing fallback is removed.
See the [whole-text implementation and verification notes](../../../docs/vizhi-desktop-whole-text-2026-09-22.md).
The owner confirmed Screenshot → Explain fails with `composer-selection-changed` on 0.17.2.
The rich-text editor can expose a generated placeholder through both value and range APIs.
Version 0.17.3 uses native end-of-input navigation for this case and verifies the inserted
instruction without replacing the composer. Controlled GUI verification and owner acceptance
are tracked in the [attachment insertion notes](../../../docs/vizhi-desktop-attachment-caret-2026-09-22.md).
The owner confirmed the 0.17.3 Screenshot → Explain insertion fix and the Compare/Plan workflow.
A subsequent Codex Dictate failure exposed missing mode-specific input hints in the adapter.
Version 0.17.4 adds the installed app's exact Chat, Codex, Plan and Goal hints to the shared
insertion path. See the [Codex dictation notes](../../../docs/vizhi-desktop-codex-dictation-2026-09-22.md).
Attach Files now lists recent Downloads on the keypad. Select files and press Attach; Browse
opens the app's picker for files elsewhere. Paste into Chat accepts copied text, images, or files.
Existing input is preserved. Summarize and Explain use supplied material and prepare a draft
for review instead of rejecting it as an existing draft.
Home keeps Screenshot in the same position in both modes. Tools keeps Attach Files and
Paste into Chat, with Find Chat in ChatGPT and Copy Reply below. Codex approvals remain at the top; its task prompts are
under Tasks. Clear Added and source-less Return are absent from default menus.
The stability changes from 0.16.1 move slow work off keypad callbacks, limit background polling, and clean up
subscriptions and timers on unload. See the [stability notes](../../../docs/vizhi-desktop-stability-2026-09-21.md).
The Tools middle key is now **Paste into Chat**: copied text appears immediately in the input,
below any existing draft. Home **Dictate** appends spoken instructions without duplicating the
email or sending it. Review the completed draft, then press **Send**. The key shows **Pasted**
only after insertion is confirmed. See [the workflow and implementation notes](../../../docs/vizhi-desktop-paste-into-chat-2026-09-21.md).
Send uses an upward arrow in a circle, with a dimmed version for an empty composer. The same
Home key shows the square Stop control during a response.
Copy Reply is intended to copy the latest completed answer with one tap, preserving native clipboard formatting.
Version 0.15.13 removes a confirmed false refusal when the window contains multiple text areas.
Copy targets the response action without requiring a unique composer; write/send guards remain separate.
Version 0.15.13 also recognizes separately wrapped response controls by requiring Copy
and two response actions to occupy one compact horizontal row. Rate response and its
selected-feedback labels are recognized. Copy Reply was confirmed working by the owner
in the real ChatGPT app using the MX keypad on 2026-09-20.
It shows Wait during activity, dims when no answer is available, and confirms Copied after readback.
Voice Chat uses a waveform in a circle matching Send's size, stroke and colour. Dictate uses a
simple microphone to distinguish speaking into a draft from a live spoken conversation.
The macOS reader recognizes the installed app's Working status label and aria-current
selection marker. Both were missing from the earlier reader; the native fixture now reproduces
those semantics with AXSelected false.
Visible task activity now updates the conversation tile when the app identifies one unique
selected conversation. Missing or ambiguous selection never assigns that activity to a guessed key.
The conversation reader now accepts explicit Thinking / Complete sidebar badges, including
named images and accessibility descriptions. Current-app compatibility still needs hardware
acceptance; a status label inside a response is not attributed to a sidebar chat by guesswork.
Home retains three direct conversation keys and puts **Dictate · Send/Stop · Voice Chat** in the
bottom row. Native Voice Chat is still the app's own voice feature. Dictate always prepares text
for review, including with captured sources; it never becomes a Send button. The owner confirmed
automatic draft insertion in 0.12.13. New layout/dispatch coverage uses unit tests and the native
fixture; physical keypad and live app acceptance remain owner-run. Windows packaging is disabled.

## Setup — the part that matters

Installing gives you an auto-imported page that appears **when the ChatGPT app is frontmost**.
That page is useful, but it is not the point of this product, because a Logi application profile
is only active while its application is in front — and the whole idea here is answering the agent
while you are somewhere else.

**So the first thing to do after installing is place four keys on your default profile**, which is
active when an application-specific profile does not override it:

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

**Vizhi Home** is the default packaged layout. Earlier profiles remain available.

| Page | Top row | Middle row | Bottom row |
|---|---|---|---|
| Home — ChatGPT | Recent chat 1 · 2 · 3 | Chats · New Chat · Screenshot | Dictate · Send/Stop · Voice Chat |
| Home — Codex | Recent task 1 · 2 · 3 | Chats · New Task · Screenshot | Dictate · Send/Stop · Voice Chat |
| Tools — ChatGPT | Mode · — · — | Attach Files · Paste into Chat · Find Chat | Copy Reply · Prompts · More |
| Tools — Codex | Mode · Approve · Deny | Attach Files · Paste into Chat · — | Copy Reply · Tasks · More |

The two pages stay in place across mode changes. Dictate and Send/Stop keep their physical
positions; native Voice Chat moves directly onto Home. The Mode key names the current mode and
shows **TO CODEX** or **TO CHATGPT**. Approve/Deny retain their old Actions-page positions on Tools,
including the request guard, risk badge and second confirmation for a high-risk request.

**Send/Stop:** the key shows Send when an existing draft can be submitted, Stop while a response
is running, and a disabled state when neither action is available. It executes the displayed verb:
a Stop press cannot turn into Send when a response ends just before the tap. Rapid duplicate taps
are suppressed. Stopping a response never invokes the native Voice Chat stop control.

**Prompts** in ChatGPT keeps its nine configurable favorites. The default Codex **Tasks** menu is:

| Top row | Middle row | Bottom row |
|---|---|---|
| View Changes · Review Code · Run Tests | Debug · Refactor · Explain Diff | Fix CI · Security · — |

**More** holds Continue, Update Deps, optional Review PR / Write Tests, and available occasional app navigation.
Custom task content and order are retained; custom layouts may use host paging. Continue and
Update Deps are available from More using their configured slots. ChatGPT’s Continue stays in Prompts. Source clearing appears only when explicitly staged
sources exist. See the [Tasks menu notes](../../../docs/vizhi-desktop-tasks-menu-2026-09-23.md).
Find Chat opens the query input: use Speak Query, or tap the query tile to focus typing/retry.
Results are selectable on the keypad. NO MATCHES YET differs from unavailable search.
Chats lists recent conversations exposed by the app, not guaranteed complete account history.

ChatGPT workflows: **Summarize, Explain, Rewrite, Draft Reply, Compare, Research, Brainstorm,
Plan, Continue**. Codex workflows: **Review Code, Debug, Refactor, Run Tests, Explain Diff,
Fix CI, Security, Update Deps, Continue**. Review Code checks the uncommitted diff; Run Tests
runs existing tests. Review PR and Write Tests are available from More.

**SPEAK workflows:** Draft Reply, Rewrite, Compare, Research, Debug, Refactor, Fix CI and Review PR.
Tap the workflow, speak the needed scope, and tap again to finish. Vizhi composes and inserts the
complete brief automatically. The same key becomes **Send Draft / REVIEW FIRST**: review the
app's input, then tap to submit. Home's Send also submits and resets the workflow key.
No blank template is inserted before dictation. Window/editor/mode and any exposed selected chat
are pinned before recording; changes refuse delivery and retain the complete brief for retry.
The app must expose those accessibility signals; the native fixture cannot establish live app
compatibility. Failed insertion shows **Insert Draft / HOLD TO DISCARD**, with the same tap retry
and hold discard behavior as Voice Draft. Plain Dictate remains available for freeform input.

**SEND** workflows with an empty composer submit their clearly scoped request immediately.
With existing input or staged sources they preserve that material and prepare an unsent draft;
the same key becomes Send Draft. Summarize, Explain, Brainstorm, Plan, and Continue have
source-specific instructions. A source task does not silently summarize the earlier conversation.
Success shows **Sent / REQUEST SENT**: actual progress belongs to the conversation state and
results in the app. Fix CI now collects a run/branch brief; Update Deps and Write Tests prepare
reviewable editing requests. Tools → Tasks → View Changes opens the diff while the keypad stays in Tasks; repeated taps leave the panel open. Opened means the panel was confirmed; Not available means no app-owned review route is exposed; Couldn’t open means a supported opening could not be confirmed. Review Code asks Codex for findings.
Codex mode alone does not establish Git Review availability. Website previews and site-building
conversations may have no Review surface. View Changes stays dimmed with **Not available**
for these contexts; it does not send a guessed keyboard shortcut or claim there are no changes.
A real Review panel remains available even when its diff is empty. Review controls in an embedded
website cannot enable the app command. See the [Review availability notes](../../../docs/vizhi-desktop-review-availability-2026-09-23.md).
App-control requests show Requested unless completion is observed. Mode switching verifies
the destination. Native Voice Chat shows End Voice when its active state is observable.

On upgrade, only unchanged stock workflow slots migrate to the new defaults. Sibling
`.before-flow` and `.before-0.17` files preserve the original JSON during their respective upgrades, and customized prompts/slots and metadata stay
intact. Reordered or shortened collections are left in place. Set `Input` to `voice` and include
`{brief}` in a custom template to opt into the spoken flow. `Submit: false` without voice input
retains the ordinary draft-and-edit behavior. Example configurations ship under `examples/`.

## Attach documents without the keyboard

1. Open the intended chat, then **Tools → Attach Files**.
2. Tap one or more files from recent **Downloads**. SELECTED marks each choice; tap again to undo.
3. Press **Attach N**. Verified attachment returns to Tools; no message is sent.
4. Choose **Prompts → Summarize** or **Compare**, or return Home and Dictate an instruction.
   Review the input and explicitly Send.

The list reads Downloads only when opened or refreshed: up to 18 recent eligible regular files
from a bounded scan of 2,048 entries. There is no background indexing. Files must be nonempty,
not symlinks, and at most 50 MiB each; one operation permits eight files / 100 MiB total. The app
may impose its own type or upload limits. Browse opens its normal picker for other locations.
A changed file/chat refuses attachment. Check Files means acknowledgment was uncertain; the
same open picker cannot retry that paste. Inspect the composer before making a new selection.
Files with conflicting exposed names can be refused to avoid falsely acknowledging an old item.

**Paste into Chat** also handles files copied in Finder and image clipboard data. File/image
payloads take precedence over filename/description text. Clipboard images are saved privately
as PNGs; the operation restores the clipboard unless a newer copy supersedes it.

## Reply to a customer using source material

1. In the email, highlight the customer's text and copy it with **⌘C**.
2. Open the intended ChatGPT conversation. Press the physical right paging button to reach
   **Tools**, then tap its middle **Paste into Chat** key. The email appears immediately;
   **Pasted** confirms insertion. Existing input is preserved.
3. Return to **Home → Dictate**. Say “Draft a polite reply confirming Friday delivery,” then tap
   again to finish. Your instruction appears below the email. Review, then press **Send**.
4. After the answer finishes, go to **Tools → Copy Reply**. No selection is needed.
5. Return to your email, paste the copied reply, review it, and send it yourself.

**Use Selection** on the optional **System → Ask ChatGPT** page still stages highlighted text
and records its source window. With that route, Dictate inserts the instruction and staged
sources together, and **Return to App → Paste Reply** can insert the copied answer into the
empty reply field you select. Clipboard text imported while inside ChatGPT has no known source;
use your usual app switch to return to the email.

**Home → Screenshot** opens the macOS region picker. Choose a region, or press Escape to cancel.
The screenshot attaches immediately to the chat you started from, preserving existing input.
The key shows **Select Area → Attaching → Attached**. Type your instruction or use **Dictate**,
then review the thumbnail and press **Send**. Dictate does not attach the same image again.
Switching chats or modes during capture refuses the attachment. **Check Image** means the app's
attachment acknowledgement could not be verified: inspect the composer before capturing again.
Remove unwanted thumbnails in ChatGPT; **Clear Added** only clears explicitly staged sources.
The first screenshot may require Screen Recording access for the Logi service/helper.
Actual attachment accessibility varies by app version and needs live testing.

Up to eight sources (50,000 text characters total) can be staged. Source counts appear on capture
keys. Captures do not submit messages. A failed capture does not reuse old clipboard contents;
previously staged sources remain until used or cleared. Source text and the retained reply stay
in memory; screenshots are private files under `~/.claude/claude-console/desktop-captures/`.
Clear Sources clears staging only, not files, clipboard contents or text already in an app.

**System access:** this installation adds an Ask ChatGPT page without replacing existing System
keys. The reusable `tools/install-desktop-context-page.py` installer accepts the explicit selected
System `ProfileInfo.json`; the plugin never changes System at startup. App-specific keypad profiles
can take priority over System. Assign **Vizhi Desktop → Context → Ask ChatGPT** to those profiles
if needed. Returning uses the originally captured source window; a closed/replaced window reports
Source Closed. Paste Reply requires the original app and the empty field you explicitly selected.

## Start with one draft

1. Open the intended conversation and go to **Home**.
2. Press **Dictate**, speak, then press it again to finish. **Listening · TAP TO FINISH**
   indicates recording; **Preparing · WAIT** indicates processing.
3. Your words appear in the input, below any existing draft. Read and edit them, then press
   **Send** on the keypad. Another Dictate press adds more detail without submitting.
   Insertion can temporarily use the clipboard for native paste and restores its contents,
   unless you copied something newer. If **Insert Draft · HOLD TO DISCARD** appears, the
   transcript is retained in memory. **Tap** to retry against the original chat and draft,
   or **hold** until **Discarded** appears to forget the retained recording. Discarding
   does not clear text already in the app. If the chat or draft changed during recording,
   insertion is refused. Retries never overwrite another draft or send automatically.
   The retained draft otherwise lasts until insertion or plugin restart. Releasing a hold
   never retries insertion or starts another recording.
4. Use **Stop** to interrupt a running response. Its square icon is distinct from navigation.

**Dictate & Send** remains an optional action and keeps working in older profiles. It sends the
transcript when you stop dictating. Workflows marked SEND also submit immediately;
DRAFT workflows wait for your edit. These keys address the window targeted when the operation
runs, so keep the intended conversation open through transcription. Home Dictate and spoken saved workflows preserve existing input. Fixed prompts and the optional
Dictate & Send action retain their existing empty-composer requirements.
Voice Draft errors stay visible until your next attempt. **Not typed** means insertion failed and
draft retention did not succeed; **No speech** means no usable transcript was produced. The
retained-draft retry applies only to Voice Draft, never to Dictate & Send. Version **0.17.6**
replaces the old character-event fallback with complete-text insertion or one verified paste.
Partial insertion is never followed by another full copy or automatic submission.

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

With the confirmed shortcut configured:

1. Keep the intended ChatGPT chat or Codex task frontmost. Press **Voice Chat · TOGGLE** at the
   bottom-right of Home to start; press again to stop.
2. Complete any first-use setup in ChatGPT. **Requested** means the shortcut was posted, not that
   the app acknowledged it. The key stays **Voice Chat / TOGGLE** in both states.
3. **Open App** means ChatGPT was not frontmost/running and no shortcut was sent. **Dictating**
   means finish the local Voice Draft capture first. Rapid repeat presses are suppressed.
4. **Stop** on Home interrupts task generation; it does not end Voice Chat.

The local configuration is `~/.claude/claude-console/desktop-voice-shortcut.json`:

```json
{ "toggleVoiceChat": "Control+Shift+V" }
```

Match this to the shortcut shown in your own ChatGPT settings, then reload the plugin. The parser
accepts macOS ANSI letter-key positions with Control, Shift, Option, and Command modifiers and
requires Control or Command. This screenshot-confirmed mapping is not assumed for other users:
the package does not seed the file. Without a valid configuration, the original exact-button
mode remains: **Voice Chat / TALK**, **End Voice / ACTIVE**, or **No Voice / CHECK APP**. No Voice
means detection failed to find a supported control, not that the feature is absent from ChatGPT.

The plugin serializes its native and dictation keys: local capture/transcription blocks native
start, and an observed active native session blocks a new local dictation. Existing local capture
can always be stopped. These checks cannot coordinate atomically with voice started outside Vizhi
or in another app window. Shortcut posting does not establish session state; when AX recognition
is unavailable, end native Voice in ChatGPT before starting offline Voice Draft. Mute/unmute and
long-press gestures are deferred until verified. The screenshot also confirms Control–Shift–D
starts ChatGPT dictation, but this update retains the existing offline Voice Draft workflow.

## Choose a layout

- **Vizhi Home** ships as the two-page default: Home and mode-aware Tools.
- **Vizhi Adaptive 3** remains an optional three-page layout with approvals on its first page.
  Import [VizhiDesktop-Adaptive3.lp5](package/optional-profiles/VizhiDesktop-Adaptive3.lp5) explicitly.
- Installed Flow 2, Flow, Everyday, Adaptive and customized profiles are retained. Ordinary
  updates add the packaged layout without changing the selected profile. This user-authorized
  redesign installation explicitly selects Vizhi Home and preserves the System source page.

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

**Review Code** and **Run Tests** are also available as separate optional actions in Options+.
Review Code scopes itself to uncommitted work and stops if the tree is clean. Review PR
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
