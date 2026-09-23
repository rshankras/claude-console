# Vizhi Desktop command UX review

Reviewed 2026-09-21 against installed Vizhi Desktop 0.16.3, the selected **Vizhi Home**
profile (`A8B982E4103C4F99A4C75070AF60A6E4`), and source on
`integrate/vizhi-desktop-main`. The installed ChatGPT and Codex workflow definitions
have the same effective behavior as the packaged examples, including omitted Submit
values that default to true.

This is a source and installed-configuration review. It does not assert new live
ChatGPT, Codex, or physical-keypad test results. Recommendations are not installed.
No plugin behavior, profile, or user configuration was changed for this review.

Implementation follow-up: [0.17.0 plan and verification](vizhi-desktop-workflows-implementation-2026-09-21.md).
The statements above describe the original review, before implementation.

## What counts as useful

A keypad action earns its position when it shortens a recurring task, makes its
target understandable, and gives useful feedback. Consider the whole path: choose
context, express the request, submit, follow progress, and use the result. Opening
an app panel can be useful, but alone does not establish a completed workflow.

Keep these qualities across beginner, intermediate, and advanced use:

- Beginners can predict a key's action without knowing the plugin's internal state.
- Intermediate users can combine capture, instructions, and output without workarounds.
- Advanced users retain stable positions and direct actions for repeated, well-defined work.
- Viewing a document, choosing a screen region, and reviewing a diff on the Mac are
  legitimate parts of the task. Avoid making the keypad a miniature Finder or code editor.
- Avoid blanket extra confirmations, automatic microphone activation, and automatic
  submission of newly captured material. Keep explicit selection and existing Send.

## Evidence already supplied by the owner

| Area | Evidence and remaining limits |
|---|---|
| Dictate | Owner confirmed automatic insertion worked. Preserve append, retry, and hold-to-discard behavior. |
| Paste into Chat | Owner confirmed immediate text insertion worked. File/image clipboard input is a separate gap. |
| Copy Reply | Owner confirmed it finally copied the response without selection. Preserve that behavior. |
| Screenshot | Owner said the immediate-attachment flow looked good. Preserve it. |
| Native Voice Chat | Owner explicitly distinguished working Voice Chat from problematic Speak Query. Keep the two routes distinct. |
| Conversation status | Owner confirmed Thinking became visible. That is not confirmation of every state in every mode. |
| Responsiveness | Owner confirmed smooth typing and a working keypad after restart. This does not establish long-duration leak freedom. |
| Other workflows | Earlier testing and discussion do not establish complete hardware acceptance. In particular, search recovery, saved-prompt combinations, and Codex task workflows need observed outcomes. |

## Home and navigation

| Command | Current behavior / friction | Recommendation |
|---|---|---|
| Recent conversations/tasks 1–3 | Direct jump with readable state cards. A strong recurring action; several similar titles may still be hard to distinguish. | Keep. Evaluate finding the intended chat and Waiting/Complete states, not just whether a tap dispatches. Never invent background state. |
| Chats / All Chats | Lists conversations reported by accessibility; it is not guaranteed to expose the entire account history. The displayed name differs between the SDK caption and rendered face. | Use a consistent **Chats** label. Make the visible/recent scope clear inside; retain Find Chat for older items. Do not promise an exhaustive list. |
| New Chat / New Task | Presses the app's new-conversation control; success feedback is Requested. | Keep direct. Verify input focus, unsent-draft handling, and the Codex workspace/project shown before work begins. Do not claim Ready until observed. |
| Find Chat | Opens search; three controls precede results: Speak Query, Type Now, and query/status. Results are selectable from the keypad. | Keep the spoken search-to-result flow. Give query/results priority; Type Now is a focus fallback, not a mandatory step. Show actionable setup/unavailable/no-result messages. Do not start recording just by opening search. |
| Search query/status | A status-looking tile refreshes on tap; empty query says Type on your Mac. Zero results currently says Check App. | Display the spoken or typed query, distinguish **No matches** from unsupported search, and make retry behavior explicit. Avoid three keys appearing to be three separate search modes. |
| Changes in Codex | The Find Chat position instead opens the changes view. | Keep this useful direct navigation. Distinguish **Changes** (view) from **Review Changes** (ask Codex for findings). Reading code on the monitor is expected. |
| Dictate | Records an instruction, then appends the transcript without sending. | Preserve the owner-confirmed flow and physical position. |
| Send / Stop | Executes the verb shown, with duplicate-tap and completion-race guards. | Preserve. Evaluate a full request-to-response flow rather than adding another Send key. |
| Voice Chat | Controls the app's native conversation, separate from local dictation. Shortcut configuration keeps the label Voice Chat / TOGGLE even when active. | Preserve direct access. Use End Voice/active feedback only when the state is observable; do not infer activity from a shortcut press. |
| Mode | Shows current ChatGPT/Codex and the destination footer. | Keep the explicit names and stable location. Verify a switch completes before presenting destination-specific actions as ready. |

