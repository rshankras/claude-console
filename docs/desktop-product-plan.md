# Vizhi Desktop — feature plan

The keypad product for the OpenAI desktop app (`com.openai.codex` — branded **ChatGPT**, carrying
both the ChatGPT assistant and the Codex agent), macOS and Windows. Decision record and recon in
[multi-agent-architecture.md](multi-agent-architecture.md) → *"a third PRODUCT, not a third
adapter"*, and issue #19.

One app, two hats. On this Mac there is a single bundle: the ChatGPT surface (chat, voice,
images, memory) and the Codex surface (tasks, approvals, diffs) live inside it. So this is **one
product with a page per hat**, not a product per app. Windows may package the two differently —
recon item W0.

---

## Two stories that define the product

Every feature below is derived from one of these. If a proposed key serves neither, it doesn't ship.

**The Codex operator.** You dictate "fix the failing CI" into a task and switch to your editor.
Twenty minutes later a key on the keypad glows amber: *npm install*. Glance — routine — press
**Approve** without leaving the editor. Later it glows red: *rm -rf node_modules && …* — the LCD
shows the command itself, so the risk is readable from the key face. You press **Focus** to go
look instead. When the task finishes, the key turns green with the elapsed time. The app never
had to be frontmost; you never had to context-switch to find out whether it was waiting.

**The ChatGPT companion.** You're writing an email. One key summons Quick Ask, one key starts
voice, one key screenshots the chart you're staring at straight into the conversation, one key
copies the answer back to your clipboard. ChatGPT stays a side-channel; the keypad is the
switchboard. None of it requires the app to be frontmost, so these keys belong on ANY profile —
including the user's default one.

The first story is the safety property the terminal products are built on, ported to a GUI. The
second is new ground: the terminal products never had a "companion" mode because a terminal
isn't one.

## Page layout (9 LCD keys per page)

> **Where these keys live — settled on hardware 2026-08-25.** A Logi application profile is only
> ACTIVE WHILE ITS APP IS FRONTMOST. The terminal products get away with binding their keys to the
> app they drive, because you are *in* the terminal while using them. This product is the opposite:
> its whole promise is answering the agent while you are somewhere else, so an app-bound page
> would show the approval keys exactly when you least need them — and would hide them the rest of
> the time. The flagship set (**Activity, Approve, Deny, Show ChatGPT**) therefore belongs on the
> user's **default profile**, which is active regardless of what is in front, and the product
> README makes that placement step one. We cannot do it for them: the default profile is the
> user's own configuration and importing over it would destroy whatever they had.
>
> The packaged, auto-imported page is consequently the *in-app* set only — Activity, Mode, New
> Chat, Voice, Stop — and deliberately does not duplicate Approve/Deny/Show (pinned by a test).
> This costs the product its "install and it lights up" story and replaces it with "install, place
> four keys once". That is the honest trade, and it is worth it: the alternative is a headline
> feature that only works in the one situation it was built to avoid.

### Page 1 — Codex operator (the flagship)

| Key | Face behaviour | User value |
|---|---|---|
| **Approve** | idle grey → amber on pending approval → **red when RiskClassifier flags the command**; LCD marquees the pending command text | resolve approvals without switching windows — the D2 property |
| **Deny** | lit only while an approval is pending | the other half; safe default under a red Approve |
| **Status** | working / waiting / done + elapsed time | "is it stuck?" answered by a glance at the desk, not a ⌘-Tab |
| **Stop** | — | halt a runaway task now |
| **New task** | — | start the next thing |
| **Voice** | existing listening face | dictate the prompt — the whisper stack ships as-is |
| **Prev / Next conversation** (2 keys) | — | move between running tasks/threads |
| **Focus app** | — | the one key that *deliberately* brings the window forward: "I need to look" |

*Deliberately absent:* **Always allow**. One press granting standing permission is a footgun on a
device you can lean an elbow on. The card exposes the button (spike-verified on Claude Desktop);
we choose not to bind it. Document the choice in the listing — it will read as a missing feature
otherwise.

### Page 2 — ChatGPT companion

