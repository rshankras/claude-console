# Complete keypad workflows — implementation plan

Branch: `integrate/vizhi-desktop-main`. Release: 0.17.0.

Installed and loaded on 2026-09-21. The plugin service reported a 216 ms load. The
selected Vizhi Home profile was preserved and its five unchanged Tools bindings updated.

- [x] Add an on-demand Downloads picker inside Attach Files: readable recent filenames,
  explicit multi-selection, Attach, and Browse fallback. Pin the chat before selection;
  retain selection on refusal and never silently retry an uncertain paste.
- [x] Extend Paste into Chat to clipboard files and images, with native attachment
  acknowledgment, draft preservation, and clipboard restoration.
- [x] Make saved prompts compose with existing text and attachments. Source material
  produces a reviewable draft; clearly scoped empty-composer requests can stay direct.
  Preserve custom prompts while migrating unchanged defaults and exposing scope/action.
- [x] Simplify search to speech plus query/results and a focus/retry fallback; distinguish
  empty results from unavailable search. Unify Chats naming and explain visible scope.
- [x] Keep Home fixed. Tools uses Mode/Approve/Deny on top (approval keys blank in ChatGPT),
  Attach Files/Paste into Chat/Screenshot in the middle, Copy Reply/Prompts or Tasks/More
  below. This preserves Codex approval positions and common capture positions. Codex
  Review Changes and Run Tests are reached through Tasks, one additional tap from Tools.
- [x] Remove Clear Added and Return from default menus; retain explicit source-capture
  and legacy bindings. Keep occasional app navigation optional and filter unavailable
  sections. Preserve observed activity and honest request/voice-state feedback.
- [x] Run the full C# and script suites; verify AppKit file/image/append/copy/search and
  Chromium file/image workflows; render revised key faces and generate both profiles.
- [x] Finish the final Chromium Copy Reply check after the Mac is unlocked. Passed with
  the 0.17.1 helper during the [Screenshot → Explain fix](vizhi-desktop-image-explain-fix-2026-09-21.md).
- [x] Install plugin/profile with rollback backups, settings-migration checks, and load verification.

The review's conditional project/PR selectors are not promoted: they remain optional app
navigation until frequent use justifies a dedicated picker. Physical keypad usability and
live ChatGPT/Codex acceptance are recorded separately from controlled automation checks.
No automated test sends mail, submits real work, changes permissions, or edits a real
workspace through ChatGPT/Codex.

## Verification

- Full suite: 2,018 C# tests passed, 13 skipped, zero failures; script suites passed.
- Native AppKit fixtures: screenshot (7 steps), multi-file/clipboard (7), append (8),
  and full app workflow (37) passed with the packaged helper.
- Chromium file and image attachment fixtures passed with the packaged helper.
- The final Chromium Copy Reply run reached a locked-screen limitation; no false pass
  was recorded. Earlier Chromium Copy Reply coverage and the final AppKit check passed.
  The native scanning functions, response selectors, and Copy Reply action are identical
  to the pre-change source snapshot. The additional locked-screen rerun does not block
  this installation; it remains explicitly pending in the receipt.
- Component previews were rendered from the built DLL and visually inspected. These
  establish rendering output, not physical-keypad readability or real-app acceptance.
- Migration preview used copies of the installed configurations; original live files
  were unchanged during development. Only unchanged stock slots gain new semantics.

Evidence: `artifacts/desktop-workflows/`. [Installation receipt](../artifacts/desktop-workflows/installation.json)
records the exact package/helper hashes, selected profile, five stock binding updates,
configuration backups, and the service's version-load message. All eight installation
checks passed, including both workflow migrations and their original-file backups.

Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260921-153002-complete-workflows`.
It contains the previous plugin/helper, application profiles, and original workflow settings.

## First hardware workflow to try

Open the intended ChatGPT conversation. Go to **Tools → Attach Files**, select two recent
Downloads, and press **Attach 2**. The picker returns to Tools after confirmation. Open
**Prompts → Compare**, speak the comparison goal, and tap again to finish. Review the
files and drafted instruction in the app, then press **Send Draft**. After the response
finishes, **Tools → Copy Reply** copies the answer without selecting it.

Also check **Paste into Chat → Summarize** and **Screenshot → Explain**: each should retain
the source, append the relevant instruction, and wait for Send. These physical-keypad
and live-app observations remain separate from the automated fixture results above.
