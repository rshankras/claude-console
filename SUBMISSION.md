# Marketplace Submission Guide

Path to a submittable `.lplug4` for the [Logitech Marketplace](https://marketplace.logitech.com/contribute), per the [Actions SDK approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

**Status:** `ClaudeConsole_2.0.0.lplug4` submitted 2026‑08‑12; QA flagged the bundled `PluginApi.dll` (host-provided assembly, must not ship). Resubmitted as `ClaudeConsole_2.0.1.lplug4` on 2026‑08‑13 with `<Private>false</Private>` on the reference (review takes ≈10 working days). The exact listing copy entered in the form (teaser, description, release notes) is kept for reuse in [docs/marketplace-listing.md](docs/marketplace-listing.md).

## Pre‑submission checklist

- [x] `LoupedeckPackage.yaml` present in `src/package/metadata/` with a plugin icon (`Icon256x256.png`).
- [x] License is MIT (GPL is **not** allowed). whisper.cpp and the Whisper model are both MIT.
- [x] [PRIVACY.md](PRIVACY.md) (on‑device, no data leaves the machine) and a draft [EULA.md](EULA.md).
- [x] **Fill `LoupedeckPackage.yaml`**: `supportPageUrl` (GitHub issues), `homePageUrl` (GitHub repo), and `author` (S.Ravi Shankar) are all set.
- [x] **Bundle whisper.cpp** (see below) — `tools/voice/bundle-whisper.sh` vendors Homebrew's `whisper-cli` + its dylib closure into a self‑contained `~/.claude/claude-console/whisper-bin/`, **Developer‑ID signed + hardened‑runtime + notarized** via `tools/voice/sign-and-notarize.sh`, and **shipped inside the `.lplug4`** by `tools/voice/pack-release.sh` (installed to the runtime home on first use, quarantine stripped, by `BridgeManager.EnsureVoiceRuntimeInstalled`).
- [x] **Fetch the model** (~142 MB) — the plugin downloads `ggml-base.en.bin` on first use and verifies its sha256 (`BridgeManager.EnsureVoiceModel` / `DownloadVoiceModel`). No manual step, no package bloat.
- [x] **Sign + notarize `ClaudeVoiceHelper.app`** — done via `tools/voice/sign-and-notarize.sh` (Developer ID + hardened runtime + mic entitlement; notarized & **stapled**; `spctl` → *accepted, source = Notarized Developer ID*).
- [x] Do **not** bundle ffmpeg/sox (GPL/LGPL). The runtime uses AVFoundation; they're dev‑only.
- [x] Do **not** bundle `PluginApi.dll` or its dependency closure (ExCSS, Svg.\*, Newtonsoft.Json, YamlDotNet, …) — the host provides them at load time, and Marketplace QA rejects packages that ship them. Enforced by `<Private>false</Private>` on the `PluginApi` reference in the csproj (QA feedback, 2.0.0 submission).
- [x] Privacy policy and EULA publicly reachable — [PRIVACY.md](PRIVACY.md) / [EULA.md](EULA.md) in the GitHub repo. (Formal legal review of the EULA remains open.)
- [x] Test on the supported hardware (MX Creative Keypad) and on a **clean Mac** (no dev tools) — the 2.0.0 release gate ran on a fresh macOS account (see [docs/clean-install-test.md](docs/clean-install-test.md)); it caught that sideloaded installs never self-register, fixed in 2.0.0 by having the plugin register itself — and made moot in 2.2.0, when the plugin went universal and stopped registering anything.
- [x] Accept the Logitech Marketplace Developer Agreement (part of the first submission at marketplace.logitech.com/contribute).
- [x] Package as `ClaudeConsole_<ver>.lplug4` (`bash tools/voice/pack-release.sh <ver>`) and submit at marketplace.logitech.com/contribute — first submitted 2026‑08‑12 as 2.0.0.
- [x] **Universal plugin** (#23, Logitech's decision 2026-08-28): `HasNoApplication` in the yaml, no `profiles/` in the package, and a `ClientApplication` subclass that overrides nothing (the service requires the class to exist; the yaml makes it bind nothing). The keypad layout is a download (`profiles/ClaudeConsole-Keypad.lp5`, `-Windows.lp5`) attached to the GitHub release and linked from the listing. Verify on the clean-machine pass: install → **nothing** on the keypad and **no** Claude Console entry in the Options+ strip (both correct), actions listed under "Claude Console Actions" → import the `.lp5` → full layout with Terminal frontmost. Also: install over an existing install and confirm the imported profile survives untouched.
- [x] **The settings.json edit is opt-in, reversible and disclosed** (#31). Nothing is written on install or load. Two paths edit `~/.claude/settings.json`, both on a deliberate press: a live key pressed **twice within 15 s** (the first press edits nothing — it posts a card in Options+ stating the exact change a second press makes), or the labelled **Enable Live Status** key (group *Setup & Privacy*; on the layout download beside Cost), whose description states the change. A **notice is posted in Options+** (`OnPluginStatusChanged`, Warning + link — the only level that renders) the moment either writes; the badge clears on the next load. **Disable Live Status** (or `uninstall.sh --unwire`) removes only the plugin's entries, restores a chained status line, and leaves the Off marker; the backup is rolling (rewritten before every change, never a stale snapshot). The public SDK exposes no plugin-wide settings page (`PluginPreferenceType` is `{None, Account}`, and the account control badges every key while signed out), so this is the closest discoverable control it offers — say so to QA and ask them to confirm. Verify on the clean-machine pass per [docs/clean-install-test.md](docs/clean-install-test.md) step 5.
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
