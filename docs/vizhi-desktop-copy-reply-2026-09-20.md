# One-tap Copy Reply — 2026-09-20

Version 0.15.7, integration branch `integrate/vizhi-desktop-main`.

Logging cleanup (unreleased, after owner acceptance of 0.15.13): the temporary key-event
and per-copy result traces described below have been removed from source. Native copy
verification and keypad feedback are unchanged. Earlier log excerpts remain historical
evidence, not a logging contract for future builds.

## User workflow

Wait for the answer, use the physical right paging button to reach **Tools**, tap its
top-middle **Copy Reply** key, then paste into an email or document. No text
selection is needed. The key shows **Copied** only after the app acknowledges the copy and
fresh text reaches the clipboard. The app's HTML clipboard representation is preserved.
Vizhi retains the plain text for the existing **Paste Reply** action.

The key dims with **Wait** during generation, a pending approval or native Voice Chat. It
dims with **No Answer** when the newest identified message is from the user. Missing speaker
semantics show **Check Chat**; missing response controls show **Use App**. These cases do not
claim that the conversation has no answer. Errors appear once in the footer, retaining the
**Copy Reply** title.
Copying a new answer clears the previous retained reply before attempting the operation;
a failed attempt cannot make Paste Reply silently reuse an older answer.

## App evidence and implementation

Read-only inspection of the installed app's shipped JavaScript, version 26.915.31945,
identified these semantics. No live ChatGPT accessibility tree or conversation was inspected.

- `user-message-4c56b67dede4.js`: level-four screen-reader headings **You said:** and
  **ChatGPT said:** mark message ownership.
- `conversation-blocks-6c1d988a9fe3.js`: the assistant action row supplies **Copy response**
  as the copy tooltip, with response-only sibling actions such as **Fork chat from here**.
- `app-initial-a498f911edeb.js`, shared copy component: the accessible button label is
  **Copy**, changing to **Copied** after the click. The copy payload supports text and HTML.

The adapter owns these labels. The Swift helper's new `copy-reply` verb checks the latest
speaker heading and the response-copy action. A generic Copy button needs an adapter-owned
sibling in the same action group, with no intervening message content; code-block buttons and user-message controls do not qualify.
It never falls back to an earlier answer or selected text. A distinct verb makes older
helpers refuse the operation instead of silently performing the former selection copy.

The helper checks task Stop, approvals, selected-sidebar activity and native Voice state.
It pins the focused window, selected conversation and button through a second scan and
clipboard acknowledgement. Ambiguous or truncated trees are refused. The status poll uses
the same targeting function, but never reads or writes the clipboard. Only an explicit
Copy Reply press accesses it. Response content is not added to plugin diagnostics.

## Verification and limits

Unit tests cover adapter arguments, key availability, fresh reply retention and failure
feedback. The compiled native fixture uses disposable message headings, action groups,
older answers, a user message, code-copy controls and a named test clipboard. It covers
copy acknowledgement, text/HTML preservation, blocking states, ambiguity, missing answers,
conversation changes and focused-window targeting.

Native fixture checks establish the helper contract; they do not prove the installed app
exposes every DOM element through AX identically. Unknown/localized layouts remain unavailable.
The separate older **Copy Answer** optional command remains unsupported; the Home/Tools
profile's **Copy Reply** is the command updated here.

Owner acceptance: finish an answer containing paragraphs and a code block, leave all text
unselected, tap Tools → Copy Reply, and paste into a disposable document. Confirm the full
answer was copied. Start another response and confirm the key shows Wait and does not copy
the older answer. Repeat in another conversation and after opening a new empty chat.

## 0.15.7 correction after owner acceptance failed

The owner reported **No Answer / No Answer** on the keypad. Further inspection of shipped
`viewer-6548e1806885.js` found a concrete missing layout: `wC` passes **Continue in new chat**
and `overflowActions` into the shared `r_` row, which suppresses its ordinary Fork button when
the overflow is present. `$g` exposes **More actions**; **Branch in new chat** is inside the
closed menu. The 0.15.6 fixture incorrectly assumed the default Fork sibling was always present.

Recognize the overflow control as AXButton or AXPopUpButton, or the direct Continue in new chat
button. Only the response's Copy is pressed. The menu is never opened. A candidate cannot
cross heading, composer or non-control text boundaries to borrow another footer's actions.
The status contract now includes `copyAnswerError`, a fixed reason code with no response text.
Regression tests cover both viewer layouts and a code-block Copy separated from a footer.
This source finding explains a supported layout being rejected; live owner acceptance is
still required because the fixture cannot establish the real app's AX hierarchy.

