# Handoff — the September commitments

State as of **2026-09-02**, with a **2026-09-04** block on top for the 2.2.1 release and submission. Read this before picking up Vizhi for Codex or the ChatGPT/Codex
desktop product; it is the shape of what was promised to Logitech and where each piece actually is.
Claude Console 2.2.0 itself is done and released — its detail is in
[HANDOFF-qa-fixes.md](HANDOFF-qa-fixes.md) and [HANDOFF-windows.md](HANDOFF-windows.md).

## State as of 2026-09-04 (read this block first)

**Claude Console 2.2.1 is submitted to the Logitech Marketplace** (owner, 4 Sep, from the form; the
Chrome extension never connected, the three texts were pasted from `~/Desktop/ClaudeConsole-2.2.1-install/`).
It answers QA's 1 September retest of 2.2.0 (`~/Downloads/Claude Console Retest, Ranked Findings.pdf`).

| | |
|---|---|
| Source | PR #67 (`fix/qa-retest-2.2.0`, ten fixes + version bump) merged to `main` as `ea39e87`; tooling fix `6de8190`; listing-URL fix `aacf154` |
| Tag | `v2.2.1` = `aacf154` (moved once from `ea39e87` before anything was published; nothing references the old one) |
| Package | `ClaudeConsole_2.2.1.lplug4`, 22,304,379 bytes, SHA-256 `2fd5da12581c2aa6d91342df32a1f8f5fe20fe9b7f829580e6e3cdd0bd2d2140` |
| Release | https://github.com/rshankras/claude-console/releases/tag/v2.2.1 — published, **private repo, internal record only** |
| Mac retest | ALL PASS on build `bb4d517f…` (same source; the submitted repack differs by two listing URLs + 144/192 bytes of build stamps). Evidence with timestamps on #55 #58 #59 #60 #62 #63 #64 |
| Report to Logitech | artifact "2.2.1 Retest Response" https://claude.ai/code/artifact/04176e26-223a-46da-ac76-271cdab83a8a (full) and `~/Desktop/ClaudeConsole-2.2.1-install/Claude-Console-2.2.1-Retest-Response.docx` (short, plain English, the one to attach) |
| PM email | drafted in Apple Notes → Career → "Logitech PM reply — Claude Console 2.2.1"; asks go/no-go, the Windows run, the #65 size decision, and what the bar for publishing is (all P0–P2, or every finding). **Check whether it was sent before saying anything to the PM.** |

### Owed, in order

1. **Send the email** (replace `[day]`; attach the `.lplug4` + the `.docx`; Drive link if 22 MB bounces).
2. **Windows pass on the laptop** (back ~7 Sep): the six checks on the last page of the `.docx` — bug A after a reboot (hook count to zero, cap of eight, uninstall without stopping LPS), item 2 (restart needed or not), item 16 both branches, Windows halves of B, C, D. None of the Windows binaries in 2.2.1 has run on Windows yet.
3. **#68 (P2) → 2.2.2**: two Options+ card links in the DLL (`BridgeNotice.SupportUrl`, `WindowsTerminalUrl`) open README anchors on the now-private repo. Needs a public help page on the keypad-profiles site first, then the constants. Ship with the Windows results, not alone.
4. **#65**: the 21 MB package is Logitech's decision (accept / per-OS / download on first use). No code until they answer.
5. The two September commitments still not shipped: **Vizhi for Codex 1.6.0** (branch `feat/vizhi-codex-1.6.0`; fix its `LoupedeckPackage.yaml` homePageUrl/supportPageUrl to the public site first, then the steps in the branch section below) and the **desktop product** (`feat/vizhi-desktop`, still needs the rebase onto `main`).

### Issue state

#66 was a false alarm: `codesign`/`spctl` run inside Codex's sandbox report a valid notarized helper as "invalid signature (code or signature have been modified)" because the sandbox blocks the trust daemon (reproduced with `sandbox-exec` denying `com.apple.trustd.agent`). Retitled to the P3 tooling ask: `sign-and-notarize.sh` ends its verify steps with `|| true` and `pack-release.sh` preflights only stapler. Never trust a signature verdict from an agent sandbox.
#57 #58 #59 #60 #55 stay open for their Windows halves. #61 #65 open. #68 new.

