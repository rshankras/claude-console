# Find Chat on the MX Creative Keypad

Branch: `integrate/vizhi-desktop-main`. Initial release: 0.12.6; recovery update: 0.12.7.

## Plan and user flow

1. Replace the ChatGPT home Search position with **Find Chat**. Codex keeps its one-press
   **Changes** action in that position. Profile IDs and all other bindings stay stable.
2. Find Chat opens native app search and a keypad page: **Speak Query**, **Type Query**,
   query/status, then conversation results. The SDK supplies Back and pagination.
3. Type Query focuses the app's search field. Type on the keyboard; results refresh on the
   keypad. Speak Query records locally; a second press transcribes directly into that same
   search field. It never writes to the message composer or submits a message.
4. Results use the conversation-card typography with an **Open chat** footer, without
   inventing task state. Tap the exact result to open it and return home.
5. Back invalidates the search session and stops its capture. Late transcripts are discarded.
   The native search surface is left available in the app; no generic Escape is injected.

## Automation contract

Search requires one enabled semantic search field inside a dialog, with results exposed as
conversation links in that dialog. Links must have exact titles and chat/conversation URL paths;
duplicate or ambiguous identities are excluded. No arbitrary buttons, sidebar guesses, screen
coordinates, clipboard input, private app databases, or message text scraping.

The helper pins the foreground app/window/mode. Its session token includes process, unique
window title, and search-field identity. Write/select verify the expected query too; typing,
changing windows/modes, closing search, ambiguous matches, and partial accessibility trees
cause refusal. Result selection uses an exact URL plus title, never a row index.

If the app does not expose this contract, **Use App / Search unavailable** is shown. Native
search can still be used directly. Windows search remains unsupported until a verified adapter
exists. These limitations must not be represented as working keypad search.

## Verification

Unit tests cover session lifecycle, stale results, capture routing, failure/recovery, mode
dispatch and serialization. The native fixture exercises actual search-field writes, result
selection, draft preservation, and wrong-query/window/mode rejection, without microphone use.
Catalog tests cover new actions and shipped profiles. Full repository tests, all product builds,
package verification and installed hashes are release gates.

Live ChatGPT inspection was previously denied by Computer Use. Tests target only the controlled
fixture. Real ChatGPT accessibility compatibility, speech capture and keypad folder navigation
require owner validation and are not auto-passed.

## Owner acceptance

- ChatGPT: Find Chat → Type Query → type a known topic → matching cards → open the intended chat.
- Speak Query → speak → press again → query appears in search; existing message draft unchanged.
- Change the query/window/mode during capture: nothing is written to a different destination.
- Back during capture/transcription: capture ends, late text does not reach another session.
- More results than one page: Back/paging remain reachable; stale cards cannot open new rows.
- Codex: the same home position still opens Changes with one press.

Implementation and release evidence are recorded below.

## SDK integration notes

The [Logitech dynamic-folder contract](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/implementing-dynamic-folders/)
requires a folder binding to open this page; it does not provide programmatic folder opening.
Both shipped profiles therefore replace only the original home `DesktopContextCommand___primary`
binding with `FindChatDynamicFolder`. The legacy primary command remains available for custom
layouts. The installed SDK ignores Activate's return value on this device, so the Codex path
dispatches Changes and explicitly closes the already-pushed folder. Host-owned Back and paging
are retained; unchanged polling results do not trigger a page-list rebuild.

## Software verification

The initial complete run `20260918-191301-c2429b` passed: 1,737 C# tests, 13 Windows-only
skips, all script suites (including 20 harness self-tests), 92 catalog/mode dispatch variants,
13 native fixture steps, fixed-file speech inference and offline package verification.
The native search fixture includes external-host and duplicate-link decoys; only the two
unique conversation links are returned. No microphone or real ChatGPT test was run.

The rendered preview caught a missing disabled microphone asset, which was added before the
final release build. Synthetic preview: `artifacts/desktop-find-chat/preview/review.png`.
The final installation receipt and post-install verification report are recorded below.

## Installed release

Installed **0.12.6** on 2026-09-18. Logi Plugin Service confirmed version 0.12.6 loaded in
196 ms at `2026-09-18T19-17-25-360`. Both **Vizhi Adaptive 3** and **Vizhi Everyday** now use
Find Chat at Conversations row 2, column 3. Adaptive 3 remains selected. Four profile/preview
files changed; the other 24 application/profile files and all desktop configuration hashes
were preserved. The installer changed only the existing primary home binding and preview entry.

