# Vizhi for Codex CLI 1.6.1 — Mac acceptance results

Date: 14 September 2026. Source: `82d0aea`, PR #100 integration branch.
Verdict: automated checks and most macOS physical keypad workflows passed. Manual results and
remaining gaps are recorded below. This is not an all-features or release sign-off.

## Installation and test evidence

| Check | Result | Evidence / limit |
| --- | --- | --- |
| Codex CLI availability | PASS | Existing codex-cli 0.153.4, logged in using ChatGPT. No reinstall needed. |
| Automated suite | PASS | 1,092 C# passed, 11 Windows-only skipped; 54 bridge-script and 27 Codex-hook checks passed, remaining shell suites/canaries passed. |
| Repository package verifier | PASS | Correct 1.6.1 metadata and 1.6.1.0 assembly, two Windows helpers, five public links, extracted signatures. |
| LogiPluginTool verification | PASS | Package unpacking and package checks successful. |
| Installation | PASS with initial failure | CLI installer initially returned “Plugin installation cannot start.” Package was subsequently opened through Finder; installed plugin and host load were verified. Do not count the initial attempt as a clean pass. |
| Installed binaries | PASS | DLL, hook, and shared-toolkit hashes match the candidate. |
| Logi host load | PASS | 15 dynamic action classes loaded. Vizhi 1.6.1 loaded in 291 ms at 08:18:06; bridge reported active. |
| Options+ action list | PASS | “Vizhi for Codex Actions” and its action groups visible. |
| Mac layout import | PASS | Imported VizhiCodex-Keypad.lp5, selected Terminal, Options+ confirmed successful import. |
| Five-page rendering | PASS | Session/voice/answers; Codex controls; prompts; terminal controls; Git pages inspected in Options+. Icons and expected labels visible. A live session and context gauge rendered. This does not prove execution of those keys. |
| CLI request/response | PASS with warning | Read-only noninteractive request returned exactly VIZHI_CLI_SMOKE_OK, exit 0. No tools requested. Does not test interactive approval menus or keypad injection. |
| Voice helper code signature | PASS | Strict deep signature verification on installed app. |
| Gatekeeper | PASS | Installed helper accepted by spctl. |
| Stapled notarization ticket | PASS | stapler validate succeeded on installed helper. |
| Offline speech engine | PASS | Installed whisper-cli transcribed generated 16 kHz mono audio as “the quick brown fox jumps over the lazy dog.” Uses existing model; no microphone tested. |
| Claude settings preservation | PASS | settings.json hash unchanged across installation. |
| Windows execution | NOT RUN | This session is on macOS; Windows tests and device acceptance remain separate. |
| Full performance regression | NOT RUN | 291 ms host load is an observation, not a CPU/latency/repaint benchmark. |

Candidate: `artifacts/VizhiCodex_1.6.1-integration-preview.lplug4` (17,385,315 bytes).
Package SHA-256: `c006d0a90b725bf6190c4f6eee73787d9274b3d3ae5dc9dd3d2140876d50ebcf`.
Installed DLL SHA-256: `2b82d7e9c931ad5c433f68c375283c2f45c8d45aaadd55ea4d08c03ff9439e86`.
Evidence logs and pre-install hook backups: `artifacts/acceptance-2026-09-14/` (local only).
The installed package remains the integration preview; no production source changes were made.

## Findings and execution limits

1. Codex 0.153.4 emits a JSONL error-type diagnostic: “clamping SessionEnd hook timeout to 3s”.
   The hook configuration currently requests 5 seconds for every event. The smoke request still
   completed successfully. Aligning this timeout is follow-up work; it was not silently changed
   in the installed artifact, and this diagnostic is not evidence that a hook failed to execute.
2. The first CLI installer attempt failed at service communication; later installation/load
   succeeded. A clean-machine install should still be part of release validation.
3. Computer Use explicitly refused access to the Terminal application. Interactive terminal
   control was not attempted through another automation mechanism. Therefore actual picker,
   tab-focus, approval-menu and physical-injection results require the manual checks below.
4. Existing microphone/model permissions and cached model were present. First-download UX,
   fresh microphone consent and clean-machine runtime installation have not been exercised.