### Traps learned today

- `pack-release.sh`'s Windows-bundle guard used `unzip -l | grep -q` under `pipefail`: grep's early exit SIGPIPEs unzip and fails a good package (3 of 30 runs). Fixed in `6de8190`.
- Release builds are not bit-reproducible (PE timestamp, MVID, debug directory). Two packs of the same source differ by ~150 bytes per binary; say "same source", never "byte-identical".
- An Options+ uninstall deletes the plugin's log file as well as the plugin folder. Expect an empty log after a reinstall.
- The Claude-in-Chrome extension is installed (Default profile 1.0.90, Profile 1 older) but never registered to the account this session ran under; `list_connected_browsers` returned nothing. Form-filling was done by `pbcopy` + the owner.
- `qa/windows-no-keypad` commits a 22 MB `.lplug4`. Do not merge it as it is.
- `sed` with `#` as the delimiter breaks on any `#` in the replacement (issue numbers, comments). Use `|`.

### Worktrees

`claude-console-p0` = `main` at `aacf154` (where 2.2.1 was packed; `git status` clean). `.worktrees/fix-58` = `fix/qa-retest-2.2.0`, merged, disposable. `.worktrees/vizhi-codex-1.6.0` and `.worktrees/windows-no-keypad-qa` as described below. The main checkout at `~/Work/MyApps/claude-console` is still on `feat/vizhi-desktop` with the stranded `GitCommand.cs` edit (see below).

---

## What was promised, in writing

