# Screenshot → Explain insertion fix

Branch: `integrate/vizhi-desktop-main`. Patch: 0.17.1.

Installed and loaded on 2026-09-21; the service reported a 225 ms load. Profiles and
workflow settings were verified unchanged. The owner subsequently reported that this version
still fails. See the [0.17.3 rich-text correction](vizhi-desktop-attachment-caret-2026-09-22.md).

## Owner report and reproduction

Screenshot attachment worked on the physical keypad. Prompts → Explain subsequently
reported Insert Failed. The installed Explain definition already contained the correct
source-aware instruction. Existing logs did not retain the native append refusal code.

A regression against the exact 0.17.0 helper reproduced `composer-selection-changed`:
attach an image, leave the text empty, then append the Explain instruction. The fixture
exposes the empty editor as `Work with ChatGPT\n` while the image enables Send. The old
placeholder guard required Send to be unavailable, so it treated the placeholder as
18 real characters and tried to place the caret beyond the actual empty editor.

Earlier attachment tests stopped after successful capture; the instruction-after-attachment
test used a nonempty draft. They missed this combination. This fixture reproduction is
consistent with the owner report, but does not establish the live app's precise AX values.

## Change

Append target preparation and insertion can now use independent editable-range evidence
when the adapter's placeholder, description, and zero-length caret at zero all match.
A zero-length range must read successfully, and the first character must either be
explicitly empty or rejected as out of bounds. Unsupported or failed reads do not count
as proof. A real draft spelling `Work with ChatGPT` contains a first character and remains
ordinary draft text.

The additional check is limited to append. Ordinary overwrite/draft-write guards retain
their previous behavior. Target, window, mode, focus, user-edit, clipboard, and send guards
remain in place. No profile or workflow-setting change is needed.

## Verification

- Installed 0.17.0 helper: new Screenshot → Explain regression failed as expected.
- Fixed helper: screenshot-only instruction insertion passed, attachments remained,
  nothing was submitted, and retries did not duplicate the instruction.
- The same fixture preserved a literal draft equal to the placeholder text.
- Pure tests cover explicit empty ranges, literal text, selection, and unsupported reads.
- Full suite: 2,018 C# tests passed, 13 skipped; script suites passed.
- Current-helper AppKit fixtures passed: screenshot/instruction (8 steps), append (8),
  files (7), and full app workflow (37). An earlier append run lost focus and was refused;
  a fresh run passed without altering the focus guard.
- Current-helper Chromium image/instruction and Copy Reply fixtures passed.
- Package helper bytes matched all six fixture copies. The diagnostic probe was excluded.
- Installation verified 0.17.1 loaded, the runtime helper matched the package, and profile,
  selected-profile, system-profile, and workflow configuration contents were preserved.

Evidence lives under `artifacts/desktop-image-explain/`. Diagnostic range probing targeted
only the disposable fixture and is not included in the packaged helper. The owner later confirmed live failure; the controlled range model did not cover a generated
CSS placeholder returned by AXStringForRange. The linked 0.17.3 notes supersede this fix.

[Installation receipt](../artifacts/desktop-image-explain/installation.json).
Rollback backup: `~/.claude/claude-console/backups/vizhi-desktop/20260921-230856-image-explain`.
