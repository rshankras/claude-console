# Conversation status repair

The owner reports that "Text Voice draft" shows Thinking and Complete in ChatGPT while its
keypad tile remains Ready. The previous implementation supported only the exact Unread static
text and an Awaiting approval text match. Running relied entirely on unnamed image counts.
Consequently explicit Thinking / Complete text, or labels on named images, could read as idle.
This is a confirmed code gap; the exact live accessibility representation is not yet verified.

Version 0.15.1 on `integrate/vizhi-desktop-main`:

1. Pass Thinking and both Complete / Unread from the OpenAI adapter to the macOS helper.
2. Read exact normalized status labels in each verified sidebar row, including accessible
   title, description, help and label attributes on text, image, group and progress nodes.
3. Prefer approval, then completion/unread, then Thinking, then the existing legacy spinner
   inference. Ignore the conversation title, buttons, composer text and neighbouring rows.
4. Preserve the stable three-key assignment, profile selection and Home/Tools layout.

The monitor already compares per-conversation state and requests a redraw even when the global
activity stays Ready. Regression coverage now exercises that distinction over a three-slot
lifecycle with a sidebar reorder. A compiled fixture drives named status images through the
actual scanner and helper JSON, separately from the pure Swift label tests.

No new monitoring of message contents or local ChatGPT history is introduced. If the current
app exposes these words only inside the open response, that does not establish the identity
and state of all three sidebar conversations. Owner verification remains necessary: on the
same visible chat, observe Thinking, Complete and a real permission request where applicable.
The existing monitor polls at roughly three seconds while idle and one second while working;
very short activity can fall between samples.

## Verification and installation

- Full automated run `20260920-090508-877ad5`: 1,883 C# tests passed, zero failed,
  13 platform skips; shell/Python/Swift regressions passed. All 30 native fixture checks
  passed, including the new status-image lifecycle. The 190 owner acceptance cases remain
  pending; fixture results do not certify the current ChatGPT accessibility layout.
- Package `VizhiDesktop_0.15.1.lplug4` verified and installed. The service recorded version
  0.15.1 loaded at 2026-09-20 09:07:31 local time. Profile contents, the selected Vizhi Home
  profile, System configuration and workflow settings were preserved.
- Installation receipt: `artifacts/desktop-status/installation.json`. Backup:
  `~/.claude/claude-console/backups/vizhi-desktop/20260920-090723-status`.
- The test report fingerprints the pre-install environment. The installation receipt connects
  its verified package/DLL/helper hashes to the installed files; installation naturally makes
  the report's original environment fingerprint stale.

## Owner retry: still Ready during "Working for 10s"

The owner reports that 0.15.1 does not fix the live case. Installation/load is confirmed;
the latest service log contains no monitor poll failures or helper timeouts. The location
of the elapsed-time label and the keypad's Send/Stop state have been requested to distinguish
a sidebar-reading failure from missing attribution of current-task activity.

A separate regression was reproduced in `DesktopMonitor.Map`: with an explicitly selected
conversation and `stopPresent=true`, global activity became Working but its tile stayed Ready.
The integration branch now maps the visible Stop/approval state to exactly one uniquely named,
app-selected conversation. Missing selection, multiple selections and duplicate titles never
assign that activity to a guessed key. An explicit permission badge outranks Stop.

This follow-up was initially source-only and is now installed as **0.15.2** (see below).
It does not yet establish that the current ChatGPT build exposes the selection needed for the owner's case.
The actual app's accessibility layout has not been inspected; automatic approval review previously
denied live access to ChatGPT. No shell/AX workaround was used to inspect it.

## Follow-up installation: 0.15.2

- Run `20260920-110135-3af4bf`: 1,888 C# tests passed, zero failed, 13 platform skips.
  Repository regressions, package checks and source-build matching passed.
- Both native fixture attempts passed the conversation-status lifecycle but failed later
  during search with `app-not-frontmost`. Their failures remain recorded (`first-attempt.json`,
  `fixture-first-attempt.log`, and the latest `run.json`/fixture log). The full current native
  fixture run is **not** a pass.