5. The known manually renamed Windows Terminal tab limitation (#88) still applies on Windows.

## Manual acceptance plan

The macOS profile is already imported and page 1 is selected in Options+.
Use disposable projects, not active work, so prompt/Git actions cannot change important files.
Two initialized local Git projects are ready (no remotes):

```sh
cd /tmp/vizhi-cli-acceptance-20260914/alpha
codex
```

Open a second Terminal tab and run:

```sh
cd /tmp/vizhi-cli-acceptance-20260914/beta
codex
```

Run `/hooks` in these test sessions and review/trust only the expected Vizhi definitions if
requested. Do not bypass hook trust. Check no hook-failed message appears on starting a session,
sending a prompt, completing a tool, completing a turn, or exiting.

Record PASS / FAIL / NOT TESTED for each row, with the approximate time and a short observation.
A result is not a pass merely because a key animates: check the terminal outcome too.

| ID | Test | Expected outcome |
| --- | --- | --- |
| M1 | Session keys with alpha and beta open; switch several times, pin/unpin, then close one test session | Correct folder labels; selected terminal matches the key; subsequent input never goes to the other session; closed session eventually clears. |
| M2 | Send a harmless prompt in each session, then interrupt one with Esc during a long answer | Thinking/completion transitions reflect the correct session; interrupted turn does not remain permanently Thinking. |
| M3 | Trigger an actual harmless approval menu in a disposable test session; press physical No. Trigger another and press physical Yes | Indicator belongs to the pending session; No does not execute the command; Yes executes only the intended command; indicators clear after a successful answer. If no approval menu appears, mark NOT TESTED. |
| M4 | Press Yes/No with no pending approval | No stray “yes/no” message or unintended submission. |
| M5 | Dictate “Reply with voice test complete”; stop recording | Listening indicator corresponds to recording; transcript reaches the selected session and is submitted once. |
| M6 | Voice Draft; say a different short sentence and stop | Text reaches the selected composer but is NOT submitted. Clear that test draft afterward. |
| M7 | Cancel during startup if that phase is observable; cancel/restart recording and try another voice key | No cancelled transcript arrives; no overlapping recorder; next capture works. Mark startup cancellation NOT TESTED if startup is too fast to observe. |
| M8 | Screenshot a harmless region; separately cancel the picker | Captured image/path reaches only the selected conversation; cancelling sends nothing. |
| M9 | Model, Plan, Skills, Agent, Fork, Resume, Review, Compact, Context (page 2) | Each control opens/performs its intended Codex operation; Up/Down/Enter/Esc navigate safely. Fork/Resume choose only disposable sessions. Context is plausible or unavailable, never another session's value. |
| M10 | Every prompt key on page 3 | Correct prompt reaches the chosen session once: Explore, Explain, Document, Optimize, Refactor, Fix Bug, Code Audit, Write Tests, Security. Use only the disposable project. |
| M11 | Go to Project, New Tab, New Codex, Previous/Next Tab, Up/Enter/Down, Exit (page 4) | Correct project/tab/session is opened or targeted; Go to Project failure is visible for a nonexistent name; Exit closes only the disposable session. |
| M12 | Git Status, Diff, Log, Commit, Push, Create PR (page 5) | Correct instruction is delivered to the selected test repository. It has no remote: Push/PR should report that or ask for setup. Do not supply a production remote just for this test. This verifies key delivery, not a successful remote publish. |
| M13 | Switch away from Terminal and back; inspect all five pages on the physical device | Correct profile activates; icons/text readable; no missing-action triangles, blank configured keys or distracting redraws. |
| M14 | Quit/reopen Options+ and restart the test CLI sessions | Installed plugin and imported profile persist, hooks remain usable, no duplicate plugin or repeated setup loop. |

Also required for release: Windows strict suite and corresponding Windows physical checks;
fresh-user Mac permission/model-download checks; upgrade/uninstall/reinstall preservation checks.
Those are not covered by today's installation on this development Mac.

Suggested reply format: `M1 PASS; M2 PASS; M3 FAIL — No confirmed the menu at 09:10; ...`.
Stop approval testing immediately if No executes a command or input lands in the wrong session.

CLI setup reference checked: https://learn.chatgpt.com/docs/codex/cli . Installed version and login
status above come from the actual local executable, not from the documentation.

## Fork verification — 14 September, 15:38–15:42 IST

Fork creation: PASS for both alpha and beta, verified from plugin press logs and Codex session
metadata. These interactive sessions report CLI **0.154.0**, newer than the morning 0.153.4 smoke.
The Fork key maps to `/fork`; it does not launch another Terminal window.

- Alpha: physical press logged 15:38:11.310; child `01a09f63-7e47-72f3-b258-70e9d62f4a64`
  created 15:38:12.928, with `forked_from_id` pointing to
  `01a09f4e-b10e-7853-9c0a-3a11ebfa0793` and `history_base` referencing its conversation.
  Further presses at 15:39:20, 15:39:44 and 15:41:49 created successive forks.
- Beta: physical press logged 15:42:20.789; child `01a09f67-46c7-72f1-bc9f-317685496cc6`
  created 15:42:20.857, with `forked_from_id` and `history_base` pointing to
  `01a09f39-97f5-7442-85f3-387a0bcf36ba`.

The token summary/resume instruction names the conversation being left, and is not evidence of
failure: separate child records prove forks were created while the user stayed in the same tab.

Separate tracking concern: at inspection, the keypad IPC for ttys002 and ttys003 still contained
SessionEnd for the parent conversations. Child creation is proven, but immediate keypad state
refresh after forking is not. Check whether a first prompt in each fork refreshes the state;
this may be related to the already observed missing startup project labels.

## Consolidated physical results after the guided session

Evidence: owner confirmations, supplied screenshots, plugin logs, and selected Codex rollout records.
A key-delivery pass does not establish successful completion of the underlying agent task.

| Workflow | Result and evidence |
| --- | --- |
| Startup session labels | ISSUE: both initially showed Codex/Complete; alpha/beta appeared after the first prompt. |
| Session selection and targeting | PASS: owner confirmed switching and prompts reached the correct session. |
| Thinking → Complete | PASS: owner confirmed both forked sessions transitioned. |
| Dictate / Draft | PASS by owner: Dictate submitted once; Draft remained in the selected composer. |
| Screenshot / Esc cancellation | PASS by owner: image attached, Enter submitted; second capture cancelled. |
| Model and picker navigation | PASS by owner for Up/Down/Esc. |
| Plan | Entry PASS; repeat press did not leave Plan. Requested toggle enhancement remains open. |
| Skills | Available skills and enable/disable screens navigated; no setting change verified. |
| Fork | Creation PASS by metadata (see above); immediate state refresh remains an investigation. |
| Approval Yes / No | PASS: log 15:52:08 Yes, 15:53:35 No; accepted file existed, rejected file absent. Owner saw physical Allow. Amber indicator visible only while pressing needs visual investigation. |
| No pending approval | Logs confirm No ignored at 17:09:52 and Yes ignored twice at 19:02; no-pending guard exercised. |
| Review | PASS: owner selected uncommitted changes and confirmed result. |
| Compact | PASS: 16:05:56 press; alpha compacted event at 16:06:22. |
| Context | PASS: 16:07:18 /status; screenshot showed alpha, correct session, 7.05K/258K used, CLI displaying 100% left. Raw-ratio estimate was approximately 97% left; do not claim those displayed percentages agree. |
| Prev/Next Tab | PASS by owner, supported by action logs. |
| New Tab | PASS by owner and 18:07:49 log. |
| New Codex / Exit / restart | PASS by owner; action logs support presses. Initial New Codex process/session startup was not independently confirmed. Exit tested in beta; restart confirmed by owner. |
| Go to Project | claude-console worked by owner; AlertWala did not. Log captured alert walla but searched demo-only configured roots plus active sessions. Shared discovery is not missing; local override and feedback need attention. |
| Git Status / Diff / Log | PASS by logs and alpha responses: clean tree, empty diff, initial test commit. |
| Git Commit / Push / PR | Delivery and expected unavailable cases PASS: nothing to commit; no remote; PR requested repository/target. Actual successful commit/push/PR remain untested. |
| Prompt keys | All nine delivered. Explain verified earlier. Explore first reached this conversation while alpha was unpinned, then reached alpha at 18:54:28 and completed 18:54:42. Other seven reached alpha; several Esc presses interrupted the rapid sequence, so separate completed outputs are NOT verified. |
| Resume | Picker and reopening saved beta conversation PASS by owner, including beta directory answer. |
| Agent | Owner saw server started; screenshot proves command center opened. Regular keyboard selected tasks; keypad navigation worked after beta was unpinned. New background task creation/completion not tested. |
| Restart/persistence | Physical Options+/plugin restart not tested, despite existing automated selection-persistence coverage. |
| Windows | Pending on owner's Windows machine. |

The absence of a trust prompt alone is not a failure: the bridge was active and approvals were seen.
Fresh trust/permissions remain untested. No requested behavioral fixes or profile changes were applied
during acceptance. See `vizhi-follow-up-plan-2026-09-14.md` for the consolidated next work.

## Follow-up source changes

A new acceptance-fixes preview has now been built and validated automatically. The original
installed candidate and this report's physical results remain unchanged. See the implementation
checkpoint in `vizhi-follow-up-plan-2026-09-14.md` for fixes, hashes, and required retesting.