| Key | User value |
|---|---|
| **Quick Ask** | summon the launcher over any app — the app's own global hotkey, fired by the keypad |
| **Voice mode** | start/stop a spoken conversation |
| **Screenshot → ask** | capture a region into the current conversation (existing screenshot key + composer attach path) |
| **New chat** | fresh context in one press |
| **Copy last answer** | the response, on the clipboard, without touching the mouse |
| **Stop generating** | interrupt a rambling answer |
| **Search chats** | jump back into an old thread ("find past chats" is a headline Plus feature) |
| **Model picker** | open the model menu |
| **Temporary chat** | privacy toggle for the next conversation |

### Page 3 — Prompt library

The existing engine feature, unchanged in spirit: user-editable `prompts.json`, each entry a key,
sent to the composer of the active conversation. Requires the composer write path (D5). This is
where per-user workflows live; it is also the page Logitech's "profile look and feel" work can
restyle freely.

## How each mechanism lands (macOS / Windows)

| Mechanism | macOS | Windows | Proven? |
|---|---|---|---|
| Press an in-window control unfocused | `AXPress` (D2-passed on Claude Desktop, same Electron shape) | UIA `InvokePattern.Invoke()` | mac: on Claude Desktop; **this bundle: D-rerun pending**. Windows: **W-run never executed** |
| *Detect* the approval card + read its text | `AXObserver` notifications, poll fallback | UIA structure-changed events | **NEW — D6/W6, the biggest unknown** (D2 proved pressing, never watching) |
| Composer write | AX set-value + press Send — better than focus-and-type: no focus needed at all | UIA `ValuePattern` | D5/W5 pending |
| Global hotkey (Quick Ask, voice) | synthetic keystroke of the app's registered global shortcut | `SendInput` | low risk |
| Menu accelerators (New chat, …) | `AXPress` the `AXMenuItem` — worked unfocused in recon | UIA on the menu | low risk |
| Deep link (resume) | `codex://` via LaunchServices | protocol launch | verify what `codex://` actually routes to |
| Voice capture | existing ClaudeVoiceHelper.app + whisper | existing claude-console-voice.exe + whisper | shipped tech |
| Screenshot | existing `ScreenshotCommand` capture path | existing | shipped capture; new attach step |

