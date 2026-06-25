# Marketplace Submission Guide

Path to a submittable `.lplug4` for the [Logitech Marketplace](https://marketplace.logitech.com/contribute), per the [Actions SDK approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

## Pre‑submission checklist

- [x] `LoupedeckPackage.yaml` present in `src/package/metadata/` with a plugin icon (`Icon256x256.png`).
- [x] License is MIT (GPL is **not** allowed). whisper.cpp and the Whisper model are both MIT.
- [x] [PRIVACY.md](PRIVACY.md) (on‑device, no data leaves the machine) and a draft [EULA.md](EULA.md).
- [ ] **Fill `LoupedeckPackage.yaml`**: uncomment and set `supportPageUrl` and `homePageUrl` (e.g. the GitHub repo / issues page); consider a fuller `author`.
- [~] **Bundle whisper.cpp** (see below) — `tools/voice/bundle-whisper.sh` vendors Homebrew's `whisper-cli` + its dylib closure into a self‑contained `~/.claude/claude-console/whisper-bin/`, now **Developer‑ID signed + hardened‑runtime + notarized** via `tools/voice/sign-and-notarize.sh`. **Remaining:** ship it *inside* the `.lplug4` instead of the runtime home.
- [x] **Fetch the model** (~142 MB) — the plugin downloads `ggml-base.en.bin` on first use and verifies its sha256 (`BridgeManager.EnsureVoiceModel` / `DownloadVoiceModel`). No manual step, no package bloat.
- [x] **Sign + notarize `ClaudeVoiceHelper.app`** — done via `tools/voice/sign-and-notarize.sh` (Developer ID + hardened runtime + mic entitlement; notarized & **stapled**; `spctl` → *accepted, source = Notarized Developer ID*).
- [ ] Do **not** bundle ffmpeg/sox (GPL/LGPL). The runtime uses AVFoundation; they're dev‑only. ✅
- [ ] Finalize EULA with counsel; confirm privacy policy is reachable via a valid URL.
- [ ] Test on the supported hardware (MX Creative Keypad) and on a **clean Mac** (no dev tools) to validate the bundled binaries and permission prompts.
- [ ] Accept the Logitech Marketplace Developer Agreement.
- [ ] Package as `ClaudeConsole_1_0.lplug4` and submit at marketplace.logitech.com/contribute (≈10 working days for review).

## Bundle whisper.cpp

`tools/voice/bundle-whisper.sh` already does the vendoring: it copies Homebrew's `whisper-cli` plus
its full dylib closure (`libwhisper`, `libggml`, `libggml-base`, `libomp`), rewrites every install
name / rpath to `@rpath` (resolved via `@loader_path`), signs, includes the whisper.cpp + ggml MIT
licenses, and verifies the result runs with Homebrew off the PATH. Output:
`~/.claude/claude-console/whisper-bin/` (≈2.4 MB). `BridgeManager.StartVoiceCapture` passes that path
via `--whisper`, and the helper's `findWhisper()` prefers it. For dev builds it ad‑hoc signs; for a
release, `sign-and-notarize.sh` exports `SIGN_IDENTITY` so the same code path signs with Developer ID
+ hardened runtime, then notarizes the bundle.

**Remaining for the shipping build — move into the package:** copy the relocated, signed
`whisper-bin/` into the `.lplug4` (e.g. under `bin/whisper/`) so a package‑only install has it, and
point `BundledWhisperCli` at the in‑package path. Today it installs to the runtime home alongside the
helper, which isn't shipped in the package yet either. Note: loose Mach‑O can't be *stapled* — once
inside the `.lplug4`/`.app`, strip quarantine on install or rely on the enclosing stapled container
for the offline Gatekeeper case.

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
cd src && dotnet build -c Release
logiplugintool pack ./bin/Release ./ClaudeConsole_1_0.lplug4
logiplugintool install ./ClaudeConsole_1_0.lplug4   # local test before submitting
```

Verify the `.lplug4` installs and runs on a clean machine, then submit.
