# Vizhi Desktop: keypad UX recommendation

Date: 2026-09-17  
Branch: `integrate/vizhi-desktop-main`  
Status: Implementation authorized and applied on the integration branch; live validation and installation pending.

## Recommendation

Retain the existing three-page structure and improve clarity and behavior before making a major layout change:

| Page | User's purpose | Contents |
|---|---|---|
| Conversations | Return to work and respond | Stable conversation cards, navigation, approvals, voice |
| Controls | Operate the app | Mode, Stop, attachments, draft/send, changes, app destinations |
| Workflows | Give a useful instruction | Nine editable, mode-specific workflow favorites |

Design for beginners, intermediate users, and advanced users through clear defaults and optional customization. Avoid forcing users to choose an expertise level or automatically changing their familiar approval positions.

## Context and evidence

This recommendation follows a brainstorm and a second review informed by six photographs of the current MX Creative Keypad ChatGPT profile:

| Photo | Visible page |
|---|---|
| `IMG_2863.HEIC` | Conversations |
| `IMG_2864.HEIC` | ChatGPT controls |
| `IMG_2865.HEIC` | ChatGPT workflows |
| `IMG_2866.HEIC` | Conversations |
| `IMG_2867.HEIC` | Codex controls |
| `IMG_2868.HEIC` | Codex workflows |

The original photos were supplied from `/Users/ravishankar/Downloads/`. They are not copied into this repository. Some have motion blur, so they are evidence of layout and visible labels, not a reliable measurement of display sharpness or font quality.

Live inspection of the ChatGPT/Codex app was blocked by Computer Use. The discussion used the photographs, repository implementation and recorded app observations, and official documentation. A photographed key establishes its visible presence, not that its action works correctly.

Related context:

