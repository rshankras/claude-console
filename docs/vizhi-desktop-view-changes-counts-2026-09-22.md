# View Changes: formatted count matching

## Evidence and scope

The owner's 2026-09-22 21:35:04 tap recorded `requested`, followed at 21:35:05 by `panel-opener-unavailable`. The helper refused before pressing a button. This rules out post-click panel confirmation as the failure stage, but that code does not distinguish a missing, disabled or ambiguous opener.

The installed app's static Changes summary implementation uses its number formatter for added/removed line counts, rendered in separate spans. Version 0.17.10/0.17.11's matcher allowed only plain digits and required spaces before each sign. It could not recognize grouped counts or compact accessible names. No live app content/AX inspection was performed.

A disposable Chromium fixture with separate label/count spans reproduces the old helper's refusal with zero clicks. The corrected helper is tested against the same HTML. This proves the formatting defect and its fix; it does not prove the exact label on the owner's live screen.

## Version 0.17.12

The exact destination names Changes and This branch may be followed by up to two signed integer counts. Comma, dot, Arabic grouping separator and whitespace groupings are supported, including Indian grouping. AX whitespace is normalized; adjacent label/count spans may have no intervening spaces. Arbitrary words, file names and malformed suffixes still do not match.

The existing guards remain: one enabled button, no nested action controls, pinned window and exposed selected conversation, correct app mode, no dialog/menu, one panel marker and positive confirmation. Repeated taps leave an already-visible panel open. No keyboard fallback or background work is added.

Opener diagnostics now distinguish `panel-opener-missing` and `panel-opener-multiple`. Diagnostics remain opt-in, fixed-code-only, bounded and expiring. Profile bindings, voice settings, prompts and key positions do not change.

## Verification

Evidence: `artifacts/desktop-view-changes-counts/`.

- `before-reproduced/fixture-result.json`: previous native helper refused the formatted summary, zero clicks.
- Native fixture cases: formatted, Indian-grouped and compact counts; plain counts; already open; no acknowledgement; ambiguous opener; wrong mode; file-only controls; mode change after click; disabled opener; duplicate panel evidence.
- The fixture intentionally toggles the panel, so an erroneous repeated press closes it and fails the test.
- Pure Swift selector tests also reject malformed numeric suffixes, additional words, third counts, disabled controls and nested sidebar actions.
- Full repository test/package checks and installation receipt record the final verified source and binaries.

Owner acceptance: tap View Changes in Codex, then tap again. Expect Opened and the panel to remain visible, with the keypad on Home. If it fails, the specific opener/refusal code is available from the temporary diagnostic.
