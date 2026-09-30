# Handoff — Vizhi Desktop 1.0.0 (2026-09-29)

Read this first when picking up Vizhi Desktop. It records where the 1.0.0 release stands, what is
still open, and the traps found on the way. Design history lives in `docs/vizhi-desktop-*.md`;
submission process in `SUBMISSION.md`; listing copy in `docs/marketplace-listing-desktop.md`.

## State at handoff

| What | Where / value |
|---|---|
| Release | **Vizhi Desktop 1.0.0**, macOS only (Apple Silicon), ChatGPT desktop app (ChatGPT + Codex modes) |
| Code | PR #136 merged → `main` **`7011148`** (tree identical to release commit `cf2be45`) |
| Tag | `vizhi-desktop/v1.0.0` → `7011148` (lightweight, like `vizhi-codex/*`) |
| GitHub release | **Published** 2026-09-29, *not* Latest (Claude Console 2.3.2 keeps Latest). Assets: `VizhiDesktop_1.0.0.lplug4`, `SHA256SUMS.txt`, `provenance.json` |
| Package | `~/Downloads/Claude Console/VizhiDesktop-1.0.0-release/VizhiDesktop_1.0.0.lplug4` — sha256 `99785a0c05d387e7b8ad6f2b27790eec26d953611b082578224a7a035a837af7`, 3,447,180 bytes |
| Signing | `VizhiAxBridge` Developer ID (Ravi Shankar, 8LEAJKRS3U), hardened runtime, timestamped, **notarized (Accepted)**; voice helper notarized + stapled |
| Marketplace | **Submitted 2026-09-29** (owner pressed Submit). Portal fields as in `docs/marketplace-listing-desktop.md` (teaser 107/120, detail 490/500, notes 994/1000) |
| vizhi.dev | Live, site repo `~/Work/MyApps/vizhi-site` commit `2edef4e`: `/vizhi-desktop/` shows **1.0.0 · In Marketplace review**; EULA names Vizhi Desktop; FAQ `#desktop` (10 Q&As); privacy covers Desktop |
| Owner's Mac | 0.17.24 installed from the pre-release package (same behaviour as 1.0.0 except version/metadata) |
| PM/QA email | Draft in **Apple Notes → Career**: "Vizhi Desktop 1.0.0 — email to PM & QA (DRAFT, unsent)" — fill in the PM's name |

## Open items (in order)

1. **Send the PM/QA email** (owner) — Apple Notes → Career.
2. **Merge PR #137** (docs only: this handoff, listing copy as submitted, `SUBMISSION.md` status line).
3. **Codex's uncommitted work** in `.worktrees/desktop-merge` — `src/Core/BridgeManager.cs` (test seams:
   `VoiceRecorderLauncher`, `VoiceModelHttp`, `ScheduleVoiceModelDownload`) and
   `tests/DesktopSharedSpeechTests.cs`. Created by Codex on 2026-09-29 19:38 when the owner pressed
   *Write Tests* with this repo open. Tests passed on later runs. **Owner to decide: review + commit, or discard.**
   Never swept into a commit or package (every package since was built from a clean checkout of a commit).
4. **On Logitech approval**: vizhi.dev badge "In Marketplace review" → Available; availability text → install
   from the Marketplace (Desktop page, home card, FAQ "How do I get Vizhi Desktop?" + its JSON-LD, `llms.txt`).