- [Desktop product plan](desktop-product-plan.md)
- [Integration handoff](HANDOFF-vizhi-desktop-merge.md)
- [Product README](../src/Products/VizhiDesktop/README.md)
- [App commands](https://learn.chatgpt.com/docs/reference/commands)
- [Voice and dictation](https://learn.chatgpt.com/docs/features/voice)
- [Code review scopes](https://learn.chatgpt.com/docs/code-review)

## Why the earlier proposal was revised

An earlier proposal repeated **Dictate · Send · Stop** on every page and moved approvals behind a **Needs You** view. The revised recommendation supersedes that proposal:

1. Repeating three controls consumes a third of every page and reduces workflow capacity from nine actions to six. Consistency is valuable, but does not require duplicating every control.
2. Hiding approvals adds a navigation step to one of the product's strongest Codex interactions: responding to a pending request with a physical key.
3. Making every workflow a draft adds friction when its target and instruction are already complete.
4. New ideas such as Copy Answer, native Voice Chat, and Terminal need live feasibility checks before becoming promised default controls.

## Page 1: Conversations

Preserve the current arrangement initially:

| Left | Centre | Right |
|---|---|---|
| Conversation 1 | Conversation 2 | Conversation 3 |
| All Chats | New Chat | Search / Changes |
| Approve | Deny | Voice |

The middle-right position is intended to follow the app mode: Search in ChatGPT and Changes/Files in Codex. The final label should accurately describe the verified control.

### Improvements

- Keep conversation positions stable while their status changes.
- Allow optional short keypad labels such as “Release” or “Research” without renaming the actual chat.
- Identify the conversation associated with an approval when that identity is observable. Do not guess when it is ambiguous.
- Make Approve and Deny visibly inactive when no request is pending.
- Keep their positions and meanings stable; an ordinary key must not unexpectedly become an approval key.
- Explain the Voice action's sending behavior during onboarding. The existing Voice action transcribes and sends; Voice Draft prepares editable text.

### Optional everyday preset

For users who seldom encounter approvals, offer an explicit ChatGPT everyday preset with **Dictate · Send · Stop** in the bottom row. Dictate in this preset means speech-to-draft, followed by an explicit Send.

This is a user-selected alternative, not an automatic replacement of approval keys when switching app modes. Preserve the established layout for users who depend on direct approvals.

### Observation requiring investigation

Both conversation-page photos show Search and the same titles. If `IMG_2866` was captured after switching to Codex and allowing the UI to settle, investigate mode detection and refresh behavior. The photos alone do not establish whether this is a timing issue, an older installed version, or incorrect mode handling.

## Page 2: Controls

Keep **Mode · Stop · Voice Draft** in the top row. Complete the two unused positions before displacing existing actions:

| Mode | First addition | Second addition |
|---|---|---|
| ChatGPT | Send | Copy Answer, after live verification |
| Codex | Send | Changes, after live verification |

This supports **Voice Draft → inspect the draft → Send** on one page. Send must act on the intended conversation and must not silently send text in another window.

Existing visible controls include:

- ChatGPT: Projects, Plugins, Scheduled, Explore.
- Codex: Permissions, Attach Files, Pull Requests, Quick Chat.

Measure actual usage before replacing these destinations. Less frequent controls can become optional assignments when evidence supports that choice.

### Labels and icons

- Use a square Stop icon; the photographed arrow-in-a-box can suggest navigation.
- Distinguish a writing Draft workflow from dictation; a microphone icon is misleading for text composition.
- Distinguish the Mode control from Plugins rather than relying on similar sparkle icons.
- Define whether the Mode label identifies the current mode or the destination. Recommended: current mode plus an explicit switching symbol; verify the interaction before changing it.
- Reserve **Voice Chat** for a live spoken conversation with the app. Do not use that name for speech-to-text dictation.
- Prefer concise labels and useful unavailable states such as “No Draft” and “No Changes.”

Copy Answer must target the latest completed assistant response in the intended conversation, not the first or last arbitrary Copy control in the window. Native Voice Chat and Terminal remain exploration candidates, not committed default actions.

## Page 3: Workflows

Keep nine workflow positions. Improve scope, submission behavior, and customization before rearranging them.

### Current ChatGPT set

| Left | Centre | Right |
|---|---|---|
| Summarize | Explain | Rewrite |
| Draft | Compare | Research |
| Brainstorm | Plan | Continue |

### Current Codex set

| Left | Centre | Right |
|---|---|---|
| Review PR | Debug | Refactor |
| Write Tests | Explain Diff | Fix CI |
| Security | Update Deps | Continue |

### Submission rules

- **Send immediately** when the target and instruction are complete. “Summarize this conversation” is a candidate.
- **Prepare a draft or open a chooser** when a target, scope, or required detail is missing. Reviewing a particular PR needs an identified PR.
- Make the difference visible and explain it during onboarding. Ellipses alone are not enough to assume a beginner understands the behavior; prototype a compact Draft/Send indicator and check readability on hardware.
- Let users replace defaults with their own frequent workflows.
- Keep **Review Changes** distinct from **Review PR**. Review scope may be uncommitted changes, a branch comparison, or a commit.
- Keep **Write Tests** distinct from **Run Tests**. A request to execute tests is not evidence that they passed.

## Experience levels

| User | What the design should provide |
|---|---|
| Beginner | Clear labels, a short first-use walkthrough, understandable sending behavior, optional everyday preset |
| Intermediate | All three pages, short conversation labels, editable workflow favorites |
| Advanced | Direct approvals, stable task positions, scoped workflows, selected global-profile actions |

Do not assume experience in ChatGPT and experience in software development are the same. Offer choices based on how people work, rather than mandatory Beginner/Advanced modes.

Global-profile actions remain useful for supervision while another app is frontmost. Background actions need an identifiable target; the user should not have to infer which conversation will receive a command.

## Proposed order of work

1. Verify installed-version and mode-switch behavior, including Search/Changes and conversation refresh.
2. Add explicit Send and validate the complete Voice Draft → Send flow.
3. Validate Copy Answer and the Changes control, then use the empty controls-page positions.
4. Clarify Voice, Voice Draft, Mode, Stop, and writing-workflow labels/icons.
5. Make workflow draft/send behavior and scope explicit.
6. Add optional short conversation labels and workflow favorites.
7. Prototype the everyday home preset and compare it with the established layout using actual tasks.

The user subsequently authorized implementation, including profile changes. Installation remains a separate step.

## Validation questions

- Can a first-time user predict whether each voice/workflow key will send or draft?
- Can they dictate, inspect, and send without hunting across pages?
- Can an experienced user approve the intended request without losing their place?
- Do conversation keys remain stable and identifiable when chats reorder?
- Does a window or mode change ever redirect a pending operation?
- Are labels and state indicators readable at normal seated distance?
- Does the everyday preset improve common ChatGPT tasks enough to justify maintaining it?
- Which app destinations are used often enough to retain default key positions?

## Implementation status

Implementation target: **0.11.0**, branch `integrate/vizhi-desktop-main`.

| Plan item | Result |
|---|---|
| Preserve three pages and direct approvals | Adaptive 2 profile generated, 27 bindings; home approval row retained |
| Add Send | One helper invocation, unique composer, exact enabled local Send, non-empty draft, window/composer/text recheck; no send while busy or waiting approval |
| Fill Controls positions | Send plus adaptive Copy Answer / Changes; Copy remains unavailable pending live feasibility |
| Clarify labels and icons | Square Stop, switching arrows, writing glyph, grey idle approvals, full-tile DRAFT/SEND strips for voice and workflows |
| Improve workflow scope | Review PR, Debug, and Refactor draft in new defaults; optional Review Changes and Run Tests actions; pre-existing drafts preserved |
| Short conversation labels | Mode-scoped JSON aliases, used only for display; original targeting and approval identities retained |
| Workflow favorites | Existing per-mode JSON ordering retained; examples and editing instructions added; user files preserved |
| Everyday preset | Separate explicit-import profile with Voice Draft (dictate to draft) · Send · Stop; no automatic approval-row switching |
| Profile migration | New stable GUIDs, existing selection/customizations retained; desktop preview metadata replaces stale donor metadata |
| Mode refresh investigation | Regression covers same titles across a mode change and verifies refreshed controls/draft state; photographed behavior still needs reproduction on hardware |

See the [product walkthrough and configuration guide](../src/Products/VizhiDesktop/README.md),
[default profile](../src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5), and
[Everyday profile](../src/Products/VizhiDesktop/package/optional-profiles/VizhiDesktop-Everyday.lp5).

**Remaining live validation:** Voice Draft → inspect → Send in ChatGPT and Codex, with empty,
existing, and edited drafts; window/conversation changes while an operation is pending; mode
switch and Search/Changes refresh; readability of the new strips on the keypad; Everyday task
flow; and identification of the latest completed assistant message. Computer Use blocked live
app access during this work; that was not bypassed through the helper. Native Voice Chat and
Terminal remain future candidates. Copy Answer deliberately stays at **No Answer** and performs
no copy action until assistant ownership and completion can be established.

Automated validation covers the helper's actual matching functions against synthetic trees,
client/monitor behavior, aliases, profile identities/bindings, and preservation of existing
configuration. Helper/plugin builds are isolated from the live runtime. These checks do not
establish live app compatibility or hardware readability. The work has not been installed.

### Validation recorded for 0.11.0

- Full C# suite: **1,433 passed, 13 skipped** (platform-specific Windows tests).
- All script suites passed, including the production helper package-build test and synthetic AX tests.
- Release plugin build: **0 warnings, 0 errors**, with `SkipPluginLink=true`.
- Signed helper compiled using `--no-install` to a temporary output; the installed runtime was preserved.
- Both profiles: all 27 bindings and preview identities checked; a second generation produced identical bytes.
- New Stop and writing PNGs visually inspected. Standalone SDK tile rendering could not load the
  host's SkiaSharp dependency; final tile rendering/readability still needs the Logi host and keypad.

Reproduce with `bash tests/run-all.sh`, then
`dotnet build src/Products/VizhiDesktop/VizhiDesktopPlugin.csproj -c Release -p:SkipPluginLink=true`.
Regenerate profiles with `python3 tools/make-desktop-profile.py`; regenerate the new desktop-only
icons with `swift tools/generate-desktop-icons.swift --ux-only`.