Verification for 0.15.7: 1,906 C# tests passed (13 platform skips), production Swift selector
regressions passed, and both ChatGPT footer layouts plus selection refusal cases passed in the
native fixture. Final acknowledgement/window-change cases were interrupted by foreground
focus loss; the entire native run is not marked passed. Evidence is under
`artifacts/desktop-copy-fix/`. Installation loaded successfully at 16:35 on 2026-09-20, with
profile and configuration preserved; see `artifacts/desktop-copy-fix/installation.json`.

## 0.15.8 diagnostic records

Versions through 0.15.7 displayed the copy result on the key but did not persist an outcome.
SDK redraw messages and absence of helper warnings could not verify the owner's repeated taps.

0.15.8 writes `DesktopCopyReply: result=requested`, then `result=copied`, a guard such as
`result=mode-changed`, or `result=failed reason=<fixed-code>` to
`~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/VizhiDesktop.log`.
The host prefixes timestamps. Tools also records `DesktopTools: copy_approve key-event`;
this is the shared Copy/Approve slot and alone does not establish that copying ran.

Only `result=copied` confirms that the helper succeeded with nonempty fresh text and Vizhi
retained it. Requested/redraw/key-event records are not success. Unknown errors become
`unknown-error`. No reply/clipboard text, titles, source names, or arbitrary exception/error
strings are logged. No new app inspection, microphone capture or clipboard operation is
performed by diagnostics. Existing checks and native helper behavior are preserved.

Read the latest timestamped request and its result after an owner tap. A busy record may
interleave with an earlier in-flight copy; do not attribute that earlier success to the busy
request. Attempts made before installing 0.15.8 remain unconfirmed unless the owner observed
Copied and verified their pasted response.

## 0.15.9 — web controls without action-row groups

Owner diagnostics at 17:11:06 and 17:49:46 reported `copy-control-unavailable`: the helper
identified an assistant speaker but could not match a response-copy control. The old tests
used explicit AppKit AXGroups for every HTML wrapper.

A new isolated WKWebView fixture reproduced that same rejection using ordinary response
markup. Its Copy and More actions buttons were AXButton/AXPopUpButton peers at the same
level as message text; the plain div wrappers did not create the small AXGroup the selector
required. The unmodified 0.15.8 helper returned canCopyAnswer=false/copy-control-unavailable;
the new helper returned the complete fixture answer through the native button's click handler.
This establishes a real web compatibility defect, not proof of the owner's exact Chromium tree.

`replyActionRun` recognizes contiguous sibling button/popup controls. It requires one generic
Copy and a known response sibling; text, headings, other groups and the composer terminate
the run. It retains the prior explicit Copy response and grouped-footer support. Code Copy
cannot cross its code text to borrow a response footer. Clipboard copying is still performed
by the app's own button, with the same acknowledgement, window and conversation checks.

Run the new isolated test with:
`python3 tools/desktop-test/web-reply-fixture.py artifacts/desktop-copy-web/native`
It targets only com.vizhi.desktop.testfixture with a nonpersistent local WKWebView and a
named test clipboard; it cannot inspect or operate the real ChatGPT app. Real keypad owner
acceptance remains separate. The per-tap diagnostics from 0.15.8 remain enabled.


## Owner failure after 0.15.9 and diagnostic follow-up 0.15.10

Owner keypad attempts at 18:14:05, 18:14:12 and 18:14:20 reached the helper and returned
`ambiguous-answer` before pressing Copy. Installed 0.15.9 was loaded successfully. The
controlled WKWebView tests did not establish compatibility with the real Electron app.
The generic reason covers four guards, so it does **not** establish that multiple Copy
buttons were found: web/composer counts, dialogs and sidebar selection also use it.

0.15.10 preserves those guards and reports their individual fixed failure codes:
`reply-web-area-missing`, `reply-web-area-multiple`, `reply-composer-missing`,
`reply-composer-multiple`, `reply-dialog-open`, `reply-selection-multiple`, and
`reply-copy-multiple`. No app content is added to diagnostics. The keypad keeps its
Check Chat failure footer. This release improves diagnosis; it does not claim to fix the
owner's Copy Reply failure. A new owner attempt is required to identify the failing guard.


## Confirmed owner failure and correction — 0.15.11

