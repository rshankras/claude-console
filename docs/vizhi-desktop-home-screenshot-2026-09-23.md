# Screenshot on Vizhi Home

## Decision and workflow

Screenshot is a frequent way to add context in ChatGPT and Codex. Put it on Home in the same physical position in both modes. Keep Find Chat and View Changes available on Tools. No additional page or command is needed.

The default Vizhi Home profile now uses:

| Page | Top row | Middle row | Bottom row |
| --- | --- | --- | --- |
| Home — ChatGPT | Recent chat 1 · 2 · 3 | Chats · New Chat · Screenshot | Dictate · Send/Stop · Voice Chat |
| Home — Codex | Recent task 1 · 2 · 3 | Chats · New Task · Screenshot | Dictate · Send/Stop · Voice Chat |
| Tools — ChatGPT | Mode · — · — | Attach Files · Paste into Chat · Find Chat | Copy Reply · Prompts · More |
| Tools — Codex | Mode · Approve · Deny | Attach Files · Paste into Chat · View Changes | Copy Reply · Tasks · More |

Use Home → Screenshot, select an area, and wait for Attached. The existing capture command inserts the image into the current draft. Add the request by typing, Dictate, or a prompt, review it, and explicitly press Send. Escape cancels area capture. There is no automatic submission.

View Changes opens the app's code review panel and leaves the keypad on Tools; Review Changes under Tasks is a separate prompt asking the agent to review code. ChatGPT Find Chat opens the existing keypad search workflow.

## Implementation and migration

Version 0.17.14 on `integrate/vizhi-desktop-main` changes the generated default profile and preview metadata. It reuses `DesktopCaptureCommand/screenshot` and `DesktopNavigateCommand/find`; capture and attachment behavior are unchanged.

`DesktopHomeScreenshotMigration` swaps control 5 (middle-right) between the uniquely named Home and Tools pages in the known Vizhi Home profile. Both bindings must still be stock. If either is customized, it skips the pair. A `.before-0.17.14` profile backup is saved once; page order, other controls, settings and selected profile are preserved. The optional Adaptive 3 layout is unchanged. Existing legacy navigation migration runs first. The swap is idempotent and applies even when the installed profile revision already matches.

The release includes the previously prepared Open Review shortcut fallback and Chromium dialog guard correction. That helper passed all 21 native scenarios after the desktop became available; these checks use disposable fixture apps, not the owner's ChatGPT conversations.

## Verification

Evidence: `artifacts/desktop-home-screenshot/`.

- Full repository suites passed: 2,113 C# tests passed, 13 skipped; shell, AX selector, shortcut, package and harness checks passed.
- Migration cases cover stock and older bindings, custom keys on either page, a different owner, reordered pages, original backup retention, repeat updates and profile selection.
- All 21 native View Changes scenarios passed with the helper bundled in the package, including no opener, already-open panels, dialog refusal, wrong mode, disabled/ambiguous controls, no confirmation, mode changes and formatted counts.
- Release package verification passed. The optional profile is byte-for-byte unchanged. Generated binding and preview metadata agree.
- Rendered Screenshot and View Changes faces were inspected at native key size.
- Installation runs the built product's migration on a copy of the installed profile first, requires exactly the two intended binding changes, then applies it with backup and rollback while the service is stopped. `installation.json` records final load, hashes and preservation checks when installation completes.

Physical keypad acceptance: confirm Screenshot is middle-right on Home in both modes; select a region, verify it attaches without sending, then use Tools to find Find Chat or View Changes.
