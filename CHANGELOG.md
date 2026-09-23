## Vizhi Desktop 0.17.17 — Continue in More

- Move Codex Continue from Tasks to More, keeping the default Tasks menu to eight clear actions and an unused bottom-right position.
- Preserve its configured prompt, draft/send behavior and Codex mode guard. ChatGPT Continue stays in Prompts; direct assignments remain available.
- No profile or workflow-file migration is needed; both menus resolve the existing configured slot.

## Vizhi Desktop 0.17.16 — Review tools in Tasks

- Move View Changes into Codex Tasks, beside Review Code and Run Tests. Keep Screenshot on Home and ChatGPT Find Chat on Tools.
- Rename only the untouched Review Changes prompt to Review Code; its scope remains the uncommitted diff, with no edits.
- Keep the default Tasks menu to nine keys by moving Update Deps to More. Preserve configured task content and custom ordering.
- Pin Tasks bindings to Codex, retain review availability checks, and migrate only the stock default-profile navigation binding with backups. Legacy direct assignments and Adaptive 3 remain compatible.

## Vizhi Desktop 0.17.15 — View Changes availability

- Enable View Changes only when the app exposes a unique enabled Changes control or an existing Review panel; Codex mode alone is insufficient.
- Show Not available for a missing review route, keep opening failures distinct, and remove the unverified default keyboard fallback.
- Scope Review controls to the app web area containing its mode control, excluding embedded browser content. Keep empty-but-valid Review panels available.
- Preserve the Screenshot-on-Home profile, draft contents, explicit Send and custom assignments.

## Vizhi Desktop 0.17.14 — Screenshot on Home

- Put Screenshot in the middle-right Home position in ChatGPT and Codex, using the existing capture-and-attach workflow and explicit Send.
- Move Find Chat / View Changes to the same position on Tools; keep both pages and the other Home keys unchanged.
- Upgrade only the stock pair in Vizhi Home, with a profile backup. Preserve custom keys, profile selection and the optional Adaptive 3 layout.

## Vizhi Desktop 0.17.13 — Open Review when its summary button is hidden

- Use the installed app's idempotent Open review tab shortcut (Control–Shift–G) when no Changes summary button is exposed.
- Run the shortcut inside the same pinned, foreground Codex-window operation and confirm the review panel before showing Opened. Repeated taps leave an already-visible panel open.
- Refuse disabled/ambiguous controls, wrong mode and dialogs; do not fall back to a toggle, type text, change the clipboard or submit a draft.


## Vizhi Desktop 0.17.12 — Formatted change counts

- Recognize Changes summary buttons whose line counts contain locale grouping separators or whose count spans form a compact accessible name, such as Changes+1,234-56.
- Keep exact destination names, numeric-only suffixes, one enabled target, no nested controls, and positive panel confirmation.
- Distinguish missing and multiple openers in the expiring diagnostic codes. The owner's previous code establishes an opener refusal; live acceptance remains separate from the reproduced formatting regression.


## Vizhi Desktop 0.17.11 — View Changes diagnostic

- Preserve allowlisted panel-opening failure codes instead of replacing every refusal with panel-unconfirmed.
- Add opt-in diagnostics for explicit View Changes taps, with 30-minute expiry and an 80-record limit. No conversation, clipboard or arbitrary helper output is logged.
- Panel targeting and keypad layouts are unchanged; this release diagnoses the owner's unconfirmed live failure.


## Vizhi Desktop 0.17.10 — View Changes stays on Home

- Rename Codex Home's Changes to View Changes and run it directly, eliminating the folder flash. ChatGPT retains Find Chat.
- Confirm the review panel before showing Opened, report failures, and leave an already-open panel visible on repeat taps.
- Remove per-file and file-list toggle fallbacks; apply the same guarded operation to legacy Changes commands.
- Back up and migrate only the stock Home binding while preserving custom assignments and profile selection.


## Vizhi Desktop 0.17.9 — spoken requests first

- Put the recognized request first in every stock spoken workflow, followed by a separate, shorter task instruction.
- Make Debug investigate first and allow a no-bug-found result before asking for a fix.
- Upgrade only unchanged stock recipes, preserve custom prompts and metadata, and back up previous settings.
- Keep the existing speak, finish, review and Send interaction with no additional key presses.

## Vizhi Desktop 0.17.8 — Copy Reply with auxiliary panels

- Fix the confirmed refusal when the focused Codex window contains multiple accessibility web areas.
- Select the unique area with recognized conversation speaker headings; exclude other areas' copy controls.
- Keep nested panels as content boundaries, and refuse ambiguous conversations, unfinished turns and changed targets.
- Reproduce the original refusal and test nested conversations, preview Copy buttons and ambiguity in isolated Chromium fixtures.

## Vizhi Desktop 0.17.7 — temporary Copy Reply diagnosis

- Add opt-in Copy Reply tracing that expires after at most 30 minutes and records at most 80 fixed status codes per service instance.
- Distinguish keypad dispatch, busy refusals and native copy results without recording chat or clipboard contents.
- Preserve copy behavior and profiles; the reported Codex failure still requires a captured owner attempt.

## Vizhi Desktop 0.17.6 — whole-text insertion for every Mac command

- Consolidate composer write and append, including legacy Dictate & Send and recovery, on one verified whole-text insertion path.
- Remove character-chunk typing and its four-second deadline, including the legacy reply-field fallback.
- Use one insertion-only accessibility write, or one clipboard-preserving paste when that write is ignored; never repeat after partial insertion.
- Preserve conversation, mode, focus, existing text, attachments and explicit-send guards. Search already writes its complete query.
- Exercise long multilingual prompts, legacy and current paths, retry, partial failures, clipboard formats and changed destinations in controlled fixtures.

## Vizhi Desktop 0.17.5 — complete task prompts

- Insert immediate ChatGPT and Codex presets through the verified whole-draft path before sending.
- Preserve the original prompt and destination after insertion failure; retry cannot treat a partial prefix as new source material.
- Send an immediate preset only while its full text and destination remain unchanged; recovered drafts wait for explicit Send.
- Suppress rapid repeat taps after a confirmed submission.

## Vizhi Desktop 0.17.4 — Codex dictation input hints

- Recognize the installed app's exact Codex, Chat, Plan and Goal composer hints, alongside Work with ChatGPT.
- Let the shared insertion helper distinguish those generated hints from real text when dictation stops.
- Cover start/stop delivery in both modes, native empty/literal drafts, attachment preservation, retries and explicit Send.
- Preserve the transcript as an unsent draft; Dictate remains Dictate after successful insertion.

## Vizhi Desktop 0.17.3 — rich-text placeholder insertion

- Handle generated placeholder text that appears in both AXValue and AXStringForRange after an image enables Send.
- Use native end-of-input navigation for this known placeholder, insert with an empty selection, and verify the full instruction.
- Preserve literal drafts with the same wording, attachments, the clipboard, and explicit Send; verify retries without duplicating text.
- Add a Chromium CSS-placeholder screenshot workflow and an AppKit regression for its range/cursor mismatch.
- Live keypad acceptance remains separate from controlled fixture results.

## Vizhi Desktop 0.17.2 — insertion failure feedback