- The native helper is byte-for-byte identical to the installed 0.15.1 helper and the helper
  from the previously successful 30-step fixture run `20260920-090508-877ad5`. Installation
  reuses only that unchanged helper's native evidence; the changed C# mapping passed the
  current suite. No real-app or hardware result is inferred from either run.
- Logi recorded 0.15.2 loaded at 2026-09-20 11:05:04 local time. Profile contents, selected
  Vizhi Home profile, System configuration and workflow settings were preserved.
- Current receipt: `artifacts/desktop-status/installation.json`; previous receipt retained
  as `installation-0.15.1.json`. Backup:
  `~/.claude/claude-console/backups/vizhi-desktop/20260920-110455-status`.
- Owner retry still required for the reported Ready-during-Working symptom.

## Root cause investigation after 0.15.2 also failed

Read-only inspection of the **static installed application code**, without attaching to the
running app or reading user conversations, found two concrete mismatches. Installed app:
`26.915.31945` (build `9922`), `app.asar` asset
`webview/assets/app-initial-a498f911edeb.js`, SHA-256
`34a75db63c7137eb4caecdba1f36d631c10c7912487e532fd5e9dafb175bb9be`.

1. Shared task-row component `u7s` wraps its spinner in `role="status"` with the accessible
   label **Working** (`taskRow.working`). Vizhi passed only Thinking. The newer named status
   group does not need to add an unnamed AXImage, so the old image-count fallback can also
   return idle. The `localConversation.workingFor` timer belongs to the response divider.
2. Shared row component `_ec` uses `role="button"` and `aria-current="page"` when active.
   It does not set aria-selected. Vizhi only read AXSelected, so 0.15.2's selected-conversation
   mapping could never run on that representation. Chromium exposes the current-page marker
   through [AXARIACurrent](https://chromium.googlesource.com/chromium/src/+/HEAD/ui/accessibility/platform/ax_platform_node_cocoa.mm).

The old test fixture hardcoded AXSelected=true and used Thinking/Complete image labels. It
therefore tested our assumptions rather than the relevant installed-app semantics. Those
passing fixture tests were insufficient evidence for the owner's case.

Version 0.15.3 adds Working to the adapter's exact running labels and reads the current row's
AXARIACurrent before legacy selection. Explicit false/unsupported current values cannot borrow
an ancestor's selection. Draft target pinning shares this reader to remain consistent.

The fixture now has AXSelected=false, exposes AXARIACurrent through the actual accessibility
protocol and tests Working as an AXGroup, including a non-current background row. Tests also
cover page/true/false, unknown current values and legacy fallback. The adapter regression was
observed failing before adding Working. Final live acceptance still requires an owner retry;
static interface inspection proves the selector mismatch, not the current live tree's output.

### 0.15.3 verification and installation

- Run `20260920-112110-7613e8` completed with automation PASS: 1,888 C# tests passed,
  zero failed, 13 platform skips; repository regressions, package/source-build checks and
  fixed-audio inference passed. All **31 native fixture checks passed** without a retry.
- The added native check read Working as running and AXARIACurrent page/true as selected,
  false as not selected, with AXSelected false throughout. Existing draft-target protection
  and search/capture tests also passed using this corrected current-conversation representation.
- Installed on `integrate/vizhi-desktop-main`; Logi recorded 0.15.3 loaded at 2026-09-20
  11:23:14 local time. Both installed DLL and shared helper hashes match the verified package.
  Profiles, selected Home layout and workflow settings were preserved.
- Receipt: `artifacts/desktop-status/installation.json`; backup:
  `~/.claude/claude-console/backups/vizhi-desktop/20260920-112305-status`.
- Real ChatGPT/keypad acceptance remains owner-run. In particular, Complete means an explicit
  Complete/Unread sidebar signal; absence of Stop alone does not prove successful completion.