A reply drafted for the Logitech PM on 2026-08-31 (Apple Notes → Career → "Logitech PM reply —
Claude Console 2.2.0") commits to three things beyond 2.2.0:

| Commitment | Date given | Actual state |
|---|---|---|
| Same changes + design applied to the **Codex CLI plugin**, resubmitted | 1 Sep | Built on `feat/vizhi-codex-1.6.0`, **not released, not submitted** |
| Same applied to the **ChatGPT plugin + desktop app** | 2 Sep | Work in progress on `feat/vizhi-desktop`, **branch not rebased** |
| **Context %** returns to the session keys, placement per their UX team | no date | Not started — design question is with them |

**Two things about that email are unconfirmed** and must be checked before anything is said to the
PM again:

1. **Was it sent?** It was drafted, not sent, when the session ended. Its first line claims 2.2.0 is
   "submitted to the Marketplace".
2. **Was 2.2.0 actually submitted?** The Marketplace form at marketplace.logi.com/contribute was
   filled in completely (teaser 108/120, detail 479/500, release notes 973/1000) but the Developer
   Agreement checkbox and the Submit button were deliberately left for the owner. If the form was
   abandoned, the text is all in [marketplace-listing.md](marketplace-listing.md) and can be re-entered.

The email also promises the **updated contract** as an attachment. Payment is waiting on it.

## Branch state

### `feat/vizhi-codex-1.6.0` — the 1 Sep deliverable

Six commits, package version **1.6.0**, branched off `a9230bf` — i.e. it has the QA fixes and the
design, and is missing only `5fb8366` (the submission-docs commit) from `main`.

```
bad876e fix(vizhi): restore CLI hooks and approvals
ae3cbe2 fix(vizhi): recover Windows hook and state writes
e1e127a feat(vizhi): enable Windows hooks and harden sessions
db0767b fix(vizhi): enable Windows approval answers
5a9f585 feat(vizhi): complete Codex 1.6 keypad experience
83c87ba fix(codex): clear activity after interrupted turns
```

**The local worktree is 3 commits BEHIND origin** — pull before touching it, or work from a fresh
checkout. Nothing local is ahead, so there is nothing to lose.

To ship it, in this order:

1. **Regenerate the Codex profile** — `python3 tools/make-codex-profile.py`. It derives from
   `profiles/ClaudeConsole-Keypad.lp5`, whose first page and preview strip both changed in 2.2.0, so
   the Codex profile on disk is stale. Then `python3 tools/render-profile-preview.py` if the Codex
   profile needs its own preview redrawn — that script currently targets the two Claude profiles only
   and would need its `PROFILES` list extended.
2. **Suite green** — `bash tests/run-all.sh`.
3. **Notarize + pack** — `bash tools/voice/sign-and-notarize.sh`, then
   `DOTNET_ROLL_FORWARD=LatestMajor bash tools/voice/pack-release.sh 1.6.0 VizhiCodex`. The pack
   **refuses without `~/.claude/claude-console/whisper-bin-win`** (restored there on 08-30 from
   `~/Downloads/claude-console-backup-2026-08-30/`; if it is gone again, that backup is the source —
   never regenerate its `TRANSCRIPTION_SMOKE_OK`, it certifies a transcription that happened on
   Windows).
4. **Hardware pass**, then GitHub release under the `vizhi-codex/vX.Y.Z` tag convention (latest is
   `vizhi-codex/v1.5.3`, 21 Aug), then submit.

### `feat/vizhi-desktop` — the 2 Sep deliverable

Twelve commits, tip `c6b05da feat(desktop): add live ChatGPT session grid` (1 Sep).

**It is still based on `0ee1679` (21 Aug)** — before the QA retest merge, the design merge, the
Windows QA pass and the README refactor. It does not have the universal-plugin change, the safe
Yes/No logic, the copper icons, the new session face, or the current profiles. **Rebasing it onto
`main` is the first task, not an afterthought**, and it will conflict in the same places the design
merge did — `AnswerCommand.cs`, `SessionSlotCommand.cs`, `KeyImage.cs`, the profiles. The rule that
worked last time: **main's logic, the branch's rendering.**

`src/Core/Actions/GitCommand.cs` is modified in that worktree and has been for days — it is the
stranded #40 palette edit, already superseded on `main`. `git checkout --` it.

### Other worktrees

- `fix/codex-interrupt-state` is checked out at **`/private/tmp/claude-console-codex-interrupt`**.
  Its commit `83c87ba` is already in `feat/vizhi-codex-1.6.0`, so the worktree is disposable — and it
  lives under `/private/tmp`, which does not survive a reboot. `git worktree remove` it.
- `design/icons` (`22f138f`) is fully merged into `main`. Safe to delete.
- `fix/qa-retest` (`e66c1ee`) is merged via PR #53. Safe to delete.

## Open issues worth knowing

- **#55 / #56** were filed from the Windows pass: an Options+ uninstall on Windows leaves the
  live-status hooks behind, and 22 Mac-shaped LiveStatus tests fail on Windows. Both are Windows-only
  and neither blocks a release.
- **#20 #23 #24 #25 #27 #28** stay open on purpose — each has a device or release check still owed.
  `docs/HANDOFF-qa-fixes.md` says exactly which.
- **#40 #41 #42 #44** are the design items still with Logitech: label wording (the keys ship Yes/No,
  which is what the PM's own "match the on-screen prompt" rule requires), the full SVG icon pack, and
  the slot-order key. Five icons are still missing as SVG — Accept Edits, Bypass Permissions, D+S,
  Manual, Upload — all for keys that do not exist yet.

## Traps that cost time here

- **`pack-release.sh` rebuilds `tools/windows/*/publish-win-x64/*.exe`** and they are tracked, so
  they show as modified after every pack. The committed copies are the ones verified on the Windows
  laptop — discard the rebuilds unless you mean to replace them.
- **A `.lp5` preview strip is not re-rendered on import.** Options+ draws it once, when a profile is
  created there. `tools/render-profile-preview.py` exists because of this; run it after
  `tools/sync-default-profiles.py` whenever the design or the first page changes.
- **`codesign -dv | grep Authority` misreports `.so` files as unsigned.** Use `codesign -dvv`.
- The keypad preview lives only in whatever DLL the dev `.link` points at, and `pack-release.sh`
  removes that link. After packing, the keypad has no plugin until the package is installed.