## Capture, Tools, and More

| Command | Current behavior / friction | Recommendation |
|---|---|---|
| Attach Files | Opens Add files and more; the user completes selection in the app picker. | Complete common selection paths: bounded recent Downloads inside the existing key, deliberate selection, then Attach. Keep Browse as fallback. Read files on demand. |
| Paste into Chat | Immediately appends clipboard text. Current capture route reads text, not a file-selection payload. | Extend the existing action to recognize copied files and images, preserve the draft, and confirm the correct result. Show what kind of material will be inserted; do not paste both filename text and the file accidentally. |
| Screenshot | Select region → attach to pinned chat → add instruction → explicit Send. | Keep. It completes capture while preserving intentional visual selection on the Mac. |
| Copy Reply | Copies the identifiable completed response without requiring selection. | Keep the verified implementation. In Codex it is buried in More; evaluate whether code/report copying makes it frequent enough for a common Tools position. |
| Clear Added | Clears internal staged sources only. It does not remove text or attachments already inserted. Clipboard and screenshot now bypass staging. | Remove from everyday Tools or show it only inside a source-staging workflow with actual staged sources. Do not silently turn it into Clear Draft. |
| Saved Prompts | Nine favorites that adapt by mode; some send, some record speech, some prepare drafts. | Keep optional, with target and action understandable before the tap. Avoid adding more favorites until the existing flows work with pasted/attached material. Detailed review below. |
| More | Contains additional app controls and workflows. | Retain as an overflow container. A low-frequency app section does not need a permanent key. Hide or explain unavailable controls instead of filling a page with dead ends. |
| Projects | Opens the app's project control, then selection remains in the app. | Optional now. If switching projects proves frequent, offer named recent projects and verify the chosen destination. Showing a project picker alone has limited benefit. |
| Plugins, Scheduled, Explore | Open respective app sections. Configuration and discovery remain in the app. | Keep optional; no added guided keypad wizard without a demonstrated recurring task. These are navigation conveniences, not completed workflows. |
| Permissions | Opens the Codex permissions control. | Keep optional. Read the actual setting in the app; do not turn a generic button into an automatic permission change. |
| Pull Requests | Opens the PR-related app control; does not identify and review a specific PR. | Keep optional. For a frequent review flow, select a PR identity then invoke Review PR. Preserve the distinction between navigation and requesting review. |
| Quick Chat | Requests the app's quick-chat control. From an already open chat it may add little value. | Evaluate from another app; deprioritize inside the default in-app menu unless the owner uses it. |
| Approve / Deny | Targets the displayed request with identity guards and a second press for high-risk requests. Default Codex Tools shows generic verbs and REVIEW REQUEST. | Keep direct decision keys and existing guards. Make waiting noticeable and tie the decision to the current task. The full request belongs on the Mac screen. Do not move keys automatically while the user is reaching for them. |
| Return to App | Removed from ChatGPT's main Tools, but still included in Codex More and optional capture routes. Clipboard paste does not identify the source app. | Remove from default Codex More too. Retain only in an explicit source-capture route where source identity is known. |
| Use Selection / Ask ChatGPT / Paste Reply | Optional source-app flow stages selected text and records an origin; returning and pasting require a valid original source and an explicitly selected empty field. | Keep advanced/optional. Evaluate the whole email-to-draft round trip. Do not add Ask ChatGPT inside ChatGPT or claim it can infer an email reply field. |

Common Tools actions currently move across modes: Copy Reply becomes Approve; Paste into
Chat becomes Review Changes; Screenshot moves from middle-right to bottom-left. This
is a muscle-memory cost for users who alternate modes. Review a stable common capture
row in both modes, while keeping Codex decision/task actions reachable. A nine-key
page forces tradeoffs: this review does not silently reorder the working profile.

## Saved prompts: target and completion

