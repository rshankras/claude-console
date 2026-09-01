# Vizhi for Codex 1.6.0 — preview notes

*Preview build, pending Marketplace submission. It can be installed beside Claude Console 2.2.0;
both products are universal and claim no terminal application.*

*1.6.0 brings the shared Claude Console 2.2.0 QA work into the Codex product:*

- *Universal packaging lets Vizhi and Claude Console coexist; neither owns Terminal.*
- *The Logitech 2026-08 key design, state palette and Yes/No tiles are shared. The active session
  bar follows product identity: Codex blue in Vizhi, Claude orange in Claude Console; inactive
  sessions remain grey.*
- *Session pinning, retention, interruption recovery, approval targeting, voice failure handling,
  non-US keyboard injection and project discovery carry the QA fixes verified for 2.2.0.*
- *Windows ships the fixed No/Esc injector, identity-based tab selection, Terminal navigation
  guard, Screenshot helper and smoke-tested offline voice payload.*
- *Separate macOS and Windows Vizhi profile downloads now bind the correct terminal and plugin,
  with previews that match the imported first page.*

## What this is

A second keypad product from the Claude Console codebase: **Vizhi for Codex** gives OpenAI's
Codex CLI the same physical control surface Claude Console gives Claude Code — live session
keys, one-press prompts and git verbs, native `/review`, screenshot into the running
conversation, and fully offline voice dictation. **Risk-graded approval lighting (amber/red)
is macOS-only** — see Windows, below.

The point of the preview is as much the **architecture** as the product: one engine now builds
a package per agent. `src/Core` is agent-neutral (platform bridges, session grid, file IPC,
actions, voice, universal packaging); `src/Agents/<agent>` is a small adapter (~a file per
concern) declaring what the agent can honestly report; `src/Products/<name>` pairs them with
branding, a profile, and a version. Supporting a new terminal agent is an adapter plus a thin
product folder — the engine, tests, and packaging are shared.

## The keys, page by page

| Page | Keys |
|---|---|
| 1 — Sessions & interaction | Codex sessions ×3 (live state per tab) · **Screenshot** · **Voice** · **Voice Draft** · Esc · No · Yes |
| 2 — Codex controls | Model · Plan · Skills · Agent · Fork · Resume · **Review** · **Context gauge** · Compact |
| 3 — Prompts | Explore · Explain · Document · Optimize · Refactor · Fix Bug · Code Audit · Write Tests · Security |
| 4 — Terminal | Voice "Go to Project" · New Tab · **New Codex** · Prev Tab · Next Tab · Exit · Up · Enter · Down |
| 5 — Git | Git Status · Diff · Log · Commit · Push · Create PR |

