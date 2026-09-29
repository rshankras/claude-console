> Update 2026-09-20: v0.15.6 replaces selected-text Copy Reply with [one-tap latest-answer copying](vizhi-desktop-copy-reply-2026-09-20.md). The original decisions below are historical.

# Complete the customer-reply workflow

Branch: integrate/vizhi-desktop-main. Target release: 0.14.0.

1. Add Use Selection, Use Clipboard and Screenshot with explicit capture, in-memory source staging,
   source counts, and a Clear Sources action. Failed/new captures must not silently reuse old clipboard data.
2. Combine staged source material with a spoken workflow brief. Keep the existing draft review/send
   step, original destination checks and retained-draft recovery. A capture is not a sent message.
3. Add Copy Reply (user-selected answer text), Return to App and Paste Reply. Copying an arbitrary
   last Copy button is unsupported. Paste uses the retained reply, pins the current empty editor in
   the original source application, and never sends an email.
4. Add a Context page to a new Flow profile and an Ask ChatGPT folder for assignment anywhere.
   Install an additional Ask ChatGPT page on the selected System profile, preserving existing keys.
   Application-specific profiles can override System; the folder can be assigned there as well.
5. Test capture and delivery with inert automation and the controlled fixture only. Use an isolated
   named pasteboard in fixture tests. Do not inspect real ChatGPT, read the user's clipboard, or
   capture the real screen during development verification. Package and install after checks pass.

Screenshot delivery uses an interactive region capture and a guarded image-file paste, with
attachment-name readback. An unconfirmed attachment is reported explicitly; repeated automatic
pastes are refused. Source text and clipboard contents never enter operational logs. Appshots
remain an alternative to investigate separately because their configured destination belongs to
the app; this implementation keeps the existing workflow's pinned composer destination.

## Delivered on 19 September 2026

Implemented and installed **0.14.0** from `integrate/vizhi-desktop-main`.
**Vizhi Flow 2** is selected; the three existing Flow pages retain their positions and Context
is page four. An additional **Ask ChatGPT** page was added to the selected System profile.
All 30 previous desktop profile files and the workflow settings were preserved.

| Context row | Left | Middle | Right |
|---|---|---|---|
| Capture | Use Selection | Use Clipboard | Screenshot |
| Draft | Draft Reply | Voice Draft | Send Draft |
| Return | Copy Reply | Return to App | Paste Reply |

Customer-reply path: highlight an email → Use Selection → Draft Reply → speak and tap to finish
→ review the assembled source/instruction draft → Send Draft → select the desired answer text
→ Copy Reply → Return to App → click the empty reply body → Paste Reply → review/send in Mail.
Screen regions use Screenshot instead of selection; the next workflow attempts the attachment.
Clear Sources is in **More → Ask ChatGPT**. It clears in-memory staging only. Screenshots remain
under `~/.claude/claude-console/desktop-captures/` until removed; app text and attachments remain.

Automated evidence: [release run](../artifacts/desktop-tests/20260919-193515-499d0a/report.html),
[installation receipt](../artifacts/desktop-context/installation.json).
The full repository suite passed: **1,844 C# tests**, 13 platform-specific skips, plus the shell,
Swift and Python checks. All **114 command/mode variants**, **29 native fixture steps**, package
validation and fixed-file higher-accuracy speech inference passed. No microphone or live ChatGPT
was used. The 208 owner cases include bindings in both layouts, optional commands and regression
workflows; none were marked as hardware passes.

The run fingerprints the pre-install environment and tested package. Installation necessarily
changes that environment; the receipt links the exact tested DLL/helper/package hashes to the
installed release. Do not treat the old run's hardware checklist as current sign-off after that
change. For fresh hardware evidence, start a new harness run against the installed release:

```sh
python3 tools/desktop-test/run.py start --package VizhiDesktop_0_14_0.lplug4
```

Copy Reply requires selected answer text; automatic latest-answer discovery is still unsupported.
The native fixture proves the code paths, not real ChatGPT/email/browser compatibility. The
region picker, live attachment labels, source app editor support, profile switching and physical
keypad workflow still need owner acceptance. App-specific profiles can override System; place
Ask ChatGPT in those profiles if needed. When clipboard capture starts inside ChatGPT, there is
no known source app and Return to App is unavailable instead of reusing an earlier email window.

Logi loaded 0.14.0 and registered both new action classes. It subsequently rejected a redundant
second load request; the identical warning exists in the preceding 0.13.0 log. No Vizhi dev link
was present. The receipt records this existing host behavior instead of claiming an error-free log.
