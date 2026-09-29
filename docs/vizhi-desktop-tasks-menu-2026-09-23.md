# Codex Tasks menu — 23 September 2026

## Decision and workflow

Group file inspection and code work under Tools → Tasks. View Changes opens the app's Review
panel. Review Code sends the existing request to inspect the uncommitted diff for correctness,
edge cases and security, without editing files; it stops on a clean tree. Run Tests requests
existing relevant tests. These are distinct actions.

Default Tasks layout:

| Left | Middle | Right |
|---|---|---|
| View Changes | Review Code | Run Tests |
| Debug | Refactor | Explain Diff |
| Fix CI | Security | — |

Continue and Update Deps move to Tools → More, alongside optional Review PR / Write Tests and any available
app navigation. Screenshot stays on Home. ChatGPT retains its nine Prompts and Tools → Find Chat.
The old View Changes position on Codex Tools is blank; Tasks is still bottom-middle. There is
no extra page for the default Tasks layout.

Try: select a supported Codex task on Home → Tools → Tasks → View Changes → inspect the app's
Review panel → Review Code → read findings → Run Tests. View Changes leaves the keypad inside
Tasks. Review Code and Run Tests retain the established draft/send behavior: existing input is
preserved for review, and an empty composer can submit their scoped requests immediately.

## Implementation plan and compatibility

1. Build mode-specific dynamic folder contents. Refresh action names on a mode change, without
   adding polling. Pin task slots to Codex so a stale key cannot execute a ChatGPT prompt.
2. Keep View Changes on the existing guarded open-panel path, including a fresh capability check.
   Not available means no supported Review route is exposed. It does not prove the diff is empty.
   Valid empty panels remain available. Browser previews do not acquire Review through this move.
3. Rename only the untouched stock Review Changes recipe to Review Code, retaining its ID and
   prompt. Back up the source JSON; preserve custom labels, prompts, metadata and slot order.
4. Replace only the stock Tools navigation binding in the owned Vizhi Home profile. Preserve
   custom assignments, selected profiles, legacy adaptive direct commands and optional Adaptive 3.
5. Verify dispatch, mode refusal, exact migrations, profile contents, actual rendered faces and
   package structure; install with backups and verify the plugin service loaded the new version.

Task ordering is adjusted only for the standard nine IDs in their original sequence. Custom
orders retain their relative order after View Changes. Configured Continue and Update Deps retain their exact
content and slots under More. If the user replaced it with another task, every custom task remains
in Tasks, with normal host paging when the added View Changes makes ten keys. No favorite is deleted.

Backups use `.before-0.17.16` for the stock profile key and review-label JSON migration. Both
migrations are idempotent and compare the original before atomic replacement. Installation also
backs up the complete plugin, profile/application configuration and workflow files.

## Verification

Evidence: `artifacts/desktop-tasks-menu/`.

- C# suite: 2,171 passed, 13 skipped; includes both success/refusal paths for every catalog action.
- Default-profile comparison changes exactly one Tools binding. Optional Adaptive 3 is byte-identical.
- Test harness catalog covers the new ChatGPT-only search and nine Codex-only task slots.
- Native panel-opening implementation is unchanged from 0.17.15; its existing 26 fixture cases
  cover capability gating, preview exclusion, empty panels and guarded opening.
- Build, rendered keypad faces, full-suite and installation receipts are recorded in the evidence directory.

Physical keypad acceptance is separate from software tests; no live ChatGPT UI inspection is
performed by this implementation task.

## Installed result

Version 0.17.16 installed from `integrate/vizhi-desktop-main`. LogiPluginService confirmed it
loaded at 12:23:10 local time. The installed profile changed exactly its stock Tools navigation
binding; workflow JSON changed exactly `/0/Label`. Selected profile, other settings and optional
Adaptive 3 were preserved. The native helper is byte-identical to the tested 0.17.15 helper.
The installation receipt and rollback backup path are in `artifacts/desktop-tasks-menu/installation.json`.
All repository suites and package verification passed; the actual nine Tasks tile renders were
visually inspected at 90 pixels per key.

## Follow-up: Continue in More (0.17.17)

The owner approved removing Continue from the main Codex Tasks menu: its broad follow-up is
less useful as a frequent action than inspect, review, test or debug. The implementation plan
was to route that existing configured slot to More, leave its old bottom-right position unused,
preserve ChatGPT Prompts and direct assignments, run existing menu/dispatch/package checks,
and install with backups.

Tasks now has eight actions. Continue remains a Codex-only optional action under Tools → More,
with its exact configured prompt and submission behavior. Custom slot ordering and content are
preserved. No profile or workflow JSON migration is needed. The shared menu filter ensures the
task is reachable exactly once across Tasks and More, including a reordered custom Continue.

Evidence for this update is in `artifacts/desktop-continue-more/`. Physical keypad acceptance
remains separate from the software checks and confirmed plugin-service load.

0.17.17 verification: 2,171 C# tests passed, 13 skipped; all repository suites and package checks
passed. The built plugin renders eight Tasks entries. Installation was confirmed by the plugin
service at 12:36:27 local time. Profile selection, profile contents and workflow configuration
were verified unchanged; the prior plugin and settings were backed up.
