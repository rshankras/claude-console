# Vizhi for Codex 1.6.2 — release readiness

The signed follow-up to 1.6.1: every Windows executable and DLL in the package carries an
Authenticode signature, and the four findings from Logitech's 1.6.1 Windows retest are fixed
(#125 #126 #112 #113). Packaging process is [SUBMISSION.md](../SUBMISSION.md); listing copy is
[marketplace-listing-vizhi.md](marketplace-listing-vizhi.md); the 1.6.1 record is
[release-vizhi-1.6.1.md](release-vizhi-1.6.1.md).

**Release source: main `62bbf36`** (PR #127: six fix commits, the docs commit, and the
review fix `fa6a4b9`).

## Done

| Item | Evidence |
|---|---|
| Code on main, version in both files | `62bbf36`; Vizhi 1.6.2, Claude Console stays 2.3.2 (its share sits under `[Unreleased]`) |
| macOS suite on the release source | 1,339 passed, 0 failed, 29 skipped (Windows-only); bridge scripts 54/0, codex hook 27/0, windows signing 16/0 |
| Both product compile checks | 0 warnings, 0 errors |
| Code review before merge | 12 findings; one fixed before the merge (the load-time status read ran ahead of the #125 latch — every service start inside the window flashed *Run /hooks*), one changelog omission fixed (Windows New Tab also moved), the rest recorded below as follow-ups |
| Package built | `VizhiCodex_1.6.2.lplug4`, sha `84c3db6973d03d92fc0c39b4b285a9b4f74732143de7bd839e69549afc25d967`, 16.85 MiB, packed 2026-09-28 from main |
| Windows payload signed | 16 of 16 `.exe`/`.dll` Authenticode-signed by one certificate (CN=Ravi Shankar S, Certum Code Signing 2021 CA), SHA-256, RFC 3161 timestamp; verified per file by `tools/verify-package.sh` and again from the packed file |
| macOS voice helper | Developer ID, notarized, stapled; re-verified after packing; byte-identical to the helper shipped in Claude Console 2.3.2 and Vizhi 1.6.1 |
| Package hygiene | One plugin DLL, no `PluginApi.dll` or host closure, no build-machine paths, no smoke markers |
| Profile | Unchanged since 1.6.1: revision `2026-09-16.2`, five pages, three session keys; hosted at <https://vizhi.dev/layouts/> |
| Release folder | `~/Downloads/Claude Console/VizhiCodex-1.6.2-release/` — package, both layouts, `SHA256SUMS.txt`, `provenance.json`, GitHub notes, Marketplace notes |
| GitHub release | DRAFT `vizhi-codex/v1.6.2` targeting `62bbf36`, not marked latest (Claude Console 2.3.2 keeps the badge); the owner publishes |

## Owed before the Marketplace submission

- [ ] **Windows suite on the release source** (`62bbf36`). The Codex launcher tests are
      `[WindowsFact]` and skipped on macOS; the tip has only the commit messages' laptop
      measurements as Windows evidence.
- [ ] **Windows device pass on the signed package**: install from the release draft, `/hooks`
      re-trust (the command changed, #126), Yes and No on a real approval, a session-key press
      showing **Selecting** then pinning (#112), New Codex opening in the project folder (#113),
      and once with the hook helper renamed away so **Blocked** and the Options+ warning appear
      (#126) — that last one also exercises the failure-record JSON (review follow-up 2 below).
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

## Not in this release

- Claude Console 2.3.3: the engine fixes above reach it, but 2.3.2 stays exactly what QA holds
  until Logitech's verdict on the signed helpers.