**Injection rule, adapted:** the terminal engine's law is "atomic focus-then-type or nothing."
The desktop equivalent is better: AX/UIA set-value doesn't need focus at all. The law becomes
**"the text lands in the composer of the conversation the user targeted, or nowhere, and the
failure is reported"** — same spirit, stronger guarantee. Focus-then-type is the *degraded
fallback* (the spike's fail-branch), not the design.

## Engine work: the third seam

The desktop twin of the two existing seams, same shape, same discipline:

- **`IDesktopAutomation`** (platform layer) — hides AX vs UIA. Verbs: find window, press control,
  read control text, set composer, observe changes, send hotkey, open URL. Mac and Windows
  implementations; nothing above it knows which.
- **`IDesktopAppAdapter`** (app layer) — hides *which app*. Maps neutral verbs (Approve, Deny,
  NewChat, QuickAsk, …) to this app's control labels, hotkeys and deep-link scheme. First
  implementation: the OpenAI app. **Claude Desktop later becomes a second adapter on the same
  engine** — the spike already passed there, so this seam is provably general from day one.

Capability honesty carries over verbatim: a `DesktopCapabilities` struct, keys hide where the
app gives us nothing truthful to show. No hooks → **no Cost, no Context** — but the 2026-08-24
sitting proved the UI itself is an honest source for more than expected (spike README, *Sitting
completed*): **Model** is readable (`AXPopUpButton "5.6 Terra Medium"` in the composer area),
**Activity** is derivable three-state from control presence (`Stop` = running, `Allow once`/
`Deny` = waiting, neither = idle), and the **approval light has a native signal** — the sidebar
toggle relabels to `View activity, needs attention` when a card is pending, so the poller watches
ONE label, not a tree diff. All gates D0–D6 passed on `com.openai.codex`; detection is poll-based
(~1s cadence, observer confirmed dead). Rollout files are NOT written by desktop tasks — the
file-based Status source is out, the UI-derived one is in.

**The brittleness risk to design for now:** control matching is by accessibility label, and
labels are UI copy — they change with app updates and with *localization*. A German user's
button isn't "Allow once". Mitigations: match role + a list of label candidates, degrade to
hidden (never to pressing the wrong node), keep the control map versioned and hot-updatable, and
treat "control not found" as a first-class reported state. This is the desktop analogue of
Codex's unstable rollout format, and it gets the same `BestEffort` treatment.

## What deliberately does not carry over

- **The session grid.** One window, not N TTYs; conversations replace sessions. Prev/Next + the
  app's own sidebar do the job. No process discovery, no reaping.
- **Live telemetry keys** (Cost / Context / Model / Activity) — no hook interface exists.
- **Input-mode chords, tab-completion** — composer semantics differ; nothing honest to bind.
- **The state-bridge scripts** — nothing to install into, which also means: no install-time
  settings mutation at all. The desktop product's install is *clean* — a genuine listing point.

## Design review round — external feedback, 2026-08-25

Phase 1 + the appendix pages shipped and were reviewed by an experienced user ("Codex companion
8/10, ChatGPT companion 6/10; stable key placement and defensive approval semantics are the two
things I'd scrutinize before trusting it daily"). Disposition of every substantive point, so the
reasoning survives:

### Accepted — the review found real defects

1. **Moving conversation keys are the product's worst UX defect.** Slots currently mirror sidebar
   order, which is recency — so sending a message to conversation B reshuffles every key,
   including between a user's glance and their press-to-jump. *Live information is good; live
   remapping of physical controls is not.* Fix: **stable slot assignment in the monitor** —
   a title→slot map, new conversations fill empty slots, existing ones KEEP their slot across
   reorders, age out only when they leave the sidebar entirely. This is `SessionRegistry`'s
   model (the terminal grid solved the identical problem for TTYs); reuse the design, not the
   TTY-keyed code. App-side pins ride along free: a pinned chat never ages out.

2. **Approval needs identity, and the mechanics need stating.** A mechanical fact the review
   surfaced by asking: the approval card only exists in the OPEN conversation's view — background
   conversations show only the sidebar "Awaiting approval" badge. So Approve can only ever press
   the visible card (a real safety property) and can also approve the wrong conversation's card
   if the user thinks a different one is open (the real hazard). Fixes:
   - The waiting Approve face names its target: the OPEN conversation's short title (active
     conversation detection = new helper field; the sidebar's selected row or the content
     heading). Within the two-words-per-key law the hardware taught — title on the face, full
     card wording one press away via Show ChatGPT.
   - **Expected-card guard**: the approve press carries the card text the monitor last showed
     (`press --expect-near "<text>"`); the helper refuses when the visible card no longer
     matches. Closes the glance-to-press race where card A resolves and card B appears — the
     press fails with "card changed, look" instead of approving the unseen one. (Double-press
     is already safe: the press verb re-finds the control at press time; a second press finds
     no card and no-ops.)
   - **Red means two-step**: first press arms (face flips to "Press again"), ~3 s to confirm,
     second press fires. Implementable with plain presses + a timer today; upgrade to
     press-and-hold if an SDK spike shows the keypad exposes hold events. Amber stays
     one-press — the risk grade is an extra warning, not the security boundary, exactly as the
     review says.

3. **Fail closed, and say why.** `Unavailable` currently collapses five different truths. The
   helper already distinguishes them (exit codes: not-trusted / app-not-running; label
   no-match); surface them as distinct states — **Hidden** (screen locked/window gone),
   **No permission** (Accessibility lost), **Not running**, **Unrecognized** (attention marker
   present but no approve/deny match — the app-update drift case). Approve/Deny disable in every
   one of them. Never guess.

4. **Adaptive cadence.** 1 s polling is ~130 ms of helper wall-clock per tick — fine, but wasted
   while idle or hidden. Poll 1 s while Working/Waiting, ~3 s when Ready, ~5 s when Hidden;
   any state change snaps back to 1 s. (Event-driven is NOT available — see pushback below.)

5. **Draft keys must look like drafts.** Review PR and Debug draft rather than send; the key
   face should say so — the label gains a trailing "…" (the writing convention for "more
   needed"), and Options+ descriptions already spell it out.

6. **Voice needs a draft variant.** `Voice Draft` key: transcribe → composer → focus the app,
   no send — the terminal products' VoiceDraft, one sink swap away. (Transcription-confidence
   display is a whisper-pipeline change; candidate, not this round.)

7. **Current-task controls beat generic prompts for Codex users.** The AX tree already exposes
   the Review surface (`Toggle file diff`, `Jump to file`, `Show files` — captured in the
   button inventory). Page 2 grows **Show Diff** (and friends as verified live). "Run tests" /
   "stop after current" stay workflow-brief territory; "notify me when finished" is a candidate
   (macOS notification off the monitor's state edge).

8. **Non-color state distinction**: audit the waiting/unread/running glyphs for shape
   distinctness at key size (the keypad-icon lesson: differentiate by inner mark, not container).

### Pushed back — with evidence

- **"Event-driven updates would be preferable where possible."** They would; they aren't
  possible. AXObserver accepted all eight notification types on this app and delivered zero
  events in 300 s of visible UI change (spike, 2026-08-24). Polling is not the fallback here —
  it is the only mechanism this app offers. Adaptive cadence (above) is the honest response to
  the battery concern.
- **The wholesale 3-page restructure (Tasks / Current task / Create).** The current page
  structure is Appendix D — the design the customer was sold. Its substance is absorbed
  (stable slots, current-task keys on page 2, Create ≈ Workflows + New chat); its structure
  stays until Logitech agrees to revise the appendix, not before.
- **"Globally active Deny is risky."** Deny is dark and inert unless a card is pending — the
  accident window is exactly the approval window. Keeping it global; the two-step rule guards
  the red case on both keys.
- **The review's "global keys" section** (Attention / Voice / Approve / Show) is the
  default-profile set this product already documents as setup step one — independently
  reinvented, which is decent evidence the design is right.

### The ChatGPT half — elevated from "deferred" to a named phase

The review's verdict — strong Codex companion, partial ChatGPT companion — is fair, and its
ChatGPT list maps closely onto this plan's original page 2 (Quick Ask, screenshot→ask, copy
last answer, search chats, model, temp chat) which Phase 1 deferred. Feasibility notes from the
live tree: per-message **Copy message** buttons exist (copy-latest = press the LAST match — the
helper grows a `--last` flag); **Stop** exists; screenshot capture ships in the engine already.
Regenerate / read-aloud / temporary chat need a recon pass over the message-actions popup.

## Phases (revised)

- **Phase 1 — DONE, hardware-verified 2026-08-25.** Seams, adapter, monitor, the three appendix
  pages (Conversations+answers / Actions / Workflows), voice, registration, packaging hooks.
- **Phase 1.5 — hardening (next; all items from the review round above).** Stable slots ·
  approval identity + expected-card guard + two-step red · explicit unavailable reasons ·
  adaptive cadence · draft markers · Voice Draft · Show Diff · glyph audit. This is the "trust
  it daily" gap and it precedes new surface area.
- **Phase 2 — the ChatGPT companion set** (list above, feasibility-gated per key).
- **Phase 3 — Claude Desktop as adapter #2.** The contractual Deliverable 3; spike passed
  2026-08-12; costs an `IDesktopAppAdapter` + its label recon, on the engine Phase 1 proved.
- **Phase 4 — Windows.** W0 recon, then the UIA `IDesktopAutomation`; voice + screenshot reuse
  the terminal port's Windows pieces.
- **Phase 5 — polish + release.** Localization-resilient control maps, notarized helper,
  listing copy, `codex://` resume.

## Open questions

1. **Name.** "Vizhi Desktop"? It may host Claude Desktop later, so not "Vizhi for ChatGPT".
2. **Does the desktop app write `~/.codex` rollouts?** If yes, a `BestEffort` task-status/context
   read may be honest after all — would upgrade the Status key. Check while running a task.
3. **What does `codex://` route to?** Resume-last, specific-thread, or just launch.
4. **Approval detection cost** — AXObserver push vs poll; keep it off any hot path.
5. **Windows packaging** — one app or two decides whether Windows needs two registrations.
