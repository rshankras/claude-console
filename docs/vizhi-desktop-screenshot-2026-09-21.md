# Screenshot directly into chat

Version 0.16.3, branch `integrate/vizhi-desktop-main`.

1. Open the intended ChatGPT or Codex conversation.
2. Press **Tools → Screenshot** and select the screen region. Escape cancels.
3. The image appears in the chat input. **Attached** confirms the app exposed its thumbnail.
4. Type an instruction or use **Home → Dictate**, review the draft, and press **Send**.

The image no longer waits for a spoken workflow. Existing text is preserved, including text
selected before capture. No hidden instruction is added, and no message is sent automatically.
Each subsequent Screenshot press deliberately captures another image.

The composer identity is pinned before opening the region picker. The native attachment step
verifies the window, chat, mode, editor, and readiness again; changing the target refuses the
attachment. It moves only the caret, pastes the captured file, restores the clipboard unless
another copy replaced it, and verifies both the thumbnail name and unchanged draft text.

The key distinguishes **Select Area**, **Attaching**, **Attached**, **Cancelled**, and
**Check Image**. An unconfirmed paste is never retried automatically by Dictate. Inspect the
composer before capturing again. Attached images are managed in the app; Clear Added only
clears separately staged source material.

Validation artifacts are under `artifacts/desktop-screenshot`. C# checks cover immediate
attachment, cancellation, a changed destination, preflight refusal, and no later reattachment.
Native AppKit and Chromium fixtures exercise preserved draft text, clipboard restoration,
duplicate recognition, and subsequent dictation. These controlled fixtures are separate
from physical-keypad and live ChatGPT acceptance.

Verified and installed on 2026-09-21: all test suites passed (1,978 C# tests passed,
13 skipped), along with AppKit image/append and Chromium image/Copy Reply fixtures.
The packaged native helper exactly matches the helper exercised by all four fixtures.
LogiPluginService confirmed version 0.16.3 loaded in 185 ms. The selected keypad
profile and workflow configuration were preserved. Installation hashes and the
rollback backup path are recorded in `artifacts/desktop-screenshot/installation.json`.