There is a concrete source-level incompatibility between two individually useful actions:
**Paste into Chat → Saved Prompts → Summarize**. Clipboard text is now already in the
composer, and staged-source count is zero. The non-speech workflow therefore calls
`WriteComposer`, whose native write path rejects a nonempty composer with `draft-exists`.
The key reports **Draft Exists**. This is a code-path finding, not a new live test result.

Separately, the current Summarize prompt explicitly targets the conversation. An
attachment does not change that prompt into a request to summarize the document.
Similar target assumptions affect Explain, Brainstorm, Plan, and Continue.

Recommended behavior: when a source-based task is applied to material already in the
composer, preserve it and append the appropriate instruction as an unsent draft. Show
the target and next action. For an empty composer and an established conversation,
clearly scoped conversation actions may remain direct. Do not add a source-selection
submenu when there is only one clear target, or erase draft protection to avoid an error.

| ChatGPT prompt | Current target/action | Review decision |
|---|---|---|
| Summarize | Conversation so far; sends immediately with an empty composer. | Highest priority: support pasted/attached material; explicitly distinguish document/input from conversation summary. |
| Explain | Topic already discussed; immediate send. | Same source-composition issue. Make screenshot/document explanation possible without rewriting the instruction manually. |
| Rewrite | Speak target, audience and tone; review draft; send. | Useful, but prove Paste → Rewrite preserves the source. Keep the brief short and target clear. |
| Draft Reply | Speak recipient, goal and tone; review draft; send. | Useful customer-email workflow. Evaluate against plain Dictate: the template should reduce spoken effort, not demand repeating everything. |
| Compare | Speak options; review draft; send. | Useful when two files/texts are present. Must use both supplied sources and ask for missing information rather than silently compare something else. |
| Research | Speak topic; review draft; send. | Optional. This inserts a research request; it does not select a dedicated app research mode or guarantee particular tools are available. |
| Brainstorm | Existing discussion; immediate send. | Optional continuation action. Clarify target and handle existing input; avoid presenting it as a new-topic wizard. |
| Plan | Existing discussion; immediate send. | Useful when turning a discussion into steps. Clarify that it sends an instruction; it is not an app Plan-mode toggle. |
| Continue | Existing discussion; immediate send. | Advanced/optional. The next action may be ambiguous; make its prompt available for review/customization. |

| Codex prompt | Current target/action | Review decision |
|---|---|---|
| Review Changes | Ask for findings on current uncommitted changes; no edits; immediate send. | Keep a direct repeat action. Show task/workspace context and distinguish request submission from review completion. |
| Debug | Speak error; prepare a request to reproduce, fix, and add a regression test. | Good candidate for Screenshot/Paste → Debug. Verify the error reaches the same task and scope stays understandable. |
| Refactor | Speak area; request behavior-preserving edits and test execution. | Optional advanced action. Target must be clear; a generic Refactor label must not imply read-only explanation. |
| Run Tests | Ask Codex to run existing relevant tests and report actual execution; no code edits; immediate send. | Keep. It requests execution through Codex, not a local test runner. Show Sent/Working and report results only when observed. |
| Explain Diff | Uncommitted changes, otherwise last commit; immediate send. | Useful review action. Expose which scope applies; the fallback can surprise users. |
| Fix CI | Latest failing run; requests a fix; immediate send. | Optional until branch/run identity is clear. Do not silently choose an unrelated run. |
| Security | Recent changes; asks for findings; immediate send. | Optional advanced review. Present as a security review request, not a certification or a separate scanner. |
| Update Deps | Requests dependency updates, lockfile changes, and tests; immediate send. | Keep out of beginner defaults. Make the change-producing scope clear and allow preparing a draft before starting. |
| Continue | Restate objective and proceed; immediate send. | Optional. Do not treat it as an approval shortcut or assume the next intended task. |
| Review PR (More) | Speak PR identity and scope; review draft; send. | Useful only with an identifiable PR. A named recent-PR selector may reduce more effort than another generic shortcut. |
| Write Tests (More) | Current diff, otherwise last commit; requests new tests, runs suite and fixes failures; immediate send. | Keep distinct from Run Tests. Clarify scope and that it edits files. |

The current SEND/SPEAK/DRAFT footers already expose part of this distinction. Preserve
that useful feedback, but do not expect a beginner to infer a source target or the scope
of edits from a small footer alone. Avoid imposing an extra confirmation on every direct
action merely to make all commands behave identically.

## Optional and legacy bindings

