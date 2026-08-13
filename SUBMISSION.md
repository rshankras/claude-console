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
- [x] Test on the supported hardware (MX Creative Keypad) and on a **clean Mac** (no dev tools) — the 2.0.0 release gate ran on a fresh macOS account (see [docs/clean-install-test.md](docs/clean-install-test.md)); it caught that sideloaded installs never self-register, fixed in 2.0.0.
- [x] Accept the Logitech Marketplace Developer Agreement (part of the first submission at marketplace.logitech.com/contribute).
- [x] Package as `ClaudeConsole_<ver>.lplug4` (`bash tools/voice/pack-release.sh <ver>`) and submit at marketplace.logitech.com/contribute — first submitted 2026‑08‑12 as 2.0.0.

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