The 0.15.10 owner tap at 20:34:08 reached the helper; the result at 20:34:09 was
`reply-composer-multiple`. This establishes that the window scan had more than one
AXTextArea, not that multiple Copy controls were found. The identities/content of those
text areas were not inspected. The count guard rejected the request before footer lookup.

Copy Reply now has no composer-count requirement. It targets a native response button;
it neither writes to nor focuses an editor. AXTextAreas still delimit response action rows.
The existing unique response target, single web area, dialog/activity, selected conversation,
window pinning and fresh clipboard acknowledgement checks remain. Write/send are unchanged.

Before the fix, an isolated WKWebView with additional editors reproduced exactly
`reply-composer-multiple` (`artifacts/desktop-copy-editors/before/fixture-result.json`).
The updated fixture also uses the user-described Copy / Rate response / Branch in new chat
footer, and covers duplicate response controls, code-only Copy and a read-only conversation.
This reproduces the failing condition, not the complete real Electron accessibility tree.


## Remaining owner failure — 0.15.12 diagnostics

Both 0.15.11 owner taps at 20:39:37 and 20:39:46 failed with `copy-control-unavailable`.
They passed the removed composer-count guard, but no response Copy target qualified.
Neither attempt pressed the app's Copy button or copied a response.

An isolated Electron 44.4.3 test with the owner-described Copy / Rate / Branch row,
additional editors and non-hidden SVG button icons successfully copied using the 0.15.11
helper (`artifacts/desktop-copy-chromium/probe`). A general Chromium/row failure was not
reproduced. This fixture cannot establish the real app's current accessibility structure.
`tools/desktop-test/chromium-reply-fixture.py` makes that test repeatable using a supplied
Electron.app runtime and a fresh output directory. It uses only the fixed test bundle,
local HTML, an isolated user-data directory and the named test clipboard.

0.15.12 preserves target acceptance and reports which remaining selector check refused:
`reply-copy-not-found`, `reply-copy-outside-latest`, `reply-copy-wrong-role`,
`reply-copy-nested-control`, `reply-action-not-found`, or `reply-action-row-unrecognized`.
No new app text/labels/element identifiers are emitted. The keypad keeps the Use App footer.
This is diagnostic work, not a claim that Copy Reply is fixed in the owner's app.


Verification limitation for 0.15.12: 1,942 C# tests and the selector regressions passed.
The isolated Chromium fixture copied the full response successfully with the packaged
helper. Nine WKWebView variants passed, but the no-ack variant returned answer-changed
instead of reaching its acknowledgement refusal. Two retries were interrupted earlier by
app-not-frontmost. These results are retained under desktop-copy-controls; no all-green
WKWebView claim is made. Target acceptance/press/acknowledgement logic is unchanged by
this diagnostic release. The installer records the incomplete no-ack check explicitly.


## Confirmed row association failure — 0.15.13

The 0.15.12 owner tap at 20:56:27 returned `reply-action-row-unrecognized`. Exact Copy
and response-action controls exist after the latest assistant heading, but neither the
contiguous peer run nor the ancestor-group checks qualify their relationship.

A Chromium fixture with individually wrapped Copy / Rate / Fork buttons and a small
static timestamp reproduced the same refusal before this fix. This is a reproduced
class of layout failure; the exact owner's AX hierarchy was not inspected.

0.15.13 recognizes Rate response plus Remove good response feedback / Remove bad response
feedback from the installed app's plan-summary-item-content-8d19152d3623.js definitions.
A fallback reads the native frames of exact response controls: Copy plus two differently
named response actions must be nonoverlapping, similarly sized icon buttons aligned in
one compact row. All three must follow the latest assistant heading. Zero/missing/invalid
frames, distant controls and different rows do not qualify. Duplicate qualifying Copy
controls still refuse. No coordinates are clicked or logged: the chosen native AX element
is pressed and revalidated, with Copied plus fresh clipboard text required as before.

The previously failing wrapped Chromium fixture now copies the full response. Additional
cases cover code-only controls, separate rows, duplicate copies, new user turns, running
responses and missing clipboard acknowledgement. Artifacts are under desktop-copy-row.
Owner/keypad acceptance remains separate from this controlled proof.


## Owner acceptance — verified on the real keypad

The owner confirmed **“yes finally it works”** after installing 0.15.13 and pressing
Copy Reply in ChatGPT. Mark this workflow accepted on the actual MX keypad.
The installation receipt and artifacts/desktop-copy-row/owner-acceptance.json record
that confirmation and the narrow operational Copy Reply results. This acceptance covers
the tested conversation/workflow; it does not claim every app layout has been exercised.
