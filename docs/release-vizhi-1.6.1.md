# Vizhi for Codex 1.6.1 — release readiness

First Marketplace submission for this product. Source of truth for what is done, what is owed, and
who owns each open item. Validation record is [issue #105](https://github.com/rshankras/claude-console/issues/105);
packaging process is [SUBMISSION.md](../SUBMISSION.md); listing copy is
[marketplace-listing-vizhi.md](marketplace-listing-vizhi.md).

**Release source: main `c534153`** (#104, plus #106 docs and #107's help-URL fix).

## Done

| Item | Evidence |
|---|---|
| Code on main, both products versioned | `c534153`; Vizhi 1.6.1, Claude Console 2.2.3 |
| macOS suite | 1,191 passed, 0 failed, 13 skipped (Windows-only hook tests) |
| Windows suite | 1,204 passed, 0 failed, 0 skipped at `8467c48`. Since then the only runtime change is #107's help-card URL; behaviour is unaffected |
| Both Release builds | 0 warnings, 0 errors, both platforms |
| Package built | `VizhiCodex_1.6.1.lplug4`, sha `70e6408ea216…`, 16.60 MiB (repacked after #107) |
| Package hygiene | One plugin DLL, no `PluginApi.dll` and none of its host closure; DLL 1.15 MB with all icon resources embedded |
| macOS voice helper | Developer ID, notarized, stapled; re-verified after packing |
| macOS device pass | Installed from the package, DLL hash matches, MX demo exercised, all three session slots in the log |
| Windows device pass | Installed from the release draft; all four payload hashes match provenance; Yes and No both log-captured on the same session |
| Profile | Five pages, three session keys, revision `2026-09-16.2`; verified against the installed layout on Windows. Downloads now hosted at <https://vizhi.dev/layouts/> |
| Licence + URLs | Proprietary; EULA, support and homepage on vizhi.dev. Since #107 **no URL compiled into the plugin leaves vizhi.dev** — verified by decoding the packaged DLL |
| Listing copy | Teaser 104/120, detail 470/500, release notes 986/1000; reviewer notes written |

## Open — owner

- [ ] **Listing screenshots / artwork.** A keypad photo showing page 1 mid-session is published at
      <https://vizhi.dev/assets/codex-keypad@2x.jpg> and is the strongest asset available. Decide
      whether one image is enough for the listing or more are wanted.
- [ ] **EULA reviewed by counsel.** Long-standing open item, carried from Claude Console. Decide
      whether it blocks this submission or ships as-is.
- [x] **The EULA on vizhi.dev carries the proprietary §1 grant**, checked 2026-09-16: "The Software is
      licensed, not sold", with MIT named only for third-party components. Last updated 2026-09-10.

## Open — decisions

- [ ] **Windows helpers are unsigned.** `claude-console-tools.exe` and `claude-console-hook.exe`
      ship unsigned, and Smart App Control or an organization policy can block them. This is the
      one substantive quality gap in the release. Options: obtain a Windows code-signing
      certificate and sign them, ship unsigned with the limitation disclosed on the listing, or
      hold the Windows half. Whichever is chosen should be stated in the listing, not discovered
      by a user.
- [ ] **Tag and publish.** No `vizhi-codex/v1.6.1` tag exists. The draft release
      `vizhi-codex/v1.6.1-9b7595a` was cut for moving the package to the Windows laptop and is
      build-identified rather than version-identified. Decide whether to publish that draft,
      retag cleanly as `vizhi-codex/v1.6.1`, or keep GitHub releases internal now that the
      repository is going closed.

## Not in this release

- **Claude Console 2.2.3** is built and packaged but not submitted and has had no macOS device
  pass. It exists so that 2.2.2 stays exactly what QA holds. Its listing copy still stops at 2.2.2
  and would need 2.2.3 release notes before any submission.

## Submission steps, once the open items clear

1. Confirm the package still matches this source: `shasum -a 256 VizhiCodex_1.6.1.lplug4` →
   `70e6408ea216…`. The earlier `ef35c7d765ea…` package predates #107 and points its hook-trust
   help card at the old host; do not submit it.
2. Package hygiene — the package legitimately carries twelve extra DLLs for the Windows whisper
   runtime, so check the three things that matter rather than counting DLLs:
   ```bash
   unzip -l VizhiCodex_1.6.1.lplug4 | grep -c 'PluginApi.dll'                 # 0
   unzip -l VizhiCodex_1.6.1.lplug4 | grep -cE ' bin/[A-Za-z]+Plugin\.dll$'   # 1
   unzip -l VizhiCodex_1.6.1.lplug4 | grep -cE 'ExCSS|Svg\.|Newtonsoft|YamlDotNet'  # 0
   ```
3. Upload at [marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute) and
   paste the three copy fields from the listing doc. Use `pbcopy`, never a terminal selection —
   invisible indentation counts against the character limits.
4. Attach the screenshots.
5. Record the submission date and the exact copy back into the listing doc, as was done for
   Claude Console 2.2.2.

Review takes about ten working days.
