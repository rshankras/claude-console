# Vizhi for Codex 1.5.1 — preview notes

*For initial review. Not yet on the Marketplace; Claude Console 2.0.1 is in Marketplace review
separately.*

*1.5.1 fixes the Windows install failure in 1.5.0: the sandbox ACL grants ran on the plugin's
Load path and their recursive `icacls /T` walk of the Logi tree ate the service's 10-second
Load budget, so Options+ showed the plugin stuck "waiting" and the install failed. The grants
now run in the background and let NTFS inheritance do the propagation.*

## What this is

A second keypad product from the Claude Console codebase: **Vizhi for Codex** gives OpenAI's
Codex CLI the same physical control surface Claude Console gives Claude Code — live session
keys, risk-graded approvals, one-press prompts and git verbs, native `/review`, screenshot
into the running conversation, and fully offline voice dictation.

The point of the preview is as much the **architecture** as the product: one engine now builds
a package per agent. `src/Core` is agent-neutral (platform bridges, session grid, file IPC,
actions, voice, self-registration); `src/Agents/<agent>` is a small adapter (~a file per
concern) declaring what the agent can honestly report; `src/Products/<name>` pairs them with
branding, a profile, and a version. Supporting a new terminal agent is an adapter plus a thin
product folder — the engine, tests, and packaging are shared.

## The keys, page by page

| Page | Keys |
|---|---|
| 1 — Sessions & answers | Codex sessions ×3 (live state per tab) · **Screenshot** · **Voice** · **Voice Draft** · Yes · No · Esc |
| 2 — Session control | **Review** (native `/review` picker) · Model · Compact · Up · Enter · Down · Scroll ↑ · Scroll ↓ · Clear (`/new`) |
| 3 — Prompts | Explore · Explain · Review (prompt) · Optimize · Refactor · Write Tests · Document · Fix Bug · Security |
| 4 — Terminal & sessions | Voice "Go to Project" · New Tab · Next Tab · Prev Tab · **New Codex** · Exit |
| 5 — Git | Commit · Create PR · Diff · Log · Push · Status |

Session keys and Yes/No light **amber** when Codex asks permission, **red** when the pending
command is destructive (`git push`, `rm -rf`, `sudo`…). The full key-by-key story is in the
[product README](https://github.com/rshankras/claude-console/blob/vizhi-codex/v1.4.4/src/Products/VizhiCodex/README.md).

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
is exact when tab titles are unique (verified on hardware via UI Automation) — but two sessions
started in the same directory share a console title, and identically-labeled tabs are
indistinguishable to Windows, so the first one wins; start sessions from different directories
to keep switching precise, and avoid Windows Terminal's "Rename tab" (it detaches the label from
the console title the switcher matches on). macOS keeps the full hook bridge and every feature,
approvals included.

Two Windows repairs worth knowing, both from the same hardware session: if codex ALSO fails its
own shell commands with "the local command sandbox failed to start", copy
`codex-windows-sandbox-setup.exe` and `codex-command-runner.exe` from the release's
`codex-resources\` folder to beside `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` —
codex does not look where its own installer put them. And this plugin now grants codex's sandbox
users access to its install and IPC directories automatically, so the day OpenAI fixes the hook
runner, hooks light up with no plugin update needed.

## Install

> **Preview on a machine without Claude Console installed** — the two plugins both bind
> Terminal.app and the plugin service activates only one, so a keypad that already runs
> Claude Console will show that plugin's keys, not this one's. Uninstall it first
> (Options+ → Plugins) or use a different machine.

1. Logi Options+ **6.4+**, MX Creative Keypad, [Codex CLI](https://developers.openai.com/codex/cli) installed natively.
2. Double-click `VizhiCodex_1.5.1.lplug4` → install via Options+.
3. Wait ~1 minute: the plugin self-registers its application + layout and restarts the plugin
   service once (Options+ blinks and returns on its own).
4. **Trust the hooks.** At your next Codex session start you'll see **"Hooks need review — 7
   hooks are new or changed"**. That's this plugin: one entry per lifecycle event
   (SessionStart, PreToolUse, PermissionRequest, …), all running the same one-line launcher,
   `~/.codex/codex-console/scripts/codex-hook.sh` — it writes state files for the keypad and
   nothing else. Choose **Review hooks**, confirm, and trust (or *Trust all and continue*).
   Codex trusts by hash, so this is one-time. If you pick *Continue without trusting*, keys
   stay static — recover later with `/hooks`.
5. Grant, once each as macOS prompts: **Accessibility** (typing), **Microphone** (voice),
   **Screen Recording** (screenshot).

## Known limitations (preview)

- **One console per machine** — this and Claude Console both bind Terminal.app; the plugin
  service activates one plugin per application, so installing both leaves one silently dead.
  A unified dual-agent console is the planned answer for users of both.
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