- Preserve the reason for failed prompt insertion and show cursor, focus, or changed-input hints.
- Record only fixed failure codes for explicit failed insertions; omit prompt and chat contents.
- Keep the original instruction and target across retries, with explicit Send after insertion.
- The remaining live Screenshot → Explain failure is under investigation; this update makes its refusal observable.

## Vizhi Desktop 0.17.1 — Explain after screenshot

- Fix instruction insertion when attachments enable Send but the empty composer exposes placeholder text.
- Verify emptiness through editable text ranges; preserve actual drafts with the same wording.
- Cover Screenshot → Explain, unsent attachment preservation, and duplicate-free insertion retries.

## Vizhi Desktop 0.17.0 — complete capture and prompt workflows

- Pick recent Downloads on the keypad, select multiple files, and attach them to the pinned chat.
- Paste copied files and images directly into chat while preserving text and clipboard contents.
- Compose source-aware instructions with existing input; keep captured material unsent until review.
- Clarify prompt scope and submission feedback, and migrate only unchanged stock settings.
- Keep Home fixed and common Tools actions in consistent positions across ChatGPT and Codex.
- Simplify search, remove default Clear Added/Return, and keep occasional navigation in More.
- Preserve native Voice Chat, screenshot, Copy Reply, approval guards, and bounded background work.

## Vizhi Desktop 0.16.3 — screenshot directly into chat

- Attach a screenshot as soon as region capture completes, without requiring Dictate or a workflow.
- Pin the destination before the picker, preserve existing draft text, and never submit automatically.
- Show Select Area, Attaching, and Attached; an uncertain result asks the user to Check Image.
- Keep attached screenshots out of staging so later dictation cannot attach them again.
- Preserve the simplified Tools layout and all existing profile bindings.

## Vizhi Desktop 0.16.2 — simpler ChatGPT Tools

- Remove Return to App from the default ChatGPT Tools page; leave that key blank while
  preserving Deny in the same Codex position and its approval guards.
- Keep Paste into Chat, Copy Reply, Screenshot, and the Home layout in their existing positions.
- Retain Return to App for explicitly configured capture workflows and custom profiles.

## Vizhi Desktop 0.16.1 — responsiveness and lifecycle

- Keep native desktop work off keypad callbacks and refuse overlapping gestures without queuing.
- Back off slow/background status scans, serialize animation/search timers, and keep draft/search
  rendering responsive during native operations.
- Detach event subscriptions, stop timers, invalidate transcript routes, and cancel pending model
  preparation on unload; restore each subscription once on reload.
- Add concurrency/lifecycle regression tests and an opt-in CPU/RSS observation tool.

## Vizhi Desktop 0.16.0 — Paste into Chat

- Rename Clipboard to Paste into Chat and insert copied text immediately, preserving existing
  input. Confirm Pasted only after readback; no hidden staging or automatic submission.
- Dictate and spoken workflows append instructions beneath existing text. Pin the original
  chat and draft, preserve user edits, and verify retries without duplicating uncertain writes.
- Keep profile bindings and positions unchanged; update command names and workflow checks.
- Remove temporary desktop diagnostics and routine plugin action traces.

## Unreleased — logging cleanup

- Remove temporary Copy Reply and composer diagnostics, routine keypad action traces,
  dictated-text logging, and Windows hook invocation/environment dumps.
- Keep error reporting, process timeouts, setup/migration events, and explicitly invoked
  troubleshooting tools. Subprocess logs report exit codes without dumping arguments or output.
- Preserve Copy Reply verification, voice draft delivery, keypad feedback and profiles.

## Vizhi Desktop 0.15.13 — recognize wrapped response action rows

- Recognize Rate response and its selected-feedback labels as native assistant actions.
- When accessibility wrappers prevent structural matching, require Copy plus two distinct
  response actions aligned in one compact horizontal row. Use native AXPress and verify
  the fresh clipboard acknowledgement; positions are never used to synthesize clicks.
- Reproduce reply-action-row-unrecognized in Chromium with wrapped controls and a timestamp;
  add geometry, duplicate-control, code-only, different-row and native-copy regressions.

## Vizhi Desktop 0.15.12 — diagnose remaining response control mismatch

- Distinguish a missing Copy label, Copy only before the latest answer, an unsupported
  control role, nested controls, missing response actions and an unrecognized action row.
  Preserve targeting behavior and emit only fixed operational reason codes.
- Add an isolated Chromium native-copy fixture with response Copy / Rate / Branch controls,
  extra editors and SVG icons. Controlled tests pass; owner Copy Reply remains unresolved.

## Vizhi Desktop 0.15.11 — Copy Reply does not require one composer

- Fix the owner-reported reply-composer-multiple refusal: copying a response no longer
  depends on the number of text areas in the window. Keep native response target uniqueness,
  conversation/window checks, running/dialog guards and clipboard acknowledgement.
- Add web regressions with unrelated text areas and a Copy / Rate / Branch response row,
  plus read-only replies and refusal of code-only or duplicate response Copy controls.

## Vizhi Desktop 0.15.10 — identify the Copy Reply refusal

- Replace the generic ambiguous-answer diagnostic with fixed reasons for missing/multiple
  web areas or composers, a dialog, multiple selected chats, or multiple Copy candidates.
- Preserve refusal behavior and verify each reason reaches the operational log without
  response content. This is a diagnostic release; the owner failure remains unresolved.

## Vizhi Desktop 0.15.9 — native Copy with flattened web controls

- Recognize the assistant response's native Copy button when the web accessibility tree
  omits ordinary action-row div wrappers. Require a contiguous sibling control row with
  one Copy and a known response action; text, headings and the composer break that row.
- Continue pressing the native Copy control, preserving window/conversation guards and
  requiring Copied plus fresh clipboard text. Never select response text or send Cmd+C.
- Reproduce 0.15.8's copy-control-unavailable failure in an isolated WKWebView fixture;
  add real web tests for complete replies, code-only buttons, newest user turns, activity
  and clipboard acknowledgement.

## Vizhi Desktop 0.15.8 — Copy Reply diagnostics

- Record Copy Reply key dispatch, request and confirmed result or a fixed failure reason in
  the plugin log. Include voice, busy and mode guards before the helper runs.
- Never log response text, clipboard content, source identity or arbitrary helper errors.
  Diagnostic failures do not change copying. Earlier attempts cannot be reconstructed.

## Vizhi Desktop 0.15.7 — recognize ChatGPT reply actions

- Recognize ChatGPT's More actions popup and Continue in new chat footer alongside the shared
  Fork chat from here layout. Copy Reply no longer requires the hidden branch menu item.
- Prevent a code-block Copy from borrowing a response footer across intervening answer text.
- Distinguish unrecognized reply controls from a newest user message with no answer. Keep the
  Copy Reply title on failure and show the reason once in its footer.
- Add production-selector and native fixture regressions for the actual shipped viewer layouts.

## Vizhi Desktop 0.15.6 — one-tap Copy Reply

- Copy the latest completed response in the open conversation using its native copy control,
  preserving the app's clipboard formatting and retaining plain text for Paste Reply.
- Require assistant speaker ownership and response action-row semantics; refuse older answers,
  user messages, code-only copy controls, running/approval states and ambiguous targets.
- Recheck the window, conversation and response around the press and require fresh clipboard
  content. Clear retained reply on failure so Paste Reply cannot reuse a previous answer.
