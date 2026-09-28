# Vizhi for Codex 1.6.2 — release readiness

The signed follow-up to 1.6.1: every Windows executable and DLL in the package carries an
Authenticode signature, and the four findings from Logitech's 1.6.1 Windows retest are fixed
(#125 #126 #112 #113). Packaging process is [SUBMISSION.md](../SUBMISSION.md); listing copy is
[marketplace-listing-vizhi.md](marketplace-listing-vizhi.md); the 1.6.1 record is
[release-vizhi-1.6.1.md](release-vizhi-1.6.1.md).

**Release source: main `e0c0b80`** (PR #127: six fix commits, the docs commit, and the
review fix `fa6a4b9`; PR #130, the Codex daemon routing fix found in this release's own Mac
device pass, #129; PR #133, the Windows pass's findings on the daemon: Yes/No refused on a
delivered approval, #131, and daemon processes on session keys, #132). The cuts from `62bbf36`
(sha `84c3db69…`) and `b2c5507` (sha `e86d6cac…`) were never published.

## Done

| Item | Evidence |
|---|---|
| Code on main, version in both files | `e0c0b80`; Vizhi 1.6.2, Claude Console stays 2.3.2 (its share sits under `[Unreleased]`) |
| macOS suite on the release source | 1,369 passed, 0 failed, 29 skipped (Windows-only); bridge scripts 54/0, codex hook 27/0, windows signing 16/0 |
| Both product compile checks | 0 warnings, 0 errors |
| macOS device pass, first cut | Install, DLL hash, load, no hook re-trust, two voice drafts, pin without timeout, New Codex in the routed folder: PASS. Then two sessions in different folders showed **AlertWala's approval on the claude-console key** and a No press typed into that tab: Codex 0.158's shared daemon files every session's events under the terminal that started it. Filed #129, fixed in PR #130 |
| macOS device pass, re-cut source | Unsigned test build of the fix, 19:49–19:52: first poll re-keyed the pending approval to slot 2; No rejected on ttys003, a second No ignored as answered, Yes approved a new prompt on ttys003, voice raised a third and No rejected it; slot 1 read claude-console throughout |
| macOS device pass, signed re-cut (the draft's bytes) | 20:07 install: DLL hash matches the package, hook exe shows the Certum chain, loaded in 176 ms, routing line on the first poll. 20:10–20:11: No rejected a pending prompt on ttys003; voice draft and voice Send transcribed; a fresh AlertWala approval arrived under ttys001, was re-keyed, and Yes approved it on ttys003; both session files then held the new Stop event, the plugin's copy owner-only (0600) — the shipped write path, not the test build's |
| Windows device pass, second cut | Unsigned splice on the laptop, 20:26–21:14: hooks trusted, bridge Ready, a real approval on the pinned session — every Yes refused ("needs fresh approval state after helper failure"): under the daemon the helper never keys a session, so it never writes the receipt the 2.3.2 gate wants (#131). Two daemon `codex.exe` processes and Codex App's plugin runtime took session keys (#132). Both fixed in PR #133 and verified there: Yes and No answered the real terminal, only the terminal in `registry.json` |
| Code review after merge (PR #133) | Findings recorded below; the fallback that adopts an invisible daemon-hosted session's events on a terminal with no known session of its own is the one to tighten Tightening filed as #134. |
| Code review before merge | 12 findings; one fixed before the merge (the load-time status read ran ahead of the #125 latch — every service start inside the window flashed *Run /hooks*), one changelog omission fixed (Windows New Tab also moved), the rest recorded below as follow-ups |
| Package built | `VizhiCodex_1.6.2.lplug4`, sha `17129db67c54aef488a52e96cd3251f69baa3aedf3d5dc13e95ef29eef058056`, 16.85 MiB, packed 2026-09-28 from main |
| Windows payload signed | 16 of 16 `.exe`/`.dll` Authenticode-signed by one certificate (CN=Ravi Shankar S, Certum Code Signing 2021 CA), SHA-256, RFC 3161 timestamp; verified per file by `tools/verify-package.sh` and again from the packed file |
| macOS voice helper | Developer ID, notarized, stapled; re-verified after packing; byte-identical to the helper shipped in Claude Console 2.3.2 and Vizhi 1.6.1 |
| Package hygiene | One plugin DLL, no `PluginApi.dll` or host closure, no build-machine paths, no smoke markers |
| Profile | Unchanged since 1.6.1: revision `2026-09-16.2`, five pages, three session keys; hosted at <https://vizhi.dev/layouts/> |
| Release folder | `~/Downloads/Claude Console/VizhiCodex-1.6.2-release/` — package, both layouts, `SHA256SUMS.txt`, `provenance.json`, GitHub notes, Marketplace notes |
| GitHub release | DRAFT `vizhi-codex/v1.6.2` targeting `e0c0b80`, not marked latest (Claude Console 2.3.2 keeps the badge); the owner publishes |

## Owed before the Marketplace submission

- [x] **Windows suite on the release source**: 1,396 of 1,398 on the laptop at PR #133's tip (the
      same tree as the merge). The two failures are timing cases: the held-open launcher case is
      green 3 of 3 after `20ef983`; the cold SessionEnd case fails identically on an untouched
      main there and is noted on #126.
- [ ] **Windows device pass on the signed package** (the 28 Sep laptop pass ran an unsigned splice
      of the second cut with only the plugin DLL replaced; it found #131 and #132 and verified
      their fixes — Yes 21:10:27 and No 21:13:50 on the real terminal — but the signed third cut
      has not been installed there): install from the release draft, `/hooks`
      re-trust (the command changed, #126), Yes and No on a real approval, a session-key press
      showing **Selecting** then pinning (#112), New Codex opening in the project folder (#113),
      and once with the hook helper renamed away so **Blocked** and the Options+ warning appear
      (#126) — that last one also exercises the failure-record JSON (review follow-up 2 below).
      Plus **two sessions in different folders** with Codex's daemon running: the approval must
      light its own session's key and Yes/No must answer only that session (#129; the Windows
      hook walks the same process ancestry, so the daemon misroutes there too).
- [ ] **Publish** the draft after the pass; record the URL here and in the PM note.
- [ ] Marketplace form: upload, paste the three copy fields from the listing doc via `pbcopy`,
      record the submission date back into the listing doc.

## Open — owner decision

- [ ] **New Codex with no routed session opens in the first `project-roots` entry.** Those
      entries are containers of projects (discovery lists their children), so Codex registers a
      folder like `~/Work` as a trusted project — narrower than the profile root, but the same
      class #113 set out to remove. Ship as documented (this release) or open in the most
      recently used child of that root (1.6.3).

## Review follow-ups (not blocking; not yet filed)

1. The Windows Codex hook command embeds the whole launcher body in the string Codex hashes for
   trust, so any later launcher edit re-prompts every Windows user — against the repo rule of
   keeping the launcher stable and putting churn in the C#. macOS avoids it with a stable script
   path; Windows could install the launcher as a `.ps1` and keep the command a one-liner.
2. The command contains literal double quotes only in the failure-record JSON. If Codex hands
   it to PowerShell without argv quoting, failure records become unparsable: Blocked still
   shows, with a "cannot be read" message. Exercised by the device pass above.
3. A faulted stdin read in the Codex launcher is recorded as a helper launch failure (Blocked
   until the next hook) although the helper never ran.
4. Dictation released during a session switch is dropped with "pin a session slot" and two beeps.
5. A session key pressed while another routed action is in flight is dropped silently.
6. An upgrade to a future-dated package after a quarantine stays Blocked until the next hook;
   with the latch file unwritable, a restored quarantined helper can read as an upgrade after a
   restart past the file time.
7. New Tab on macOS computes and logs a directory the platform does not use; the synchronous
   `SelectSlot` has no admission gate; Yes/No repaints do wasted work during a selection.
8. The daemon-routing notice ("an event for … arrived under ttys001 … shown on ttys003") was
   logged twice on this Mac for one session (20:07:48 and 20:11:43) although the registry test
   pins once per session and target, and the earlier test build logged it once across two
   events. Informational only; routing was correct both times. Not reproduced.

## Not in this release

- Claude Console 2.3.3: the engine fixes above reach it, but 2.3.2 stays exactly what QA holds
  until Logitech's verdict on the signed helpers.