- Final pre-install run: `artifacts/desktop-tests/20260918-191603-22c04f/report.html` — PASS.
- Post-install run: `artifacts/desktop-tests/20260918-191736-eb2d14/report.html` — PASS.
- Receipt: `artifacts/desktop-find-chat/installation.json`.
- Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260918-191718/`.
- Package: `VizhiDesktop_0_12_6.lplug4`; installed DLL and both helper copies match the archive.

Final results: **1,737 C# tests passed**, 13 Windows-only tests skipped; script suites,
20 harness self-tests, all 92 command/mode variants, 13 native fixture steps, fixed-file
speech inference, and offline package verification passed. All three products built with
zero warnings/errors. External link validation was not run in offline verification.
All **174 owner-driven checks remain Not tested**; two existing Copy Answer cases remain
Unsupported. Installation does not establish live ChatGPT search or microphone compatibility.

## 0.12.7 follow-up: search unavailable on the owner's keypad

The owner reported that 0.12.6 displayed disabled Speak Query and Search unavailable / Use App,
with only Type Query enabled. That is a failed live experience; the fixture success above did
not establish compatibility with the installed ChatGPT search interface.

Code review found unnecessarily narrow mode/field recognition, an unhelpful catch-all error
face, no recovery after a slow first open, and a retry that could press the search opener
again even when an unsupported search field was already present. The patch covers these
cases without accepting a generic message composer or arbitrary window-wide search results.

- Exact search placeholder labels (including trailing ellipsis) and search roles are accepted.
- Mode buttons/popups and their descendant labels are recognized; message text is not a selector.
- A web area inside a native search sheet no longer breaks the ancestor search.
- Explicit Find Chat may focus the pinned target window. Probes, writes and selection never do.
- A failed first open can read the same window until its field appears. Once a field has been
  established, losing it still requires an explicit retry; no automatic retargeting.
- The retry key says **Open Search** until a field is available, then **Type Query**.
- The status card names the failing stage: Open app search, Search not ready, Search layout /
  Unsupported, Mode unreadable, Click search / In app, or Open ChatGPT / Then retry.
- Every explicit retry creates a new session, cancelling any late transcript from the old one.

Live ChatGPT inspection remains unavailable after the earlier Computer Use denial. The user
was asked whether the Mac app actually opens its search box. Until that observation and the
updated keypad status are known, the exact live cause and successful repair are unverified.

Release verification and installation evidence follow below.

The native fixture additionally exposed stale foreground state after activation. AppKit's
[NSRunningApplication documentation](https://developer.apple.com/documentation/appkit/nsrunningapplication)
describes state refresh at run-loop turns; the search operation now services the run loop during
bounded waits. The fixture verifies that an explicit open can activate the app, while a probe
or delayed transcript cannot. The expanded fixture passed all **17** native steps.

### Recovery update installed

Installed **0.12.7**, with Logi Plugin Service version-load confirmation. Release verification
`artifacts/desktop-tests/20260918-193435-c63136/report.html` passed **1,747 C# tests**, with
13 Windows-only skips, all script suites, 92 command/mode variants, 17 native fixture steps,
fixed-file inference and offline package verification. The subsequent installation hash check
confirmed that the installed DLL and both helper copies match that verified archive.

Receipt: `artifacts/desktop-find-chat-fix/installation.json`. Backup:
`~/.claude/claude-console/backups/vizhi-desktop/20260918-193554/`. All 28 profile/application
files, the selected Adaptive 3 profile, workflows, aliases and voice shortcut were preserved.
The pre-install report records the earlier installed version; the receipt records adoption
of the tested artifact. A fresh hardware run is required for owner acceptance.

The owner's original failure is still **unverified after the update**. Next observation:
Back → Find Chat; check whether ChatGPT opens search and report the exact status card if
Speak Query remains disabled. No real-app or microphone check has been represented as passing.

## 0.12.8: Mode unreadable

The owner reported **Speak Query / Not ready**, **Mode unreadable / Retry**, and
**Open Search / Retry** after 0.12.7. This identifies the mode check as the failing gate;
the native app layout has not been inspected. Source review found that search required a
visible mode selector even after opening a modal, and recognized fewer selector shapes than
the existing status path. A controlled modal that hides its selector reproduced exactly
`mode-unavailable` with the 0.12.7 helper; see `artifacts/desktop-find-chat-mode/before/result.json`.

Implementation plan and behavior:

1. Verify mode before opening search. Read semantic labels on popup controls and directly
   labelled pressable groups; never infer mode from ordinary message text or links.
2. Include the expected mode in the process/window identity. Within the initial operation,
   retain that verified identity when the modal hides the selector. Later operations require
   the matching previously issued field token, or the verified window origin while waiting
   for a delayed field. Visible conflicting/wrong modes always reject the operation.
3. Do not issue recovery origins when initial mode verification fails, the window changes,
   the app is backgrounded, or its accessible surface disappears. Keep the existing field,
   query, result and capture-session guards. No composer or clipboard fallback is introduced.
4. Cover modal read/focus/write/select, missing initial mode, conflicting reports, wrong
   window/field, and delayed modal recovery. Run the full release harness, install the tested
   package with a backup, and verify the service loads it. Preserve profile/configuration bytes.

Live acceptance remains pending: close the app's existing search panel, return to a ChatGPT
conversation, then use Back → Find Chat. Confirm that Speak Query enables and that a spoken
query, after pressing Speak Query again to stop recording, appears in the app search field.
This is a tested implementation fix for a reproduced failure; it is not a claim that the
owner's exact ChatGPT accessibility layout has been verified.

### Mode-check update installed

Installed **0.12.8** from `integrate/vizhi-desktop-main`. Logi Plugin Service confirmed it
loaded in 205 ms at `2026-09-18T22-46-33-297`. The installed DLL and both AX helper copies
match the tested package; all 28 profile/application files and desktop settings are unchanged.
Adaptive 3 remains selected.

- Release run: `artifacts/desktop-tests/20260918-224457-2145c2/report.html` — automation PASS.
- 1,747 C# tests passed; 13 Windows-only tests skipped; script suites, 92 command/mode
  variants, 18 native fixture steps, fixed-file inference and offline package checks passed.
- The original fixture failure and corrected run are under `artifacts/desktop-find-chat-mode/`.
- Receipt: `artifacts/desktop-find-chat-mode/installation.json`.
- Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260918-224625/`.
- Package: `VizhiDesktop_0_12_8.lplug4`.

The release run predates installation and records the previous installed fingerprint;
the receipt proves installation of that tested archive. External link checks were not run.
All 174 owner checks remain Not tested, with two Copy Answer cases Unsupported. The
mode-unreadable regression passed in the controlled fixture; the owner's live result is pending.
