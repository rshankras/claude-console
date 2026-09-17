# Marketplace Submission Guide

Path to a submittable `.lplug4` for the [Logitech Marketplace](https://marketplace.logitech.com/contribute), per the [Actions SDK approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

**Status (2026-09-16):** **Claude Console 2.2.2** is the current Marketplace submission (submitted 2026-09-13; the copy as entered is in [docs/marketplace-listing.md](docs/marketplace-listing.md)). **2.2.3** is built and packaged from main `9b7595a` but is NOT submitted and has had no macOS device pass — it exists because Vizhi 1.6.1's shared-engine work also reaches Claude Console, so 2.2.2 stays exactly what QA holds. **Vizhi for Codex 1.6.1** is prepared for its FIRST Marketplace submission: validated on macOS and Windows (issue #105), listing copy in [docs/marketplace-listing-vizhi.md](docs/marketplace-listing-vizhi.md), open items are listing screenshots, the EULA counsel review, and the unsigned Windows helpers. History: 2.0.0 submitted 2026-08-12, QA flagged the bundled `PluginApi.dll` (host-provided, must not ship); resubmitted as 2.0.1 on 2026-08-13 with `<Private>false</Private>`; 2.2.1 submitted 2026-09-04. Review takes ~10 working days.

**Before submitting 2.2.0, decide two things that are cheaper to settle now than to re-version:**

- **Key labels** — the keys ship as Yes / No; the 2026‑08 design frames say Allow / Deny (#40 / #42). A one-line change, but it changes the screenshots.
- **The QA retest answer** — 2.2.0 exists to answer the 16 ranked findings of the 2.0.1 retest. Sending that response to the PM alongside (or before) the submission keeps the two Logitech tracks in step; the per-finding response is in `docs/HANDOFF-qa-fixes.md` and the retest report.

## Pre‑submission checklist

- [x] `LoupedeckPackage.yaml` present in `src/package/metadata/` with a plugin icon (`Icon256x256.png`).
- [x] Licence is **proprietary under the EULA** (https://vizhi.dev/eula/) as of 2026-09-09, ahead of closing the source; the repository's MIT `LICENSE` file is gone. GPL is **not** allowed on the Marketplace; whisper.cpp and the Whisper model are MIT and their licence texts ship in the package. If the form's licence list has no proprietary entry, pick the nearest and keep the EULA URL — and make sure the EULA on vizhi.dev carries the same §1 grant as [EULA.md](EULA.md) (the site copy still said MIT on 2026-09-09).
- [x] [PRIVACY.md](PRIVACY.md) (on‑device, no data leaves the machine) and [EULA.md](EULA.md), both published on vizhi.dev.
- [x] **Fill `LoupedeckPackage.yaml`**: `supportPageUrl` (https://vizhi.dev/faq/), `homePageUrl` (https://vizhi.dev/claude-console/), `licenseUrl` (https://vizhi.dev/eula/) and `author` (S.Ravi Shankar). **No user-facing link may point at github.com** — the repo will be closed, and every card button 404'd for a week when it went private (#68, #71). Same for the three URLs baked into `BridgeNotice.cs`.
- [x] **Bundle whisper.cpp** (see below) — `tools/voice/bundle-whisper.sh` vendors Homebrew's `whisper-cli` + its dylib closure into a self‑contained `~/.claude/claude-console/whisper-bin/`, **Developer‑ID signed + hardened‑runtime + notarized** via `tools/voice/sign-and-notarize.sh`, and **shipped inside the `.lplug4`** by `tools/voice/pack-release.sh` (installed to the runtime home on first use, quarantine stripped, by `BridgeManager.EnsureVoiceRuntimeInstalled`).
- [x] **Fetch the model** (~142 MB) — the plugin downloads `ggml-base.en.bin` on first use and verifies its sha256 (`BridgeManager.EnsureVoiceModel` / `DownloadVoiceModel`). No manual step, no package bloat.
- [x] **Sign + notarize `ClaudeVoiceHelper.app`** — done via `tools/voice/sign-and-notarize.sh` (Developer ID + hardened runtime + mic entitlement; notarized & **stapled**; `spctl` → *accepted, source = Notarized Developer ID*).
- [x] Do **not** bundle ffmpeg/sox (GPL/LGPL). The runtime uses AVFoundation; they're dev‑only.
- [x] Do **not** bundle `PluginApi.dll` or its dependency closure (ExCSS, Svg.\*, Newtonsoft.Json, YamlDotNet, …) — the host provides them at load time, and Marketplace QA rejects packages that ship them. Enforced by `<Private>false</Private>` on the `PluginApi` reference in the csproj (QA feedback, 2.0.0 submission).
- [x] Privacy policy and EULA publicly reachable — https://vizhi.dev/privacy/ and https://vizhi.dev/eula/, mirrored from [PRIVACY.md](PRIVACY.md) / [EULA.md](EULA.md). (Formal legal review of the EULA remains open.)
- [x] Test on the supported hardware (MX Creative Keypad) and on a **clean Mac** (no dev tools) — the 2.0.0 release gate ran on a fresh macOS account (see [docs/clean-install-test.md](docs/clean-install-test.md)); it caught that sideloaded installs never self-register, fixed in 2.0.0 by having the plugin register itself — and made moot in 2.2.0, when the plugin went universal and stopped registering anything.
- [x] Accept the Logitech Marketplace Developer Agreement (part of the first submission at marketplace.logitech.com/contribute).
- [x] Package as `ClaudeConsole_<ver>.lplug4` (`bash tools/voice/pack-release.sh <ver>`) and submit at marketplace.logitech.com/contribute — first submitted 2026‑08‑12 as 2.0.0. **2.2.0 verified against this list on 2026‑08‑30**: no `PluginApi.dll` or host closure in the package (0 matches), 21 MB, `HasNoApplication`, helper notarized + stapled (`spctl` → *accepted, source = Notarized Developer ID*), `whisper-cli` and all five ggml backends Developer‑ID signed under the hardened runtime, both whisper bundles carry a transcription smoke marker.
- [x] **Universal plugin** (#23, Logitech's decision 2026-08-28): `HasNoApplication` in the yaml, no `profiles/` in the package, and a `ClientApplication` subclass that overrides nothing (the service requires the class to exist; the yaml makes it bind nothing). The keypad layout is a download (`profiles/ClaudeConsole-Keypad.lp5`, `-Windows.lp5`) on the keypad layouts page (https://vizhi.dev/layouts/), which also carries their SHA-256s. Verify on the clean-machine pass: install → **nothing** on the keypad and **no** Claude Console entry in the Options+ strip (both correct), actions listed under "Claude Console Actions" → import the `.lp5` → full layout with Terminal frontmost. Also: install over an existing install and confirm the imported profile survives untouched.
- [x] **The settings.json edit is opt-in, reversible and disclosed** (#31). Nothing is written on install or load. One path edits `~/.claude/settings.json`: a deliberate press on a live key (the first press edits nothing — on macOS it opens a dialog mid-screen stating the exact change with **Not now** / **Turn on**, and posts a card in Options+; *Turn on* or a second press **within 15 s** does it). A long press on a live key is the way off (*Keep* / *Turn off*, or a second long press). A **notice is posted in Options+** (`OnPluginStatusChanged`, Warning + link — the only level that renders) the moment either writes; the badge clears on the next load. A long press (or `uninstall.sh --unwire`) removes only the plugin's entries, restores a chained status line, and leaves the Off marker; the backup is rolling (rewritten before every change, never a stale snapshot). The public SDK exposes no plugin-wide settings page (`PluginPreferenceType` is `{None, Account}`, and the account control badges every key while signed out), so this is the closest discoverable control it offers — say so to QA and ask them to confirm. Verify on the clean-machine pass per [docs/clean-install-test.md](docs/clean-install-test.md) step 5.
- [x] **The cleanup script reaches users without the repo** (#45). `uninstall.sh` is embedded in the DLL and installed to `~/.claude/claude-console/scripts/` on every load — the package directory is deleted on uninstall, so it cannot live there. Verify: after uninstalling in Options+, `bash ~/.claude/claude-console/scripts/uninstall.sh --dry-run` lists the leftovers.

## Bundle whisper.cpp

`tools/voice/bundle-whisper.sh` already does the vendoring: it copies Homebrew's `whisper-cli` plus
its full dylib closure (`libwhisper`, `libggml`, `libggml-base`, `libomp`), rewrites every install
name / rpath to `@rpath` (resolved via `@loader_path`), signs, includes the whisper.cpp + ggml MIT
licenses, and verifies the result runs with Homebrew off the PATH. Output:
`~/.claude/claude-console/whisper-bin/` (≈2.4 MB). `BridgeManager.StartVoiceCapture` passes that path
via `--whisper`, and the helper's `findWhisper()` prefers it. For dev builds it ad‑hoc signs; for a
release, `sign-and-notarize.sh` exports `SIGN_IDENTITY` so the same code path signs with Developer ID
+ hardened runtime, then notarizes the bundle.

**Shipped in the package:** `tools/voice/pack-release.sh` copies the relocated, signed `whisper-bin/`
and the notarized helper into the packed tree under `bin/voice/`. On first voice use,
`BridgeManager.EnsureVoiceRuntimeInstalled` `ditto`s them into the runtime home and strips
`com.apple.quarantine` — so a package‑only install has working voice. (Loose Mach‑O can't be
*stapled*; stripping quarantine after install covers the offline Gatekeeper case, and the binaries
are notarized so the online check passes regardless.)

The ~142 MB model is **not** bundled — it downloads on first use (see the checklist above), which
keeps the package small and within any Marketplace size limit.

## Sign + notarize the voice helper — automated

`tools/voice/sign-and-notarize.sh` does the whole release flow (helper **and** whisper bundle):

```bash
# one-time: store a notarytool credential (App Store Connect API key or app-specific password)
xcrun notarytool store-credentials "claude-console-notary" --key … --key-id … --issuer …

# then, per release:
bash tools/voice/sign-and-notarize.sh
```

It signs with **Developer ID Application** + hardened runtime, submits both artifacts to
`xcrun notarytool submit --wait`, **staples** the helper (`.app` carries its ticket offline), and
verifies with `codesign`/`spctl`/`stapler`. Override `SIGN_IDENTITY` / `NOTARY_PROFILE` via env.

**Entitlements (required under the hardened runtime):**
- Helper — `tools/voice/helper.entitlements`: `com.apple.security.device.audio-input` (mic).
- `whisper-cli` — `tools/voice/whisper.entitlements`: `disable-library-validation` +
  `allow-unsigned-executable-memory` (+ `allow-jit`). whisper.cpp runs inference on the **GPU via
  Metal**; without these the hardened runtime aborts Metal init (`ggml_abort` in
  `ggml_backend_dev_init`) and every transcription comes back empty. Applied to the executable only.

A stable Developer‑ID identity also keeps the Microphone TCC grant from resetting on every rebuild
(ad‑hoc hashes rotate; the Developer‑ID hash is stable).

## Package

```bash
bash tools/voice/sign-and-notarize.sh          # Developer-ID sign + notarize helper + whisper
bash tools/voice/pack-release.sh 2.0.0         # build, embed voice, pack ClaudeConsole_2.0.0.lplug4
logiplugintool install ./ClaudeConsole_2.0.0.lplug4   # local test before submitting
```

`pack-release.sh` embeds the notarized voice payload (`bin/voice/`) so voice works from a
package-only install. Verify the `.lplug4` installs and runs on a **clean machine** (no dev tools,
no Homebrew), then submit.
