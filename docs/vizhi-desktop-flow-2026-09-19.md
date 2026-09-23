# Vizhi Flow: complete tasks from the keypad

Branch: `integrate/vizhi-desktop-main`. Implementation target: 0.13.0.

## Plan

1. Ship a new Flow profile alongside the owner's retained profiles. Home: three conversations;
   All Chats / New Chat / Find Chat (Changes in Codex); Voice Draft / Send Draft / Stop.
   Actions: Mode / Approve / Deny; Voice Chat / Attach Files / More; useful contextual shortcuts.
   Workflows retains nine slots. Approval locations stay fixed across modes.
2. Promote Review Changes and Run Tests in Codex. Keep Review PR and Write Tests in More.
   Draft Reply, Rewrite, Compare, Research, Debug and Refactor collect a spoken brief.
   Review PR also collects its target when invoked from More. Explicit SPEAK footer announces
   recording. Tap to start, tap to finish, then the same key becomes Send Draft for the third tap.
3. Pin the app window, editor and mode before capture. Build the complete task brief in memory;
   never insert blank template fields. Do not overwrite existing input. Retain failed composed
   briefs for explicit retry/discard. Local dictation remains distinct from native Voice Chat.
4. Explain Mode's destination on its footer. Search already focuses the query: say Type Now,
   with Tap to Focus as recovery. Add honest completion feedback to workflow/control actions.
   Remove unsupported Copy Answer and duplicate Changes from the new default pages.
5. Update the command catalog, hardware checklist, previews, examples and documentation. Test
   real production dispatch, voice session routing, stale targets, refusal, retry and send.
   Build all products without development links, run the release harness and install with backup.

The new layout is installed and selected only after verification. Existing profiles remain for
rollback. Known stock workflow definitions are migrated; custom prompts are preserved. The live
ChatGPT/keypad checks remain owner-run; controlled fixtures do not establish real app acceptance.

## Implemented and installed

Version **0.13.0** loaded successfully on 2026-09-19 at 18:43 IST. **Vizhi Flow** is selected in
Logi's application configuration. Its first page is labelled **Conversations** and serves as Home.
The previous Adaptive 3 and Everyday profiles remain available; all 25 existing profile files
were preserved. Both stock workflow files migrated with `.before-flow` backups. Other settings
were unchanged. Custom-slot preservation, metadata preservation and repeat-load behavior are
covered by migration tests.

Release verification: **1,794 C# passes**, **13 Windows skips**, **96 command/mode variants**,
**26 native fixture steps**, package verification and fixed-file Whisper Turbo inference passed.
The verified DLL and helper hashes match the installed files. The release harness ran before
installation; installation changes its installed-state fingerprint. That report is release
verification, not live acceptance of the current keypad configuration.

- [Installation receipt](../artifacts/desktop-flow/installation.json)
- [Release test report](../artifacts/desktop-tests/20260919-184139-d3e060/report.html)
- [Rendered Actions and spoken-flow preview](../artifacts/desktop-flow/preview/flow-sheet.png)

First physical check: open the intended chat, select **Actions → Draft Reply**, speak a specific
reply brief, and tap that key to finish. Confirm the complete brief appears in the app without
sending. Review it, then tap **Send Draft** on the same key. Confirm exactly one submission.
Live app compatibility, microphone capture and physical press/hold behavior for these new
workflow keys remain owner-run. The earlier freeform Voice Draft insertion fix was already
confirmed by the owner on 0.12.13.
