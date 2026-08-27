# Handoff — the 2.0.1 QA retest fixes

State as of **2026-08-27**, end of day. Branch `fix/qa-p0`, branched from `main` (2.1.0), pushed.
Read this before touching the QA work; the issue tracker carries the detail, this carries the shape.

## Where it stands

| | |
|---|---|
| Branch | `fix/qa-p0`, 4 commits, pushed, **no PR opened** |
| Suite | 617 C# + 47 shell, green |
| Issues | 25 filed (#20–#44) plus #45, #46 found while working. Milestone `QA fixes — CC 2.2.0 / Vizhi 1.5.4` |
| Blocked on Logitech | #23, #35, #40–#43 (label `blocked:logitech`) |

### The four P0s

| Issue | Reproduced | Fixed | Hardware-verified |
|---|---|---|---|
| **#20** install forces an LPS restart | yes | `655f98d` + `4f2438e` | dev-link path both ways; **packaged install still unproven** |
| **#21** No key approves the action | yes, on the keypad | `592097d` | yes, on the keypad |
| **#22** injection breaks on non-US layouts | yes, on the keypad | `c5f6932` | yes, on the keypad |
| **#23** universal plugin vs packaged profile | n/a, a decision | — | blocked on Logitech |

## What is NOT done

- **#24 whisper, #25 sticky pin, #26 project roots, #27 redraw storm** — all P1, all untouched. #24 needs
  the bundled binary rebuilt with its backend libraries and then re-signed and notarised, so it is the
  long pole.
- **#25 needs a design decision before code.** The Session keys mean "the one I selected", the display
  keys mean "the one I'm looking at", and they cannot share one value. Three options in the issue.
  It is also the second half of the answer promised to Logitech about approval targeting, so it is the
  next thing a reader should pick up.
- **Two behaviours claimed but not proven on hardware**: that pasting a slash command still selects the
  right autocomplete entry (#22 — paste arrives at once where typing filtered progressively), and #20
  on a real package install.
- **No PR.** The base-branch question was never formally settled — see below.

## The base-branch question, and why `main` won

A second review recommended branching from `v2.0.1` rather than `main`, on the grounds that 2.1.0 has
never been hardware-tested. That is a fair objection and it was under-weighted at first.

`main` won on two facts: `v2.0.1` is the **pre-multi-agent tree** (flat `src/`, no `src/Core`,
`src/Products`, `src/Agents`), so every engine fix would have to be written twice and Vizhi for Codex
would get none of them — and **11 of the 14 QA issues are shared-engine defects that the released
1.5.3 already carries**.

The objection is now largely answered in passing: the dev build driven all afternoon *is* 2.1.0 plus
these fixes, and it behaved. That is not a full pass, but it is no longer untested.

## Traps and findings worth not rediscovering

**The #20 fix was wrong once, and the second version matters.** Gating the heal per PRODUCT also
silenced it for dev builds, where the desync is real. The Options+ icon vanished within the hour.
It is now gated on install SOURCE: a package install lives under the service's `Plugins/` directory,
a dev build is reached through a `.link` pointing outside it. Packages skip the restart, dev builds
keep it. The suite was green through both versions — only hardware caught it.

**A file-existence check is not sufficient evidence for #21.** Both answer keys get exercised in the
same minute during a test, so the victim file's absence can mean "Yes approved it" rather than "No
failed". Read the plugin log timestamps against the transcript, or you will conclude the fix failed
when it worked.

**`auto` permission mode masks #21 entirely.** With `defaultMode: auto` and `skipAutoPermissionPrompt`
in `~/.claude/settings.json`, routine commands never prompt — two reproduction attempts produced false
positives before this was noticed. Use `Shift+Tab` in the session to reach ask-every-time.

**The plugin already knew.** #21's fix needed no new detection: `session.State == "waiting"` is what
draws the amber badge on the very key that was misbehaving. The key drew a badge about a pending menu
and then typed a word at it.

**`keystroke` breaks more than text.** The reproduction harness itself failed under Cyrillic because
it used `keystroke "d"` for Ctrl-D. Any character-based shortcut is affected — hence three
`keystroke "t"` sites fixed, not the one the report cites.

## Reproduction tooling (scratchpad, not in the repo)

Written this session and worth recreating if useful:

- `input-source.swift` — list/enable/select/disable keyboard layouts through Text Input Services
  rather than editing `com.apple.HIToolbox`. Takes effect immediately, cleanly reversible.
- `repro-22.sh` + `inject.applescript` / `inject-fixed.applescript` — runs the plugin's exact
  injection into a Terminal window that captures stdin, prints what actually arrived. Honours
  `INJECT_SCRIPT` so old and new mechanisms can be compared side by side under one layout.
- `recolour.py` — recolours flat single-colour PNGs, preserving alpha.

## What Logitech owes us

An email is drafted and, at time of writing, unsent. The blocking item is **#23**: QA wants a
universal plugin (no app binding, no packaged profile) while their design team is commissioning a
redesign of that same profile. Answering it also resolves #34 and #45 as side effects.

Also outstanding from them: the 19 missing icon glyphs at 192px or SVG in white, the colour semantics
decision (#40), whether these fixes go into the existing 2.0.1 submission or a new one, and whether
key labels follow each agent's own wording ("Yes/No" on screen for Claude Code) or stay consistent
across the family.