| Binding family | Decision |
|---|---|
| Separate Stop and Send Draft | Retain for custom profiles; default Home's Send/Stop already covers them. |
| Show ChatGPT | Useful outside the app; redundant inside its active profile. |
| Activity | Useful as an ambient indicator on another profile. No-op on tap is intentional; avoid duplicating the conversation cards on Home. |
| Dictate & Send | Explicit advanced opt-in; keep separate from default Dictate, which must remain an unsent draft. |
| Older Search / Show Diff / Copy Answer / Changes bindings | Preserve compatibility but avoid duplicate entries in the recommended profile. Older bindings do not automatically inherit the usability of the current supported route. |
| Adaptive 3 / old source pages | Review as separate optional layouts. Do not count their alias bindings as additional commands a beginner must learn. |
| Voice model setup | Setup/recovery UI, not another daily command page. Keep model preparation failures distinct from app search unavailability. |

## Priority and acceptance workflows

1. **Make source material and instructions compose:** file attachment, copied file/image
   support, and Paste/Attach → Summarize/Explain. Preserve owner-confirmed capture routes.
2. **Remove confusing choices:** Clear Added in an everyday flow, Return without a source,
   ambiguous summary targets, and vague success/error feedback.
3. **Prove search and Codex task loops:** query → correct chat, workspace → review → tests,
   pending request → informed decision. Evaluate stable common Tools positions before any
   profile reorganization. Keep the working Home unchanged during these improvements.
4. **Promote other navigation only with evidence of repeated use:** Projects, PRs and
   app-section openers remain optional until they reduce measurable effort.

| User / scenario | Complete workflow to observe | Success criterion |
|---|---|---|
| Beginner, customer reply | Paste email → Draft Reply or Dictate → review → Send → Copy Reply | No re-paste, lost source, surprise submission, or answer selection required. Record which instruction method was easier. |
| Beginner, explain an image | Screenshot → Explain or Dictate → Send | Uses the attached image and preserves notes. The new Explain composition behavior is proposed; Dictate already has owner confirmation. |
| Intermediate, compare documents | Attach two chosen files → Compare → brief → Send | Exact files are visible, selection is reversible before attach, and the response compares both. |
| Intermediate, resume older work | Find Chat → Speak Query → finish → select result → continue work | Query is visible, titles distinguish results, intended chat opens, and results selection returns to Home. Observe zero-result/retry paths too. |
| Advanced, assess a code change | Confirm task/workspace → Review Changes → inspect findings/diff → Run Tests | Correct scope, visible working/waiting states, actual commands/results in the app, no false success on request submission. |
| Advanced, handle an error | Paste error or Screenshot → Debug → brief → Send → review changes and tests | Same task receives the context; changed targets refuse delayed delivery; edits and test results are reviewable. |

Measure total time, keypad taps, keyboard/mouse returns, wrong choices, retries, and
whether the user can explain what happened. An intentional screen review is different
from a forced keyboard step to finish an incomplete command. Automated checks should
guard target identity, source/draft preservation, attachment acknowledgment, and send
intent. Hardware observation establishes readability and whether the flow is effortless.

## Source anchors

- [Installed page definitions](../tools/make-desktop-profile.py): HOME and TOOLS.
- [Context menu actions](../src/Core/DesktopActions/DesktopContextCommand.cs) and
  [app control labels](../src/Agents/OpenAiDesktop/OpenAiDesktopAdapter.cs).
- [More contents](../src/Core/DesktopActions/DesktopMoreDynamicFolder.cs) and
  [Saved Prompts contents](../src/Core/DesktopActions/DesktopSavedPromptsDynamicFolder.cs).
- [Capture and staging](../src/Core/Desktop/DesktopContextCapture.cs).
- [Workflow definitions and dispatch](../src/Core/DesktopActions/DesktopWorkflowCommand.cs),
  [spoken workflow delivery](../src/Core/Desktop/DesktopWorkflowVoice.cs), and
  [native write guard](../tools/desktop/VizhiAxBridge.swift).
- [Search page](../src/Core/DesktopActions/FindChatDynamicFolder.cs),
  [query controls](../src/Core/DesktopActions/DesktopSearchCommand.cs), and
  [chat listing](../src/Core/DesktopActions/AllChatsDynamicFolder.cs).
- [Default Tools routing](../src/Core/DesktopActions/DesktopToolsCommand.cs),
  [approval behavior](../src/Core/DesktopActions/DesktopApprovalCommand.cs), and
  [command/acceptance catalog](../tests/desktop/commands.json).
