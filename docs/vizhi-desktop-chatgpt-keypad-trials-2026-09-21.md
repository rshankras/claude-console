# ChatGPT workflows to try on the physical keypad

For Vizhi Desktop 0.17.0/0.17.1, selected Vizhi Home profile. The installed DLL
and Home/Tools bindings were checked before preparing these trials. The owner presses
the hardware keys; reported outcomes are recorded in the table below.

Start in ChatGPT mode. If the Tools Mode tile says Codex, tap it and wait until it
says ChatGPT. On the main pages, use the physical paging buttons to move between
Home and Tools. Within a folder, use its Back/navigation control to return.

## 1. Beginner: answer a customer without retyping their message

Goal: produce a concise, accurate reply from copied context and a spoken instruction.

Practice customer message:

> Hi Ravi, can you deliver 10 desk lamps by Friday? Please confirm whether installation
> is included and tell me the total price. Thanks, Maya.

1. Copy that message once, just as you would select and copy text from Mail.
2. On Home, press **New Chat** and wait for an empty composer.
3. Go to **Tools → Paste into Chat**. The customer message should appear in the composer.
4. Return Home and press **Dictate**. Say: “Draft a friendly reply to Maya. Confirm
   Friday delivery. The total is fifty-two thousand rupees including installation.
   Keep it under eighty words.” Press Dictate again to finish.
5. Wait for transcription. The instruction should appear beneath the customer message;
   the email should remain intact. Review it on the Mac, then press **Send**.
6. Wait for the response to finish. Go to **Tools → Copy Reply**, without selecting
   any answer text. Paste into a scratch note to confirm the complete reply was copied.

Success: one initial source copy, automatic insertion of context and dictated instruction,
one deliberate send, and one-tap response copying. No real email is sent in this practice.
If the key says Insert Draft, Check Chat, or another refusal, record the exact label and
which step produced it; do not mark the workflow passed merely because the key reacted.

## 2. Intermediate: understand a confusing screenshot

Goal: capture the visible problem and obtain an explanation without saving and browsing
for an image manually.

1. Leave a new ChatGPT conversation ready, with a suitable example visible on screen
   (for example, a confusing app dialog or a table whose meaning you want explained).
2. Press **Tools → Screenshot** and select the region with the mouse/trackpad.
3. Wait until the image attachment is visible in the chat composer.
4. From Tools, open **Prompts → Explain**. It should append an instruction about the
   supplied image and show **Send Draft**, leaving the image attached and unsent.
5. Review the draft and press **Send Draft**. Check that the answer addresses the
   screenshot, rather than an unrelated earlier conversation.

Success: capture goes directly into the intended chat; no Save, Finder, or manual paste
step; the request is sent only after review. Selecting the screen region remains a
mouse/trackpad action. The keypad does not choose the intended region for you.

## 3. Advanced: compare suppliers and turn the decision into a plan

Goal: use two documents, apply real decision criteria, and plan the next actions.

Two fictional practice files were created in Downloads:

- `Vizhi-Practice-Quote-A.txt`: INR 48,000, delivery after the Friday opening, one-year warranty.
- `Vizhi-Practice-Quote-B.txt`: INR 52,000, Friday delivery, two-year warranty.

1. On Home, press **New Chat**. Go to **Tools → Attach Files**.
2. Tap the two practice quotes. Both should show **SELECTED**; the action should say
   **Attach 2**. A second tap on a file should deselect it.
3. Press **Attach 2**. Confirm both filenames in the composer. The picker should return
   to Tools after confirmed attachment.
4. Open **Prompts → Compare**. Say: “Compare these two quotes. My budget is fifty-five
   thousand rupees. Delivery before the Friday opening is essential. Compare total
   cost, delivery and warranty, then recommend one supplier.” Tap again to finish.
5. Confirm both attachments and the complete instruction remain visible. Review and
   press **Send Draft**. The answer should recognize that B meets the delivery requirement
   and budget, whereas A is cheaper but late. Judge the content, not an exact wording.
6. After completion and with an empty composer, press **Plan** in Prompts. This sends
   a conversation-based planning request immediately. The answer should provide next
   steps based on the comparison; this request does not itself place an order.
7. Return to Tools and press **Copy Reply** to use the resulting plan in your notes.

Success: choose both sources on the keypad, state criteria once, review before sending,
and continue the same conversation without reattaching the files.

## Record the outcome

| Trial | Physical outcome | Exact failing step/key label, if any |
|---|---|---|
| Customer reply | Passed by owner: Paste into Chat, Dictate/Send, and Copy Reply into a scratch note reported working. | Owner confirmed new-chat grid refresh seemed quick; no timing change needed. |
| Screenshot explanation | Owner reports prompt insertion now works on 0.17.3. | The attachment-only insertion failure through 0.17.2 is resolved in the owner’s reported trial. Sending and judging the screenshot response have not yet been explicitly confirmed. Other prompts have not each been confirmed on hardware. |
| Compare and plan | Passed by owner on 0.17.3: “all good” after the full guided workflow. | Two-file attachment, spoken Compare brief, automatic insertion, explicit Send Draft, follow-up Plan, and Copy Reply reported working. |
| Codex Dictate | Owner reported failed automatic insertion on 0.17.3; 0.17.4 installed, owner retry pending. | Missing Codex composer hint reproduced the cursor refusal. Updated adapter passed both-mode delivery tests and AppKit/Chromium insertion checks. |
| Codex Review Changes | Owner reported a truncated preset followed by the complete prompt on 0.17.4. | The legacy insertion/retry route reproduced this pattern in a controlled fixture. The 0.17.5 correction uses whole-draft insertion, retained recovery and exact-text submission; hardware acceptance pending. |

Automated fixture results and physical acceptance are separate. Useful observations are
whether the intended source arrived, whether instructions were appended without loss,
whether any unnecessary keyboard steps remained, and whether the final answer was usable.

[Screenshot → Explain fix and verification](vizhi-desktop-image-explain-fix-2026-09-21.md).
[Continuing prompt retry investigation](vizhi-desktop-prompt-retry-2026-09-22.md).

[Rich-text placeholder correction and verified 0.17.3 installation](vizhi-desktop-attachment-caret-2026-09-22.md).

[Codex dictation diagnosis and installed 0.17.4 correction](vizhi-desktop-codex-dictation-2026-09-22.md).

[Complete preset insertion and submission in 0.17.5](vizhi-desktop-preset-submit-2026-09-22.md).
