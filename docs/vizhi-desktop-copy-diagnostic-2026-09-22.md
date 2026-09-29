# Copy Reply after a Codex diff — 2026-09-22

Branch: `integrate/vizhi-desktop-main`. Diagnostic version: **0.17.7**.

The owner reports that Copy Reply fails after generating a diff explanation and returning
from the keypad task menu. The precise native refusal is not yet known. Version 0.17.6 does
not retain copy outcomes after the earlier log cleanup. Its installed helper passed the
normal and wrapped Chromium copy fixtures, 77 focused unit tests and the selector regressions
in `artifacts/desktop-copy-recheck/`. These are isolated tests, not live Codex acceptance.

## Temporary diagnosis

Tracing is **off by default**. A developer can place an ISO-8601 UTC expiration, no more than
30 minutes ahead, in `~/.claude/claude-console/desktop-copy-trace-until`. While enabled, only
explicit Copy Reply taps emit `DesktopCopyReply: result=<fixed-code>` to the existing plugin
log. No polling trace, chat text, clipboard contents, AX dump, titles or arbitrary errors are
recorded. Unknown codes become `unknown-error`. The trace expires automatically and is capped
at 80 records per desktop service instance. Deleting the enable file disables it immediately.

`key-pressed` confirms the configured key reached its handler; `requested` confirms the native
copy command was called. `copied` confirms fresh response text was returned and retained. A
specific refusal identifies the failed guard. `command-busy`, `action-unavailable`, `voice-busy`
and `context-busy` distinguish dispatch refusal from native selector failures. Diagnostic
failures cannot change the copy operation's result.

This release preserves the native helper, copy target checks, clipboard acknowledgement and
keypad layout. It is for diagnosis, not a claimed fix. Next: enable tracing after installation,
have the owner tap Copy Reply once on the completed Codex answer, read only the fixed outcome
codes, and reproduce that condition in a controlled fixture before changing the selector.

## Validation

Tests cover absent, malformed, expired and overlong enabling files; concurrent record limits;
unknown-code sanitization; successful, refused and exceptional copies; busy guards; and
diagnostic failures. Full suite and installation evidence are under
`artifacts/desktop-copy-diagnostic/`.
