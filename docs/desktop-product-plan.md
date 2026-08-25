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

## Phases

- **Phase 0 — gates (mac, this machine + the Windows laptop).** Re-run D1–D5 against
  `com.openai.codex` (`probe.swift --app` is ready); add **D6: detect + read the approval card**
  via AXObserver, measure latency and cost. Run W1–W6 on Windows (`spikes/desktop-plugin/windows/`
  has never been executed). **W0:** how does the app ship on Windows — one package or two?
  Verify the `codex` basename matcher collision with the app running (issue #19 checklist).
  *Everything below is contingent on D2/W2 passing on this bundle.*
- **Phase 1 — mac, page 1.** The two seams, the OpenAI adapter, Approve/Deny/Stop/New/Voice/
  Focus/Prev/Next. Product scaffold under `src/Products/` with its own namespaces (IPC root,
  `@_` registration, profile GUID, runtime home, versions in two files). This alone is a
  demoable, sellable product.
- **Phase 2 — mac, pages 2–3.** Companion keys + prompt library + screenshot-attach. Detection
  (Status key) lands here if D6 passed.
- **Phase 3 — Windows.** UIA implementation of `IDesktopAutomation`; voice + screenshot reuse
  the Windows pieces the terminal port already built.
- **Phase 4 — polish.** Localization-resilient control maps, `codex://` resume, listing copy,
  Claude Desktop as adapter #2 if/when we choose.

## Open questions

1. **Name.** "Vizhi Desktop"? It may host Claude Desktop later, so not "Vizhi for ChatGPT".
2. **Does the desktop app write `~/.codex` rollouts?** If yes, a `BestEffort` task-status/context
   read may be honest after all — would upgrade the Status key. Check while running a task.
3. **What does `codex://` route to?** Resume-last, specific-thread, or just launch.
4. **Approval detection cost** — AXObserver push vs poll; keep it off any hot path.
5. **Windows packaging** — one app or two decides whether Windows needs two registrations.
