# Vizhi Desktop keypad visual consistency

Branch: `integrate/vizhi-desktop-main`. Based on the owner's IMG_2873–IMG_2878 hardware photos.

Readability follow-up: **0.12.5 is now installed**; see
[measured title layout and quieter Ready footer](vizhi-desktop-title-readability-2026-09-18.md).
The 0.12.4 implementation and installation history follows.

Status: **0.12.4 implemented, packaged, installed and loaded locally** on 2026-09-18 at 17:03:58
IST. Adaptive 3 remains selected. Physical keypad appearance is pending owner confirmation.

## Plan

1. Fix All Chats first: return the same full-key conversation widgets used on Home. Keep exact-title tokens, live status, guarded selection, and close only after a successful selection. Remove the second host caption. Retain the SDK's Back button and automatic pagination.
2. Use one outlined desktop icon family. Reduce the optical weight of All Chats; distinguish Quick Chat. Give Search, New Chat, Scheduled, Attach Files, Rewrite, Plan, Send Draft, and Continue recognizable symbols.
3. Keep action identity visible when disabled: `Send Draft` / `No draft`, `Changes` / `No changes`, `Copy Answer` / `Unsupported`. Dim the glyph and show the reason separately. Busy and approval states must not masquerade as an empty draft.
4. Refresh both profile previews without changing their IDs, physical bindings, page order, custom workflow prompts, or SEND/DRAFT behavior.
5. Verify SDK routing and state guards, render real plugin key faces for visual inspection, build and package, then run the existing automated harness. Physical keypad appearance still needs owner confirmation.

## Rendering contract

- Conversations: one title, at most three lines; the existing 25% state footer on both Home and All Chats.
- Controls: consistent outlined icon, persistent name, optional smaller availability text. Status does not replace the action name.
- Workflows: preserve icon → name → SEND/DRAFT footer.
- Product glyphs remain blue-violet `#8593F8`; inactive glyphs are neutral grey. State colors retain their existing meaning.
- Keep the host-owned Back control. The public dynamic-folder API has no widget setting or Back-image override. A folder may return other action names, so its children use the registered conversation widget with an exact-title parameter instead of the SDK's synthesized non-widget folder commands.
- Existing workflow configurations using the old built-in Rewrite/Continue glyphs receive the new semantic glyph at render time. Explicit alternative icons remain authoritative; no configuration file migration is needed.

SDK reference: [Implementing dynamic folders](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/implementing-dynamic-folders/).

## Implementation and verification

- `AllChatsDynamicFolder` returns `DesktopConversationCommand` widget bindings with `chat:` plus
  a base64url title token. The existing title guard handles successful, failed, missing and reordered
  conversations. SDK navigation executes only after successful selection. The folder's Back key and
  automatic pagination remain SDK-owned.
- Desktop controls own their full key face. The label stays in a fixed position as availability
  changes; dim glyphs and smaller status text communicate the reason. Copy support is an explicit
  automation capability, separate from whether an answer happens to exist.
- Both packaged profiles' complete `ProfileInfo.json` layouts are byte-identical to the previous
  version. Only their previews changed. Existing workflow defaults receive semantic icon upgrades
  at render time; custom prompts and submission flags are untouched.
- Pre-install verification: **1,683 C# tests passed, 13 Windows-only tests skipped**; all script
  suites passed, including 20 harness self-tests. All 84 catalog/mode dispatch cases passed.
  All 10 controlled native fixture steps passed; fixed-file voice inference passed. These tests
  do not assert live ChatGPT behavior or physical keypad readability.
- Release build: zero warnings/errors. Package verified offline, including extracted voice-helper
  signing/notarization checks. External package URLs were not rechecked.
- Real plugin rendering: 33 PNG key faces generated from the built DLL and visually inspected.
  [Review sheet](../artifacts/desktop-key-preview/review.png) and
  [reusable renderer](../tools/desktop-test/preview/README.md). Synthetic content is labelled;
  the sheet uses a placeholder for the SDK's Back control.
- [Pre-install automated report](../artifacts/desktop-tests/20260918-170104-747792/report.html).
  Its installed-artifact fingerprint predates installation; retain it as historical build evidence.
- [Post-install automated report](../artifacts/desktop-tests/20260918-170458-3e8315/report.html):
  the full pipeline passed again with stable installed/source fingerprints, 1,683 passing C# tests,
  13 Windows-only skips, all 10 native fixture steps and fixed-file inference passing. The 162
  owner-driven live-app/hardware cases remain **Not tested**; software checks do not auto-pass them.

## Installation

The installed DLL matches the fully tested source build and the package:
`64d0c0c6862924f393a84fc36315bccda65a7db5696e0cd5fbe1087ced46e8d2`.
The installed and packaged AX helper retains the verified shortcut-delivery fix:
`ef5e99c2fea58637ab7e421e97aecdfb41b3278c15deb95694116e1cf433a5a6`.

Logi Plugin Service confirms version 0.12.4 loaded. Both installed profile previews were updated;
the other 26 application/profile files were preserved during installation, including the selected
profile and all bindings. Desktop workflow and shortcut configuration hashes are unchanged.
No development `.link` files are present.

Backup and receipt: `~/.claude/claude-console/backups/vizhi-desktop/20260918-170331/`.
Local [installation receipt](../artifacts/desktop-key-preview/installation.json).

## Owner acceptance on the keypad

1. Home: check that All Chats has the smaller outlined bubbles; compare its weight with New Chat
   and Search. Open All Chats and confirm entries are full-size cards with one title and state bar.
2. Select a conversation and confirm the folder closes only on success. Check Back and pagination.
3. Controls: check both modes, especially Attach Files, Scheduled and Quick Chat; verify Send Draft
   and Changes retain their names when unavailable and Copy Answer says Unsupported.
4. Workflows: inspect Rewrite, Plan and Continue. Confirm the existing SEND/DRAFT intentions remain.

The SDK's Back icon is intentionally unchanged. Custom Back artwork would require owning the
navigation layout and pagination, which is outside this consistency fix.