- Show a dim Copy Reply with Wait or No answer, and confirm successful copying on the key.

## Vizhi Desktop 0.15.5 — distinct voice controls

- Give Voice Chat a rounded waveform inside a circle, matching Send's circle, colour and
  rendered size. Align its label and status with the adjacent Send control.
- Give Dictate a simple microphone so speech-to-draft and native Voice Chat have distinct
  symbols. Preserve recording feedback, voice shortcuts and action behaviour.

## Vizhi Desktop 0.15.4 — prompt send icon

- Replace the mail-style paper-plane Send glyph with an upward arrow in a circle, including
  its dimmed unavailable state. Retain the square Stop glyph and existing guarded Send/Stop
  behavior. Use the new glyph wherever Vizhi Desktop offers prompt submission.
- Add ready/empty/working tile previews and targeted icon regeneration.

## Vizhi Desktop 0.15.3 — match the installed sidebar semantics

- Recognize the sidebar's Working label, identified in installed app 26.915.31945's static
  interface code. The visible transcript's Thinking / Working for timer is a separate element.
- Read AXARIACurrent (aria-current="page") to identify the open conversation. Keep AXSelected
  as a legacy fallback and use the same reader for conversation status and pinned draft delivery.
- Reproduce the installed semantics in the compiled fixture: a Working status group and a
  current row with AXSelected false. Preserve exact per-row scoping and ambiguous-target guards.

## Vizhi Desktop 0.15.2 — selected conversation activity

- Apply visible task Stop/approval state to the uniquely identified, app-selected conversation
  when its sidebar badge does not report the activity. Preserve the other conversation states.
- Reject ambiguous selections and duplicate titles; preserve explicit permission badges.
  Do not infer the active conversation from recency or the last keypad press.
- Add regressions for the previously disconnected task and conversation states. This fixes
  a reproduced mapping defect; the owner's current app must still expose selection for it
  to resolve the reported Ready-during-Working case.

## Vizhi Desktop 0.15.1 — conversation status labels

- Recognize explicit Thinking and Complete sidebar badges in addition to the older Unread
  and Awaiting approval labels. Read status from named images and accessibility descriptions,
  as well as static text, before falling back to the legacy unnamed-spinner count.
- Keep matching inside each conversation row; titles and neighbouring conversations do not
  supply its status. Preserve the existing Home/Tools layout and profile selection.
- Add compiled fixture lifecycle coverage and a three-slot monitor regression. Live ChatGPT
  compatibility and physical keypad acceptance remain separate from fixture verification.

## Vizhi Desktop 0.15.0 — two-page Home redesign

- Replace the default four-page layout with Vizhi Home: Home and adaptive Tools. Preserve the
  three recent conversation keys and make native Voice Chat directly accessible on Home.
- Keep Dictate bottom-left and Send/Stop bottom-centre. Dictate remains unsent after source-backed
  insertion; Send/Stop executes the displayed verb and cannot submit on a late Stop press.
- Put Clipboard/Screenshot/Copy/Return on ChatGPT Tools, and Review Changes/Run Tests/Approve/Deny
  on Codex Tools. Preserve approval positions, expected-card checks and high-risk confirmation.
- Move configurable workflows into Saved Prompts; remove the redundant Ask ChatGPT entry inside
  the app. Keep existing profiles and external System controls. Allow region capture to start
  from the app without inventing a return destination.
- Add dispatch, mode-transition, dictation and combined-action regressions; render both modes.
  Native fixture evidence remains separate from live app and hardware acceptance.

## Vizhi Desktop 0.14.0 — source capture and reply return

- Add Use Selection, Use Clipboard and interactive Screenshot. Stage sources without sending;
  combine them with spoken task instructions into one reviewable draft. Contextual one-tap
  workflows also pause for review before submission.
- Add Copy Reply for selected answer text, Return to App, and Paste Reply into a selected empty
  field in the original app. Retain the reply independently of clipboard changes; never send email.
- Add Vizhi Flow 2's Context page, Ask ChatGPT folder, and an additive System-page installer.
  Preserve earlier profiles and the first three Flow pages.
- Preserve clipboard contents during selection fallback/image paste. Verify attachment filenames;
  refuse repeated unconfirmed image pastes and preserve source/draft recovery.
- Extend command, native fixture and hardware workflow checks. Real ChatGPT/email/browser and
  screenshot-picker acceptance remain owner-run; automatic latest-answer copying is unsupported.

## Vizhi Desktop 0.13.0 — complete workflows from the keypad

- Add Vizhi Flow: Home groups Voice Draft, Send Draft and Stop. Actions keeps approval keys
  fixed, separates native Voice Chat, and moves infrequent utilities into More.
- Spoken task briefs automatically fill Draft Reply, Rewrite, Compare, Research, Debug,
  Refactor and Review PR. The same key becomes Send Draft for explicit review and submission.
  Changed window/editor/mode/selected-chat signals refuse delivery; failed composed briefs
  remain available for tap-to-retry or hold-to-discard.
- Promote Review Changes and Run Tests in Codex. Keep Review PR and Write Tests in More.
  Migrate unchanged stock slots with a backup while preserving custom prompts and profiles.
- Show Mode's destination, clarify Type Now in Find Chat, and add honest request/sent feedback.
  Remove unsupported Copy Answer and duplicate Changes from the new pages.
- Extend production dispatch, migration, voice session and native fixture coverage. Live app
  and physical keypad acceptance remain separate from automated test results.

# Changelog

## Vizhi Desktop 0.12.13 — distinguish the empty composer prompt from a draft

- Recognize the observed **Work with ChatGPT** empty prompt when it is mirrored into the
  accessibility value and description, the cursor is at zero without a selection, and no Send
  control is enabled. Unknown prompts and contradictory state keep the existing draft guard.
- Insert into that state using verified keyboard text events, without value/selection replacement
  or clipboard access. Preserve retry, long-press discard, and separate Send Draft behavior.
- Add native fixture coverage for mirrored placeholders, exact-placeholder transcripts, and
  existing text containing the same words. Real ChatGPT confirmation remains owner-run.

## Vizhi Desktop 0.12.12 — hold to discard a retained voice draft

- Tap **Insert Draft** to retry, or hold it to discard only the retained recording. The key
  shows **HOLD TO DISCARD**, confirms **Discarded**, then returns to **Voice Draft**.
- Handle SDK press/hold/release events so a hold cannot also insert text, send it, or start a
  fresh recording on release. A newer transcript arriving during the hold is preserved.
- Existing ChatGPT input and clipboard contents are untouched by discard. No profile rebinding
  is needed. This release also packages the metadata-only insertion diagnostic helper;
  the original live non-empty-composer diagnosis remains open.

## Vizhi Desktop 0.12.11 — automatic draft completion

- A blank paragraph no longer blocks Voice Draft. Readback tolerates outer whitespace while
  preserving the original transcript and refusing different/partial existing text.
- Successful automatic insertion briefly shows **Draft Ready / SEND DRAFT**, directing the
  user to the existing keypad submission button. No manual paste step is required.
- Added a native rich-editor AX fixture case and delivery-feedback regressions. The owner's
  specific live Clear Composer report still requires confirmation of the visible input contents.

## Vizhi Desktop 0.12.10 — keypad voice draft insertion

