# Vizhi Home redesign

Branch: `integrate/vizhi-desktop-main`. Release: 0.15.0.

The owner asked for an intuitive keypad with frequent actions directly accessible and native
ChatGPT Voice retained. A single page with nested menus increased interaction cost for recent
chats and screenshots. The chosen design uses two pages with stable positions and optional menus.

| Page/mode | Top row | Middle row | Bottom row |
|---|---|---|---|
| Home — ChatGPT | Recent 1 · 2 · 3 | Chats · New Chat · Find Chat | Dictate · Send/Stop · Voice Chat |
| Home — Codex | Recent 1 · 2 · 3 | Chats · New Task · Changes | Dictate · Send/Stop · Voice Chat |
| Tools — ChatGPT | ChatGPT → Codex · Copy Reply · Return to App | Attach Files · Clipboard · Screenshot | Clear Added · Saved Prompts · More |
| Tools — Codex | Codex → ChatGPT · Approve · Deny | Attach Files · Review Changes · Run Tests | Screenshot · Saved Prompts · More |

Home keeps the first six positions and Dictate/Send positions from Flow. The old standalone Stop
position becomes native Voice Chat; Stop shares the submission position while the app responds.
Tools preserves the old Actions-page Mode/Approve/Deny positions in Codex. Unknown/changed mode
refuses a mode-specific tap instead of reinterpreting it. Copy Reply still needs selected answer text.

Dictate records, inserts and leaves review/submission to Send. This includes source-backed drafts,
which previously reused the workflow key's third-press Send behavior. Saved workflow keys retain
their existing explicit SPEAK/SEND/DRAFT behavior, custom configuration and retained-draft gestures.
Review/Tests resolve their configured named IDs; deleted or unusable custom tasks are unavailable.

A displayed Stop only tries the response Stop control. It never falls back to Send, including if
the response ends between display and press. Submission remains the existing guarded helper verb.
The native Voice Chat key retains the configured app shortcut and its own voice state handling.

Screenshots may now start while ChatGPT is frontmost. In that case there is no external source
window to return to; the capture must not reuse an earlier email destination. Screen capture and
real attachment accessibility still require owner acceptance; verification uses a synthetic image.

The new profile is added and selected with a backup. Older profiles, custom prompt files and the
System Ask ChatGPT page are retained. Neither tests nor previews inspect live ChatGPT, record the
microphone, read the general clipboard or capture the real screen.

## Installed and verified

Installed **0.15.0**, selected **Vizhi Home**, and confirmed Logi loaded the release plus the new
Tools command and Saved Prompts folder. The installed DLL/helper hashes match the tested package.
All 35 existing desktop profile files, the entire System application configuration, and custom
workflow JSON hashes were preserved.

- [Generated preview of Home and Tools in both modes](../artifacts/desktop-home/preview/home-and-tools.png)
- [Installation receipt](../artifacts/desktop-home/installation.json)
- [Release verification](../artifacts/desktop-tests/20260919-211445-32b5f5/report.html)

Verification passed: **1,881 C# tests**, 13 platform-specific skips, shell/Python/Swift suites,
**128 command/mode variants**, **29 native fixture steps**, package checks and fixed-audio Turbo
inference. External package URLs were not checked. All 190 owner acceptance cases remain pending;
software coverage does not mark hardware results as passing. The test report fingerprints the
pre-install environment; the receipt links its exact tested artifacts to the installed release.

Try Home's bottom-left Dictate → speak → tap to finish → review → bottom-centre Send. The
bottom-right Voice Chat remains the native app-owned conversation. During a response, centre
changes to Stop. In Codex, Home's Find Chat position becomes Changes; Tools presents direct
Approve/Deny/Review Changes/Run Tests. These live transitions still need the owner's keypad check.
