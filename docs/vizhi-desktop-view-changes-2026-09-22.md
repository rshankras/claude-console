# View Changes on Codex Home

## Intended workflow

Codex finishes → tap **View Changes** → inspect the diff on the computer → dictate a follow-up or choose **Review Changes** to ask for an analysis.

The keypad stays on Home. It briefly shows Opening, then Opened only after the native review panel is confirmed, or Couldn't open if the operation cannot be confirmed. Another tap leaves an already-visible panel open. ChatGPT's same key remains Find Chat and opens the existing search controls.

## Implementation

Version 0.17.10 on `integrate/vizhi-desktop-main` replaces the stock dynamic-folder binding with `DesktopNavigateCommand/find`. The command opens a keypad folder only for ChatGPT, using the SDK's generic dynamic-folder action. Mode changes between rendering and execution are refused. The search folder also refuses Codex if the mode changes before activation.

The Mac helper's bounded `open-panel` operation pins the foreground window and selected conversation when exposed. It checks the mode and refuses dialogs, menus, missing/disabled/ambiguous openers, and ambiguous panel evidence. Success requires a single native Show files or Hide files review-header control. The helper does not press those controls. It returns immediately when that panel is already visible; otherwise it presses the Changes summary row once and waits up to 1.2 seconds for confirmation. No retries or new background polling.

Installed static app resources distinguish the summary row (`onOpenReviewTab`) from Toggle file diff (a per-file disclosure) and Show/Hide files (the review header's file-list toggle). The latter controls were unsafe opening fallbacks and have been removed. The feature-flagged This branch summary row is also recognized, with optional numeric line counts. No text scraping, keyboard injection, composer changes or submissions occur.

Legacy Show Diff, adaptive output and primary context commands use the same guarded operation. Review Changes remains a separate workflow. Platforms without an implementation report Unavailable.

Both packaged layouts retain their profile IDs and physical key positions. A backed-up migration replaces only the exact old stock binding at Home position 5 in the two known Vizhi-owned profiles. Other key assignments, user settings and selected profile are preserved. Customized bindings are skipped; legacy Find Chat folders remain searchable in ChatGPT.

## Validation

Automated command routing, refusal paths, positive native confirmation, profile migration/backup/idempotence and custom-key preservation are covered. Native tests use only a disposable Chromium fixture: opening, repeated tap, already open, no acknowledgement, ambiguous opener, wrong mode, file-only controls, mode change after press, disabled opener and duplicate panel evidence. The fixture deliberately implements its opener as a toggle so an erroneous second press would close it.

Real app/hardware acceptance remains owner-run: in Codex tap View Changes twice; the desktop panel should stay open and the keypad should stay on Home. Switch to ChatGPT and confirm Find Chat still opens the search page. Live app inspection was not performed.