- Voice Draft tries verified text events when the app refuses accessibility setters; it never
  reads or replaces the clipboard. Existing/partial drafts and changed targets block further input.
- Failed transcripts stay in memory. The same key becomes **Insert Draft** for a retry;
  **Send Draft** submits the reviewed composer with a separate press. Exact already-inserted
  transcripts are recognized on retry, preventing duplicates. Existing profile bindings work.
- Added controlled native fixture checks for rejected setters, Unicode insertion, retry, explicit
  send, partial writes and window changes. Live ChatGPT/keypad acceptance remains owner-run.

All notable changes to Claude Console are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/); this project uses [SemVer](https://semver.org/).

## [0.12.9] — Vizhi Desktop — 2026-09-19

- Speak Query on macOS uses a stronger local Whisper Turbo model, downloaded and verified
  separately from the plugin on load. Keypad and Options+ show progress, with tap-to-retry on
  failure. No recording starts until the model is ready; Type Query remains available.
- Silent/invalid search recordings no longer deliver invented text. Search transcript success
  logs contain character counts instead of the query.
- Added reproducible fixed-audio model comparisons and download/recovery tests. See
  `docs/vizhi-desktop-speak-query-2026-09-19.md` for results and owner acceptance limits.

## [0.12.8] — Vizhi Desktop — 2026-09-18

- Keep a verified search usable when its modal hides the mode selector. Bind mode evidence to
  the process, window and search field; reject visible mode conflicts and changed targets.
- Recognize mode descriptions on popup controls and directly labelled pressable groups. An
  unreadable initial mode cannot open search or issue a recovery token.
- Reproduce the previous Mode unreadable failure in the native fixture and add modal lifetime,
  missing initial mode, mode/window changes and delayed modal recovery checks. Real ChatGPT
  compatibility still requires the owner's retry; fixture results do not establish it.

## [0.12.7] — Vizhi Desktop — 2026-09-18

- Recognize search fields by exact placeholder labels, semantic search roles and search sheets
  containing web content. Accept mode popup controls and their labels without treating message
  text as a mode selector.
- Let an explicit Find Chat request bring its selected app window forward. Background polling
  and delayed voice writes remain unable to focus or retarget an application.
- Recover from a late search field using a read-only probe pinned to the original window. Never
  repeatedly click Search while polling or toggle an already-open unsupported search panel.
- Show a specific failure and a correctly labelled Open Search retry key. Revoke pending voice
  transcripts on explicit retry, even when the app reuses its original search field.
- Add search recovery/diagnostic tests and native fixture variants. These address implementation
  defects; the owner's exact live-app failure is not yet confirmed resolved.

## [0.12.6] — Vizhi Desktop — 2026-09-18

- Add a Find Chat keypad page with Speak Query, Type Query, query status and conversation cards.
  Search dictation has a separate capture-start sink and can never fall back to the message composer.
- Select exact result links, pin the search window/field and verify the expected query. Reject
  stale results and cancelled captures; preserve paging when results have not changed.
- Update the same home position in both profiles; Codex still dispatches Changes with one press.
- Add controlled native search fixture checks and session/dispatch tests to the command harness.
  Live ChatGPT search compatibility and microphone/keypad acceptance remain owner checks. Unknown
  accessibility layouts show Use App; Windows search has no implementation yet.

## [0.12.5] — Vizhi Desktop — 2026-09-18

- Measure conversation titles in the actual font and draw each line once. Keep a consistent text
  size and at most three lines; avoid the SDK's second wrap and dangling final-word fragments.
- Use a thinner, darker Ready footer; preserve explicit approval, thinking and completion states.
- Add long-title and Unicode layout regressions plus real-render pixel checks. Home and All Chats
  share the renderer; display aliases do not change conversation names or navigation targets.

## [0.12.4] — Vizhi Desktop — 2026-09-18

- Render All Chats entries through the same full-key conversation widgets as Home, removing the
  duplicated SDK caption while preserving exact-title selection, Back, and automatic pagination.
- Balance the All Chats outline and distinguish Quick Chat, Search, New Chat, Scheduled,
  Attach Files, Rewrite, Plan, Send Draft, and Continue with semantic desktop glyphs.
- Keep control names visible when unavailable, with dim glyphs and separate status text. Report
  unsupported Copy Answer explicitly; distinguish a busy task or pending approval from no draft.
- Refresh both profile previews while preserving their IDs, bindings and custom workflow behavior.
- Add automated command/fixture coverage, an owner-driven hardware checklist, and a standalone
  renderer for the built plugin's key faces. Keep the shortcut helper alive long enough to deliver
  targeted events, as verified against the controlled fixture app. Live ChatGPT and hardware checks
  remain separate from software verification.

## [0.12.3] — Vizhi Desktop — 2026-09-17

- Add an explicitly configured native Voice Chat shortcut. The user's app settings screenshot
  confirms **Control–Shift–V** is **Toggle voice chat — Start or stop voice chat**.
- With the shortcut configured, the home key stays **Voice Chat / TOGGLE**, including when AX
  voice controls or the web tree are unavailable. Post one chord only to the already-frontmost
  ChatGPT process; do not activate another app or fall back to a second toggle method.
- Keep key feedback honest: **Requested** means events were posted; the app owns start/stop state.
  Refuse while local dictation is in progress and suppress repeat requests within 1.2 seconds.
- Preserve all profile identities/bindings and the Voice Draft clipboard recovery path. The
  shortcut is local configuration, not a presumed default for every ChatGPT installation.

## [0.12.2] — Vizhi Desktop — 2026-09-17

- Recover a Voice Draft that the composer refuses by copying its transcript to the macOS clipboard.
  The key shows **Paste Draft / CMD+V** until the next attempt. Review the composer before pasting:
  an unconfirmed write may have partially landed. Recovery does not send text or simulate a paste.
- Show **Transcribing / WAIT** after the second press and **PRESS TO STOP** while recording.
  Keep Voice Draft failure messages visible until the next press.
- Preserve existing profile identities and bindings. Native Voice recognition still reports
  **No Voice** on the user's app after 0.12.1; direct draft insertion is also unresolved there.
  This release adds draft recovery, not a verified repair of the app's Accessibility integration.

## [0.12.1] — Vizhi Desktop — 2026-09-17

- Match complete button labels independent of capitalization and whitespace. The user-reported
  **Start Voice Chat** tooltip differs from the documentation's sentence case.
- Read button title, description, help, and label together so a short icon title or AX value
  cannot hide the action name. Continue refusing generic Close/× controls and ambiguous matches.
- Preserve the Adaptive 3 and Everyday profiles without changing their identities or bindings.

## [0.12.0] — Vizhi Desktop — 2026-09-17

- Add native **Voice Chat / End Voice**, driven by exact observed app buttons. Unsupported or
  ambiguous controls remain unavailable; live native-voice validation is still pending.
- Add **Vizhi Adaptive 3**, with native Voice at home bottom-right. Keep Voice Draft and Send on
  Controls, preserve all nine workflows, and retain older profiles and the user's selected default.
- Keep offline **Dictate & Send** as an optional action. Coordinate native and local voice key
  requests to avoid overlapping capture when the target window's native session is observable.
- Match task **Stop** exactly so it cannot press **Stop voice chat** or report voice as task activity.
- Preserve the Everyday profile's **Voice Draft / Send / Stop** row.

## [2.2.3] — 2026-09-16

Claude Console and Vizhi for Codex compile the same engine, so the work done for Vizhi 1.6.1
reaches this product too. Nothing here is a new feature; these are the shared changes a 2.2.2
rebuild would otherwise have picked up unannounced. The keypad's faces, colours and key names
are deliberately unchanged from 2.2.2 — each product now declares its own identity, so Vizhi's
blue and its relabelled keys stay with Vizhi.

### Fixed
- **Go to Project handles a path containing an apostrophe.** The shell command was spliced into
  AppleScript with a broken single-quote escape, so a folder like `Ravi's Apps` produced a
  malformed command. The command is now passed as an argument instead of being interpolated.
- **A reused terminal tab no longer inherits the previous session's project name.** macOS hands
  out `ttys000` again as tabs close and open, and a session with no name of its own yet could
  show the last occupant's.
- **The Context key shows a dash until the session reports.** It previously read `0%` before the
  first status line, which is indistinguishable from a genuinely empty context window and is the
  kind of invented value the key is supposed to refuse.
- **The release scripts fail on a bad signature instead of reporting success.** `spctl` and
  `stapler` were tolerated with `|| true`, so a rejected helper could still reach the green
  success line (#66). The packaged helper is now extracted and re-verified after packing.

### Changed
- **An empty `prompts.json` array now removes every prompt key**, rather than reseeding the
  twelve defaults. Writing `[]` meant "no keys" and got the opposite; there was previously no way
  to express removal at all. Delete the file to restore the defaults. Logged when it happens,
  since keys vanishing otherwise reads as a broken plugin.
- **Windows session discovery matches an interpreter's arguments, not its install path.** A
  bundled `node.exe` living under an agent's own directory could otherwise be mistaken for a
  session of that agent.
- **Log lines carry the product name**, so two consoles writing to one log can be told apart.
- The voice helper's microphone permission string no longer names a single product. It reaches
  an installed helper only when the helper is re-signed.

## Vizhi for Codex [1.6.1] — 2026-09-15

### Known issues
- **The screenshot picker switches the keypad to its default profile** and hides Vizhi's Escape
  key while the overlay is open. Pressing Escape on the keyboard dismisses the picker and
  restores the Vizhi profile. Accepted for this release rather than fixed.

### Fixed
- **A rollout lifecycle edge could discard a live approval.** The hook owns `PermissionRequest`
  and can write it between two polls; a `task_started` read afterwards then overwrote it with
  Busy, turning the amber key grey with nothing left to re-emit it. A plugin reload during a
  pending approval hit this every time. Only a terminal edge, a newer approval, or the code-mode
  output that resolves the approval may now clear a waiting session.
- **Updating no longer reports working hooks as untrusted.** Hook trust was proved by a
  `transport` field that the launcher had only just started writing, so every envelope written by
  an earlier version read as untrusted — permanently if the launcher rewrite failed. The launcher
  no longer carries the tag, and the reader infers it from absence, since the rollout fallback is
  the only writer that stamps one.

### Changed
- **Voice now shows `Setting up` during its first packaged-runtime check.** The check and any required
  Whisper bundle copy run off the keypad thread; recording starts only after setup finishes, and
  extra voice-key presses during setup are refused instead of stopping a recorder that is not ready.
- **Screenshot now carries its own Windows runtime.** The capture helper no longer assumes that
  Options+'s private .NET runtime is globally discoverable, so it can open the snipping overlay on
  machines without a separate .NET Desktop installation.
- **Each product now declares its own identity.** The keypad's identity colours, icon set and
  failure-word hold are named by the plugin rather than chosen inside the shared engine, so the
  two products can look different without either one moving the other's keys.

## [2.2.2] — 2026-09-13

Answers to Logitech QA's retest of 2.2.1 — macOS (8 September; #71–#73) and Windows (#74–#80) —
plus what three Windows device passes and the review of those fixes turned up on the way.
Verified on the keypad on macOS (12 September) and Windows (10–12 September); run sheets in
`docs/windows-qa-2.2.1.md` and `docs/windows-qa-2.2.2.md`. The package is 16.5 MiB, down from
21.3 MiB.

### From the device passes and the review (10–12 September)
- Reduce package size by sharing one self-contained Windows runtime across typing, tab focus,
  voice capture, and screenshots. The hook retains its separate executable and watchdog.
  The speech model continues to download on first use.

- Known issue ([#88](https://github.com/rshankras/claude-console/issues/88), P3): Windows
  session switching can fail for manually renamed Terminal tabs. Clear the custom tab name
  to restore automatic titles. Failed focus leaves session routing unchanged.
- Windows status and activity hooks use an encoded PowerShell launcher that survives Git Bash
  argument conversion. Existing owned commands are upgraded while preserving unrelated settings.
- Settings edits and macOS uninstall cleanup retain the original text of untouched values,
  including inline foreign hooks, comments, spacing, line endings, and UTF-8 BOMs. Removing
  plugin hooks no longer reformats the user's whole settings file.
- Windows session focus refuses an ambiguous title when identity verification fails, instead
  of selecting the first matching tab and reporting success.
- Windows voice keys show **Starting** until the microphone is ready. A second press during
  startup cancels and stops the helper before another capture can start. The recording face
  appears only after the helper acknowledges microphone readiness.

- The Model key now keeps the same brain icon for every model. Model-specific colors and their
  live-state repaint subscriptions were removed; pressing the key still opens the model picker.
- Windows creates the plugin's runtime home (`~/.claude/claude-console/`) on load, as macOS
  already did, so turning live status on with your own status line in place always records the
  chain file that restores it when you turn live status off. Found by running the C# suite on
  Windows, which is now green there as well as on macOS.
- **Windows: New Claude, New Claude (Window) and New Tab start in your home folder** (#85).
  Windows Terminal's default profile has no starting directory, so a tab the plugin opened
  inherited the plugin service's own folder under Program Files; the keypad then named the
  session "LogiPluginService". The plugin now passes your home directory, which is what the
  terminal uses when you open a tab yourself. Go to Project was unaffected — it always named
  the project's folder.
- **Yes/No find the one session with a prompt up even when another session sits idle.** With
  nothing pinned, the answer keys fall back to the single session waiting on you. A session idle
  at its prompt for a minute counts as waiting too, so one prompt plus one idle session left the
  keys with no target — on Windows, which has no frontmost tab to break the tie, that was any
  second session (Logitech's "Mode B"). A pending approval now outranks an idle prompt; two
  pending approvals still refuse to guess.
- **Windows says "Turned on", not "Restart Claude", after live status is switched on** (#58).
  Running sessions pick the new hooks and status line up by themselves on Windows exactly as on
  macOS — measured on two days on sessions started hours earlier, including the approval hook. The
  restart wording had been kept on the strength of QA's 2.2.0 report, which was #74 in disguise.
- **Windows: a session survives a Claude Code auto-update.** Claude Code updates itself in place
  while sessions run; Windows cannot overwrite a running program, so the updater renames it
  (`claude.exe.old.<stamp>`) and the running session carries that name from then on. The hook
  exe matched the name `claude` only, so from the update onward every hook in an already-running
  session wrote only the shared fallback: Cost/Context showed the last writer's numbers, no
  approval ever attached to the session, and Yes/No answered "no pending approval" on a session
  the plugin had pinned and named. Found on 2026-09-11 on a session up since the evening before,
  with the 06:58 auto-update as the cut-over. The hook and the plugin's discovery now apply one
  rule for a renamed running image; `claude-console-hook selftest` prints the ancestry walk hop
  by hop so this class of miss is visible in the field.

### Retest review follow-up
- Windows screenshot and tab-focus helpers no longer need the .NET Desktop Runtime at all, so
  they start on clean installations without a separate runtime download. The screenshot helper
  reads the clipboard through Win32 and takes the snipping overlay's own PNG; the tab-focus
  helper drives UI Automation through COM. Both are now self-contained and trimmed like the
  other helpers, about 12 MB each instead of 68 MB. The executable-only staging step rejects
  publish output that leaves runtime dependencies out.
- Go to Project preserves carrier words that belong to a project name: "go to open source kit"
  prefers `open-source-kit` over `source-kit`. Equally good folders produce No match instead of
  depending on discovery order.
- Windows destructive-command warnings recognize every `-Recurse` and unambiguous `-Force`
  abbreviation, including `-Recu` and `-Recurs`.
- macOS cleanup serializes settings edits and recovery-data removal across processes, uses a
  unique temporary file, and retains recovery data after errors or lock contention. Hooks only
  record successful uninstall cleanup and retry failed attempts.

### Fixed — Windows
- **Yes/No answer again** (#74, Windows retest item 2 — every press was discarded as "no pending
  approval on (no target)"). The Windows hook exe wrote the PermissionRequest hook's argv verb,
  `permission`, as the session's state; the plugin only ever recognises `waiting`, which is the word
  the bash hook translates to on macOS. So on Windows no session ever counted as waiting: the
  pending payload sat correctly on disk beside it and was never read (a pinned session "yields no
  pending approval"), and the routing fallback "exactly one session waiting" could never fire (an
  un-pinned press found "no target"). Discovery, pinning and the status data all worked, which is
  why it looked like a routing defect. The exe now translates like the bash hook and the plugin
  normalises the word whichever hook wrote it; the slot key reads **Allow?** while a menu is up
  instead of **Complete**. Nothing had asserted the word; `ActivityWordTests` does now.
- **Voice refusals show their reason on the key** (#76, item 6). The three Windows refusals in
  `StartVoiceCapture` — no helper exe, no `whisper-cli.exe`, speech model not ready — were a log
  line and a beep; macOS already put **Model loading** on the key. They now go through the same
  path: **No helper**, **No whisper**, **Model loading**.

### Fixed
- **A dictation that cannot be typed says so** (#75, item 6). The transcript path discarded the
  platform's injection outcome, so a dictation with no target session — nothing pinned, no single
  obvious session — was transcribed and dropped with a WARN line, indistinguishable on the device
  from one that landed (QA: three delivered while pinned, four of five lost after un-pinning). The
  pressed key now reads **No target**, or **Not typed** when the session was known but the
  keystrokes did not land, and the log keeps the words. `VoiceDeliveryTests`.
- **The speech model download is announced** (#76). One Options+ card when the 142 MB one-time
  download starts (the key says Model loading until it finishes; press again afterwards), one when
  it is ready, one when it fails with the reason. Before, the first voice press on a fresh install
  started a multi-minute download with a log line as the only notice, and a failed download
  silently restarted on the next press.
- **Go to Project matches names that contain carrier words, and says when it matched nothing**
  (#77, item 8). Carrier words ("go to", "open the … project") are now dropped whole-word off the
  edges of the phrase only; folder names are never stripped. The old blind substring `Replace` ran
  over both — "the" ate the middle of `theme`, "open" the front of `openai`, and "claude" was cut
  out of every `claude-*` folder, so a project called `claude` could not be reached at all. Both
  readings of the phrase are scored and the best wins, so "go to project claude code" reaches
  `claude-code` over `vscode-ext`. No match now reads **No match** on the key, the log prints the
  phrase as compared (QA read the raw phrase in the old line as proof nothing was stripped), and
  the first miss per load posts an Options+ card naming the candidates' source and the
  `project-roots` file, which until now was named only in that log line.

### Fixed — macOS
- **A settings.json write hands the file back the way it was found** (#72, retest finding B).
  The plugin parses the whole document and serialises it again, and the default writer made that
  visible: every quote, ampersand, apostrophe, angle bracket and non-ASCII character came back as
  a `\uXXXX` escape and the trailing newline was gone — valid JSON, functionally identical, and a
  whole-file diff for anyone who keeps `~/.claude` in git, applied to entries the plugin does not
  own (on QA's machine, another plugin's ten hooks), on every write including the on-load
  migration 2.2.1 added. Reproduced against the shipped 2.2.1: one write turned an em dash inside
  the user's own permission description into `—`. The writer now uses the relaxed encoder,
  and reads the indentation, line ending, trailing newline and byte-order mark off the file and
  writes them back as found; a file that does not exist yet gets Claude Code's own shape. The
  cleanup script's `--unwire` had the same defect for non-ASCII text and is fixed the same way.
- **The hooks take themselves out after an Options+ uninstall** (#73, retest finding C; #55).
  Options+ removes the plugin folder and nothing else — the SDK gives a plugin no uninstall
  moment — so the five hooks and the status line kept running against a plugin that was gone,
  recording every prompt and permission request with nothing left to read them, at three
  status-line runs a second, and 2.2.1's guard only helped once the runtime folder was deleted
  too. The hooks can see what the plugin cannot. On every load the plugin now records where it is
  installed (the package folder under the service's Plugins directory, or the dev `.link`) in
  `~/.claude/claude-console/plugin-home`; a hook that finds that place missing records nothing,
  and once it has been missing for over a minute across two runs it runs the surgical unwire
  itself (rolling backup, your own entries untouched, the Off marker set) and leaves an
  `unwired-after-uninstall` breadcrumb. One miss is not enough on purpose: an Options+ update
  replaces the folder for a few seconds, and the service restarts on its own — QA's suggested
  unwire-on-Unload would have switched live status off on every one of those. The runtime home
  (voice helper, speech model) is left for `uninstall.sh`, which the plugin still installs.
  macOS only for now; the Windows shim keeps the 2.2.1 behaviour (#55).

### Changed
- **The card button link works again** (#71, retest finding A; #68). The repository it points
  at is public again as of 9 September; nothing in the package changed.
- **Every user-facing link points at vizhi.dev, and the licence is the EULA.** The three card
  buttons baked into the plugin (how to undo the settings edit, the Windows Terminal rule, voice),
  both packages' homepage, support and licence fields, and the README's download and support
  links now open vizhi.dev — its product pages and FAQ carry the same content the README sections
  did. Nothing a user can reach points at the source repository any more: it went private on
  2 September and every card button was a 404 for a week (#68, #71), and it will be closed for
  good. With that, the MIT licence file is gone and the packages declare the
  [EULA](https://vizhi.dev/eula/) as their licence; whisper.cpp and the Whisper model stay MIT and
  ship their licence texts. The vizhi.dev anchors the plugin uses are frozen — a rename needs a
  redirect first — and `CC_CHECK_LINKS=1` makes the test suite fetch them.
- **The Windows Terminal notice no longer claims Yes/No and voice "still work here"** (#61,
  Windows retest item 16). QA caught it mid-way through a run in which neither did. It now says
  they do not need Windows Terminal but do need a target session, and to pin a session slot if a
  press is refused.

## [2.2.1] — 2026-09-04

Answers to Logitech QA's retest of 2.2.0 (1 September).

### Fixed
- **The voice helper is replaced when the package carries a newer one** (#59, retest bug D). It
  never was: macOS does not let another app write inside a signed app bundle that has been
  launched and granted a permission — the system said so ("LogiPluginService was prevented from
  modifying apps on your Mac") while the plugin logged "installing voice helper" and moved on. The
  install now renames the old bundle aside and creates the new one, which macOS allows; a copy that
  fails puts the old bundle back; failure to remove quarantine does the same; and the install path
  reports the exit code and error of every tool it runs instead of logging success unconditionally.

- **A leftover hook no longer errors on every turn** (#55, retest bug B). An Options+ uninstall
  removes the plugin and nothing else — the SDK gives a plugin no uninstall moment — so the five
  hooks and the status line stay in `~/.claude/settings.json` pointing into
  `~/.claude/claude-console/`, on macOS as on Windows. A user who then deleted that folder got
  "Stop hook error occurred" on every turn. On both platforms each command now checks that its
  script or exe exists before running it, so a leftover entry is a silent no-op. On load, commands
  already recognisably ours are migrated from the 2.2.0 form through the normal rolling-backup
  writer; no wiring is added and user commands are untouched. The Turn on dialog and README still
  explain how to remove the wiring before uninstalling because Options+ cannot clean it up.
- **The shipped PDB no longer names the build machine** (#62). The 2.2.0 symbol file carried the
  author's worktree path twice — in the document paths of the engine's sources, which sit outside
  the folder the Release PathMap covered, and in a Source Link map pointing at a repository that
  is now private. Every source root is now mapped to `/_/`, Source Link is off, and the release
  script's leak check reads the PDB as well as the DLL.
- **Both whisper bundles are packed the same way** (#64). 2.2.0 stripped the transcription smoke
  marker from the Windows bundle but shipped the macOS one, which QA read as the smoke test having
  run for Mac only. Neither marker ships now, and the release script prints when each bundle last
  transcribed instead. The runtime comparison only looks at packaged files, so a marker left in
  the runtime home by an earlier install changes nothing.
- **The service no longer warns about `PluginConfiguration.xml`** (#63). The SDK looks for a static
  plugin declaration embedded in every plugin and logged two WARN lines per load when it found
  none. A first fix embedded the required shape but left `<actions>` empty; a fresh packaged install
  proved the parser merely replaced the old warnings with two `Action tags not found` warnings.
  Every real action is dynamic, so each product now declares one uniquely named compatibility
  command with `deviceType="0"` (None). It satisfies the legacy parser but is unavailable on every
  real device, is absent from the layout, and cannot collide with a runtime action. The 2.2.0 retest
  response claimed a load with zero WARN lines; that was wrong, and QA was right to say so.
- **Release builds cannot reuse a resource-less intermediate DLL.** The packer now clears both
  `bin/Release` and `obj/Release`, then refuses the artifact unless the actual DLL being packed
  contains `PluginConfiguration.xml`, a common icon, and the product-specific bridge script. This
  closes the incremental-build path that could silently undo #63 and omit every embedded asset.

### Fixed — Windows (code change; not yet run on Windows hardware)
- **Hook processes can no longer pile up** (#57, retest bug A). QA found around fifteen
  `claude-console-hook` processes left behind after one session following a reboot, and the
  machine froze until the plugin service was stopped. The hook now ends itself after eight
  seconds whatever it is blocked on; the watchdog is armed before even diagnostic file I/O and
  fails closed if its thread cannot start. It bounds every read of its input, bounds the PowerShell
  lookup it falls back to when the kernel cannot name a parent process (the old code read that
  output before applying its time limit, so a slow PowerShell start after boot held every hook
  open), refuses to run above eight concurrent copies (below QA's approximately fifteen-process
  freeze), and kills a timed-out chained status-line command with its descendants. The watchdog
  deliberately performs no synchronous logging before exit. Built here, contract-tested and
  cross-published, awaiting a Windows retest.

### Changed
- **Yes / No say when they cannot work** (#58, retest item 2). The answer keys see a permission
  prompt only through the `PermissionRequest` hook, which is part of the opt-in live-status wiring;
  with it off they looked ready, beeped, and did nothing at a real prompt. They now read
  **Set up** / **Off** on a grey tile, keeping the check and cross, and the first press posts a
  card in Options+ naming the key that turns live status on. Every session key's state bar reads
  **Set up** / **Status off** while the wiring is off, instead of a frozen **Complete** or
  **Waiting** — nothing can update a session's state without the hooks, so nothing the bar could
  say would be current.
- **Both answer keys show a pending approval, and a No press clears it** (#60, retest bug C). An approval clears itself when
  the tool runs and the next hook fires; a rejection fires no hook, so the Yes dot and the
  session's **Allow?** bar stayed lit until that session's next prompt. The answer key now clears
  the captured payload itself the moment its keystroke lands, and leaves it — and says so — if the
  keystroke did not. Both Yes and No now show the amber pending cue; for a destructive request Yes
  turns red while No remains amber because rejecting does not authorize the command.
- **"Restart Claude" only where a restart is needed.** On macOS a running Claude Code session
  picks the new hooks and status line up by itself (measured 2026-09-02 on Claude Code 2.1.258:
  status line one second after *Turn on*, the approval hook three minutes later, no restart), so
  the face after *Turn on* reads **Turned on** and the Options+ card no longer sends you to start a
  new session. Windows keeps the restart wording: QA saw the keys stay inert there until Claude Code
  was restarted, and the cause is not yet known.

## [2.2.0] — 2026-08-30

The Logitech QA retest release. Twenty-five findings were filed against 2.0.1; this release
answers the ones that were ours to answer, changes one thing about what the plugin *is* — and
carries the keypad design Logitech's designers drew for it.

### Added
- **The Logitech design.** Every action icon is now the designer's glyph in one copper monochrome
  (Claude's identity colour); colour is reserved for state. **Session keys** are a full-surface
  face: the project name on black with a state bar along the bottom — *Thinking* / *Waiting* /
  *Allow?* / *Complete* — highlighted for the pinned session, grey for the rest. **Yes / No** are
  full green / red tiles with a circled check / × and the label inside; the approval badge stays on
  Yes only. The **Voice** key is now called **Dictate**. The importable layouts (`ClaudeConsole-
  Keypad.lp5`, `-Windows.lp5`) carry the approved first page — Session 1·2·3 / Clear · No · Yes /
  Esc · Tab · Dictate — with the approval keys deliberately off the Esc slot.
- **Screenshot key** (Core) — capture a region with the system picker and hand the image to the
  *current* conversation, unsent, so you add the question. It has been in the plugin since 2.1.0
  and undocumented until now; first use asks for Screen Recording.

### Changed
- **Live status is opt-in.** The plugin no longer edits `~/.claude/settings.json` on load. The
  Cost / Context / Activity keys read *Set up* until you turn them on — by pressing one of them:
  the first press flashes *Press again*, posts a card in Options+ and, on macOS, opens a dialog
  mid-screen stating the exact change (five hooks and a status line, your entries kept, the file
  backed up first) with *Not now* / *Turn on* — it edits nothing; *Turn on*, or a second press
  within 15 s, does it. To turn it off, hold a live key: the mirror
  dialog (*Keep* / *Turn off*), or a second long press within 15 s, takes exactly those entries
  back out and the keys read *Off*. Nothing to drag: the keys are the switch. Existing installs are already wired and simply load as
  enabled. Logitech QA asked for a prompt before user config is modified, and the SDK has neither
  a dialog nor a plugin settings page (#31), so the press is the prompt.
- **The plugin is universal.** No application binding, no packaged profile (`HasNoApplication`),
  at Logitech's request. The keypad layout is now a download — `ClaudeConsole-Keypad.lp5` (and
  `-Windows.lp5`) from the release — imported once onto Terminal's own Options+ entry. This removes
  the self-registration and repair machinery that produced the "icon vanished after reinstall" and
  "nothing appeared after first install" failures outright, and lets Claude Console and Vizhi for
  Codex be installed together.
- **A Marketplace install no longer forces a service restart** (P0). The heal that did so fired on
  every healthy install because the installer writes the registration before the payload.
- **Text injection works on non-US keyboard layouts** (P0): typing goes by key code, not character.
- **The No key rejects** (P0): both answer keys used to type text into the approval menu.
- **The session pin releases**: display keys follow your eyes, answer keys follow the pin, and
  pressing the pinned session again lets go.
- **A busy turn that was interrupted no longer shows an hourglass forever**: the plugin watches
  the transcript, not just the hooks — and a keypad Esc clears it in seconds.
- **Subprocess timeouts say how often and how long**, and back off instead of retrying a stalled
  machine every tick.
- **Sessions in a terminal the keys cannot drive no longer take a Session key.** Only Terminal.app
  sessions are shown; one in iTerm2, cmux or an editor's terminal used to occupy a slot whose press
  did nothing, and six of them could crowd out every real session. Each skipped session is named
  once in the plugin log.

### Fixed
- **Voice works from a package install.** 2.0.1 shipped a whisper bundle with no compute backends;
  it aborted on every machine without Homebrew while passing every check on the build machine. The
  bundle now carries them, the release script refuses one that has not transcribed, and a broken
  bundle already on disk is replaced on the next update.
- **A failed dictation says so**: a denied microphone, a silent room, a missing model or helper each
  put two words on the key — "Mic denied", "No speech", … — with a beep, instead of quietly typing
  nothing. Whisper's noise labels ("(gunshot)") are no longer treated as speech and matched to a
  project name.
- **Go to Project finds projects anywhere**, not only under `~/Work`; the release binary no longer
  embeds the build machine's paths.
- Idle sessions no longer borrow another session's cost or wear the approval badge; an idle session
  keeps its name and its state file.
- The key-redraw storm (~11/s) is gone; redraws happen when something changed.
- The icon converter wrote to a directory that no longer existed and reported success.
- `uninstall.sh` ships with the plugin and lives outside the package, so it is there after an
  uninstall; the README's recovery instructions name the installed paths.

### Fixed — Windows (verified on the MX keypad, 2026-08-30)
- **Voice ships.** No package had ever carried the Windows whisper bundle, so packaged voice could
  not work (#47). The release script now refuses to pack without a bundle that has transcribed on
  Windows.
- **No and Esc reach Claude Code.** The inject helper sent every named key without its character,
  and Node's console reader identifies Escape by exactly that — so Yes answered approvals while No
  and Esc did nothing, and the log claimed otherwise. Windows' own version of the No-key defect.
- **A navigation press with no Windows Terminal window is refused, with the reason** (#33): it
  beeps and posts a "Windows Terminal required" card once per load instead of issuing a `wt`
  command that quietly acts on nothing — or on whatever window happens to exist.
- Known, not yet fixed: an Options+ uninstall on Windows leaves the live-status hooks in
  `settings.json` and there is no cleanup script there yet (#55) — turn live status off before
  uninstalling.

## [2.1.0] — 2026-08-21

One repository, two products. The engine (`src/Core`), the agents (`src/Agents`) and the thin
products (`src/Products`) were separated so that Claude Console and **Vizhi for Codex** build from
the same core with one adapter each. Codex on Windows gained a hookless state transport (the
rollout transcript), a working Screenshot key, per-session keys, and focus by tab identity.
No user-facing change for Claude Console on macOS.

## [2.0.1] — 2026-08-13

### Changed
- **`PluginApi.dll` is no longer bundled in the package** (−15 MB). Logitech Marketplace QA
  flagged it: the assembly belongs to the Logi Plugin Service runtime and is provided by the
  host at load time, so shipping a copy risks a version conflict. The project reference now
  sets `<Private>false</Private>`; behavior is unchanged (the plugin always loaded the host's
  copy — the bundled one was dead weight).

## [2.0.0] — 2026-08-08

**The Windows release.** One package now serves macOS and Windows — same keys, same live
displays, same offline voice, on the same MX Creative Keypad, whichever machine it's plugged
into. This rolls up the 1.8.x internal builds below; the detailed entries stay for the record.

### Highlights
- **Windows support** (see [1.8.4]): every key group works on both platforms, with typing that
  addresses the console handle — a keypress reaches the intended Claude session or nothing at
  all. Windows Terminal is the supported host; the layout imports via
  `ClaudeConsole-Windows.lp5`.
- **First installs work on a clean machine — the plugin registers itself.** The release gate
  (a fresh macOS account) caught that a sideloaded `.lplug4` install never creates the
  application entry at all: no Claude Console icon in Options+, no keypad layout. Only
  Marketplace installs get that step; every dev machine had been coasting on entries created
  before packaging. The plugin now writes the registration itself at load when it's missing —
  ApplicationInfo, icon, and the packaged layout, the exact files the service adopts at
  startup — then restarts the service once. **What you'll see on a first install: Options+
  blinks once (closes and reopens by itself) within about a minute — that's the registration
  landing, not an error; don't relaunch Options+ mid-blink.** The restart exists because the
  service only reads registrations at startup; an install from the Logitech Marketplace
  registers natively, and on those the plugin finds the entry and skips the restart entirely.
  This also upgrades the Windows reinstall story: its uninstall deletes the entry outright,
  which used to mean a manual `.lp5` re-import; now the default layout rebuilds unaided.
- **Reinstalls and upgrades self-heal** (see [1.8.9]–[1.8.13]): the service silently drops the
  application registration on every reinstall on both platforms; the plugin now detects the
  install and restarts the service once so the Claude Console entry survives. Options+ blinks
  once — that's the heal.
- **Profile polish** (see [1.8.8], [1.8.12]): session keys on the top row everywhere including
  the preview, the actions sidebar opens on Claude Console's actions, the profile carries a
  proper package identity, and the author reads S.Ravi Shankar.

## [1.8.14] — 2026-08-08

### Changed
- **Key backgrounds are now pure black.** Runtime-painted keys used `#0D1117` (a dark
  blue-grey) while the profile's stored icon tiles and the Options+ editor background are pure
  `#000000`, so the two read as mismatched blacks side by side. Everything now agrees on
  `#000000`, which also blends into the hardware bezel as closely as a backlit LCD allows.

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