5. **Field test** (artifact https://claude.ai/artifact/MytWzLVaXbRptPYvEwuQuU, db collection `results`):
   scenarios 1–8 **pass** (owner-marked). Remaining: 9 Attach (had an empty-target launch failure at
   20:18 on 0.17.21 — now guarded; re-test), 10 Find Chat (fixed in 0.17.22 — re-test), 11 Prompts,
   13 Voice Chat. **12 Schedule: skip** (removed). Use the owner's "use" notes for vizhi.dev use cases.
6. **Deferred features**: keypad folders for Permissions / Projects / Pull Requests (need live AX inspection of
   those screens), scroll keys, Quick Chat + dictation, Windows (UIA helper not self-contained).

## What changed this session (0.17.18 → 1.0.0)

- **Layout** (0.17.18): Home = Recent 1·2·3 / New Chat · Deny · Approve / Screenshot · Dictate · Send.
  Deny/Approve are the family red/green `RenderDecisionTile`s. Tools = Mode · All Chats · Find Chat /
  Attach · Paste · Copy Reply / Voice Chat · Prompts|Tasks · More. No blank keys in either mode.
  `DesktopHomeToolsLayoutMigration` moves stock Home+Tools together (backup `.before-0.17.18`), and in
  0.17.22 swaps only the Find Chat key on a stock 0.17.18–0.17.21 Tools page (`.before-0.17.22`).
- **Voice** (0.17.19–0.17.20): Speak Query uses the shared `base.en` model; the 574 MB Turbo download and
  `DesktopSearchVoiceModel` are gone (decision note `docs/vizhi-desktop-speak-query-2026-09-19.md` marked superseded).
- **Run Tests** flask icon (0.17.21) via `WorkflowIcon` stock-icon mapping.
- **Find Chat** (0.17.22): Tools key is now the `FindChatDynamicFolder` itself; in Codex it opens View Changes.
- **Empty helper args** (0.17.22): `MacDesktopAutomation.Invoke` refuses a null argument and logs the flag.
- **Stuck prompts** (0.17.23): a retained *fixed* prompt (no spoken brief, no added material) no longer
  blocks other prompts; its chat gone → rewritten into the current chat, waiting for Send.
- **More** (0.17.24): ChatGPT = Projects, Continue (+Clear Added); Schedule/Scheduled removed (still assignable);
  "Open Chat" when there is no composer.
- **Release** (1.0.0): metadata Proprietary + vizhi.dev links; `pack-release.sh` notarizes the AX helper when
  `SIGN_IDENTITY` is a Developer ID.
- Removed from the default profile at the owner's request ("keep it simple"): hold-Screenshot window capture,
  Copy Reply "Back to <app>", Return to App in More. The helper's `context-window` verb and
  record-source-before-attach stay as dormant, tested plumbing — don't re-add without asking.

## Traps learned (expensive to rediscover)

- **Options+ ignores a command's request to open a folder** (`ExecuteGenericAction(DynamicFolder, …)`):
  the log shows `Action=… ADD/GET` but never `Creating layout mode for dynamic folder`. Bind folders to keys.
- **The Desktop profile is app-bound**: its keys exist only while ChatGPT is frontmost. Anything meant to act on
  "another app" must target the window *behind* ChatGPT. The System "Ask ChatGPT" page is not installed on the owner's Mac.
- **Build release packages from a clean checkout of the commit** (`git worktree add --detach … <commit>`) — the
  working tree may hold someone else's uncommitted edits.
- **`pack-release.sh` + release signing**: `SIGN_IDENTITY="Developer ID Application: Ravi Shankar (8LEAJKRS3U)"`,
  notary profile `claude-console-notary`; `CC_CHECK_LINKS=1` checks the yaml links. Capture notarytool output
  before grepping (pipefail + `grep -q` SIGPIPE'd the first attempt).
- **`-t:Compile` can leave a resource-less DLL** for the next full build: `rm -rf src/Products/VizhiDesktop/obj
  bin/VizhiDesktop` before packing; a good Desktop DLL is ~2 MB.
- **Full `tests/run-all.sh` runs here often lack a pty** → codex-hook (17 fail) and bridge-scripts (44/54). Rerun
  `tests/scripts/test-codex-hook.sh` and `test-bridge-scripts.sh` standalone before believing a failure.
- **Stuck plugin state** clears with a service restart (`killall LogiPluginService`; launchd respawns in ~1 s).
- **"No speech"** with a quiet WAV (`/private/tmp/vizhi-desktop/voice/capture.wav`) = timing/level, not the model:
  speak after the key shows Listening. Measure levels only; never listen to or transcribe the user's audio.
- **Chrome headless below ~500 px crops, it doesn't reflow**; use CDP device emulation for phone checks.
- **Marketplace portal** is `marketplace.logi.com/contribute`; an "Unauthorized" validation means re-login.
  The browser upload tool only accepts files the session may read (copy into the scratchpad first).

## Useful paths

- Worktree: `.worktrees/desktop-merge` (branch `integrate/vizhi-desktop-main`, merged; holds Codex's WIP).
- Installed plugin: `~/Library/Application Support/Logi/LogiPluginService/Plugins/VizhiDesktop/`;
  log `…/Logs/plugin_logs/VizhiDesktop.log`; profile
  `…/Applications/Loupedeck70/@_vizhidesktop/Profiles/A8B982E4103C4F99A4C75070AF60A6E4/ProfileInfo.json`.
- Site: `~/Work/MyApps/vizhi-site` (Firebase `vizhi-dev`; preview channel `vizhi-desktop-preview`); its own
  handoff is `.planning/HANDOFF.md`.
