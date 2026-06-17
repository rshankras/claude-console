# Marketplace Submission Guide

Path to a submittable `.lplug4` for the [Logitech Marketplace](https://marketplace.logitech.com/contribute), per the [Actions SDK approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

## Pre‑submission checklist

- [x] `LoupedeckPackage.yaml` present in `src/package/metadata/` with a plugin icon (`Icon256x256.png`).
- [x] License is MIT (GPL is **not** allowed). whisper.cpp and the Whisper model are both MIT.
- [x] [PRIVACY.md](PRIVACY.md) (on‑device, no data leaves the machine) and a draft [EULA.md](EULA.md).
- [ ] **Fill `LoupedeckPackage.yaml`**: uncomment and set `supportPageUrl` and `homePageUrl` (e.g. the GitHub repo / issues page); consider a fuller `author`.
- [ ] **Bundle whisper.cpp** (see below) — end users won't have Homebrew's `whisper-cli`.
- [ ] **Bundle or fetch the model** (~142 MB) — decide bundle vs. download‑on‑first‑run (mind any package size limit).
- [ ] **Sign + notarize `ClaudeVoiceHelper.app`** (see below) — ad‑hoc signing won't pass Gatekeeper on other Macs.
- [ ] Do **not** bundle ffmpeg/sox (GPL/LGPL). The runtime uses AVFoundation; they're dev‑only. ✅
- [ ] Finalize EULA with counsel; confirm privacy policy is reachable via a valid URL.
- [ ] Test on the supported hardware (MX Creative Keypad) and on a **clean Mac** (no dev tools) to validate the bundled binaries and permission prompts.
- [ ] Accept the Logitech Marketplace Developer Agreement.
- [ ] Package as `ClaudeConsole_1_0.lplug4` and submit at marketplace.logitech.com/contribute (≈10 working days for review).

## Bundle whisper.cpp

The voice helper currently auto‑detects Homebrew's `whisper-cli`. For distribution, ship a binary inside the plugin:

1. Build a self‑contained `whisper-cli` from [whisper.cpp](https://github.com/ggerganov/whisper.cpp) (Metal enabled), or vendor the Homebrew binary plus its dynamic libs.
2. Place it under the packaged plugin (e.g. `bin/whisper/whisper-cli`) and include the whisper.cpp `LICENSE`.
3. Point the helper at the bundled path via the existing `--whisper` argument (set it in `BridgeManager.StartVoiceCapture`).
4. Ship the model alongside (e.g. `bin/whisper/ggml-base.en.bin`) or download it to `~/.claude/claude-console/whisper/` on first run, and include the model's MIT license.

## Sign + notarize the voice helper

`tools/voice/build.sh` ad‑hoc signs `ClaudeVoiceHelper.app`. For distribution:

1. Sign with a **Developer ID Application** certificate and the hardened runtime, declaring the microphone entitlement:
   ```bash
   codesign --force --options runtime \
     --entitlements tools/voice/helper.entitlements \
     --sign "Developer ID Application: Your Name (TEAMID)" \
     ClaudeVoiceHelper.app
   ```
   (`helper.entitlements` should grant `com.apple.security.device.audio-input`.)
2. **Notarize** the bundle (`xcrun notarytool submit … --wait`) and `xcrun stapler staple` it.
3. A stable Developer‑ID identity also keeps the Microphone grant from resetting on every rebuild (ad‑hoc hashes rotate).

## Package

```bash
cd src && dotnet build -c Release
logiplugintool pack ./bin/Release ./ClaudeConsole_1_0.lplug4
logiplugintool install ./ClaudeConsole_1_0.lplug4   # local test before submitting
```

Verify the `.lplug4` installs and runs on a clean machine, then submit.
