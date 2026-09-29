# View Changes: owner's live failure

After 0.17.10 was installed, the owner reported Opening → Couldn't open. The nine controlled Chromium scenarios had passed, but they do not prove the real application's accessibility structure matches the fixture. No live application inspection was performed.

The C# caller discarded the helper's failure code and replaced all failures with panel-unconfirmed. Therefore the previous operational log cannot distinguish an unavailable opener, changed target, obstructing menu, or unsuccessful panel confirmation.

Version 0.17.11 preserves only allowlisted failure codes and adds opt-in View Changes diagnostics. An explicit press produces requested followed by opened/already-open or a fixed failure code. Unknown/raw helper output is replaced with unknown-error or unexpected-reply. No conversation text, titles, paths, clipboard data or AX dumps are logged. No additional helper invocation or background polling is added. A broken diagnostic sink cannot change the action's result.

Enable file: `~/.claude/claude-console/desktop-changes-trace-until`, containing a UTC timestamp ending in Z no more than 30 minutes ahead. Recording stops at expiry or 80 records per service instance. Delete the enable file when diagnosis is finished.

The native helper, selectors, profile bindings and panel-opening behaviour remain identical to 0.17.10. This release diagnoses the failure; it does not claim to fix its underlying cause. Next evidence required: one owner tap while diagnostics are enabled, plus whether the desktop panel appeared.