Session keys and Yes/No light **amber** when Codex asks permission, **red** when the pending
command is destructive (`git push`, `rm -rf`, `sudo`…). The full key-by-key story is in the
[product README](https://github.com/rshankras/claude-console/blob/main/src/Products/VizhiCodex/README.md).

Two design rules the product is built around:

- **A key never shows a value the agent did not report.** Codex bills a subscription and
  reports no spend, so there is no Cost key rather than a fake `$0.00`. No autocomplete, no
  Tab key. Capability flags per adapter decide, and the packaged profile agrees with the code.
- **A press never lands in the wrong window.** Every typing key resolves Codex's own Terminal
  tab and types there atomically, or beeps and types nothing.

## Verified on hardware (macOS, Apple Silicon)

Session discovery · hook state bridge · live busy/waiting/ready · project name · focus
tracking and tab switching · risk-graded approvals (amber/red) · model + context display ·
voice (submit and draft) · screenshot → current conversation · native `/review` · all prompt,
git, and navigation keys.

**Windows works differently, on purpose (1.5.0).** Codex's hook runner creates no process on
Windows — a probe binary that logs every launch and cannot exit nonzero was never invoked while
codex reported "hook exited with code 1", in BOTH elevated and unelevated sandbox modes, with
the sandbox itself provably working. That is upstream (openai/codex #17478, #26158, #24098;
experiment table in `docs/spike-windows-codex-hooks.md`), so 1.5.0 stops waiting on it.

**Windows reads codex's own rollout transcript instead.** Sessions, project name, busy / done /
ready and best-effort context all work — with **no hooks installed and no `/hooks` trust prompt
on that platform**. The transport differs; the state files, the grid and every key do not.

The honest gap: **risk-graded approval lighting is unavailable on Windows.** Codex publishes no
approval event outside the hook runner, so the keypad declares the capability absent rather
than lighting keys amber on evidence that does not exist — the same rule that gives Codex no
Cost key. Yes/No still answer prompts; they type, they do not observe. Tab-switching on Windows
selects by identity, even for identically-titled tabs (verified on hardware): unique titles
match directly, and on a duplicate the switcher briefly retitles the target session's own
console to a nonce, selects the one tab that repaints to it, and restores the title — the tab
is found by which console it is, not what it says. One caveat: avoid Windows Terminal's
"Rename tab", which detaches the label from the console title the switcher works through.
macOS keeps the full hook bridge and every feature, approvals included.

Two Windows repairs worth knowing, both from the same hardware session: if codex ALSO fails its
own shell commands with "the local command sandbox failed to start", copy
`codex-windows-sandbox-setup.exe` and `codex-command-runner.exe` from the release's
`codex-resources\` folder to beside `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` —
codex does not look where its own installer put them. And this plugin now grants codex's sandbox
users access to its install and IPC directories automatically, so the day OpenAI fixes the hook
runner, hooks light up with no plugin update needed.

## Install

1. Logi Options+ **6.4+**, MX Creative Keypad, [Codex CLI](https://developers.openai.com/codex/cli) installed natively.
2. Double-click `VizhiCodex_1.6.0.lplug4` → install via Options+.
3. Import `VizhiCodex-Keypad.lp5` on macOS or `VizhiCodex-Windows.lp5` on Windows. The plugin is
   universal and intentionally installs no application or layout of its own.
4. **macOS only — trust the hooks.** At your next Codex session start you'll see **"Hooks need
   review — 7 hooks are new or changed"**. That's this plugin: one entry per lifecycle event
   (SessionStart, PreToolUse, PermissionRequest, …), all running the same one-line launcher,
   `~/.codex/codex-console/scripts/codex-hook.sh` — it writes state files for the keypad and
   nothing else. Choose **Review hooks**, confirm, and trust (or *Trust all and continue*).
   Codex trusts by hash, so this is one-time. If you pick *Continue without trusting*, keys
   stay static — recover later with `/hooks`.

   **On Windows there is no hook step and no trust prompt at all** — the plugin installs no
   hooks there and reads Codex's session transcript instead. If Codex ever asks you to trust
   hooks on Windows, they are not ours.
5. Grant permissions, once each. **macOS**: **Accessibility** (typing), **Microphone** (voice),
   **Screen Recording** (screenshot). **Windows**: microphone access for desktop apps, if you
   use voice — nothing else.

## The Yes / No keys, precisely

They do two separate things, and only one of them is cross-platform:

- **Answering** — pressing Yes or No types the answer into the focused Codex session. This
  works on **macOS and Windows**, always: the keys type, they do not observe.
- **Lighting** — the same keys (and the session key) turn **amber** when Codex is waiting for
  an approval and **red** when the pending command is destructive. This needs Codex to announce
  the approval, which it does through a lifecycle hook — so it works on **macOS only**. On
  Windows the keys stay dark and answer exactly as well; you just don't get the glance-from-
  across-the-room signal.

## Known limitations (preview)

- **Windows: no approval lighting** (above). Codex's hook runner spawns no process on that
  platform, and no other Codex channel announces an approval, so the capability is declared
  absent rather than guessed. A working prototype exists behind Codex's app-server websocket;
  it is documented, not shipped.
- **Profiles are OS-specific downloads.** Import the macOS profile on Terminal.app and the Windows
  profile on Windows Terminal. The packages themselves can coexist with Claude Console.
- Context % is read best-effort from a Codex transcript format documented as unstable — the
  key shows nothing rather than a stale number when parsing surprises us.
- Layout changes in a future package don't reach an already-imported profile (import
  de-duplicates by profile id); a version-aware heal is on the roadmap.

## Going deeper

The two-seam design that makes this a product line rather than a fork — a platform bridge
hiding the OS, an agent adapter hiding the CLI — is in
[docs/multi-agent-architecture.md](https://github.com/rshankras/claude-console/blob/vizhi-codex/v1.4.4/docs/multi-agent-architecture.md).
A third agent is an adapter plus a thin product folder; the engine, the 550-test suite, and
the packaging are shared. Also in the codebase: the risk classifier behind the approval keys,
and the fully offline voice pipeline (Developer-ID signed, notarized helper + whisper.cpp —
no network).
