# Developing Claude Console

Contributor rules and the non-obvious traps are in [CLAUDE.md](../CLAUDE.md); the architecture (one engine, one package per agent) in [multi-agent-architecture.md](multi-agent-architecture.md). This page is the build / test / package sequence.

## Requirements

- **.NET 10 SDK** (the Logi Plugin Service 6.4 runtime is .NET 10; `PluginApi.dll` comes from the host and is never bundled).
- **Logi Plugin Tool** — `dotnet tool install --global LogiPluginTool`. It targets .NET 8, so run it with `DOTNET_ROLL_FORWARD=LatestMajor`.
- For voice: **whisper.cpp** (`brew install whisper-cpp`) — needed only to *build* the bundled `whisper-cli`. The speech model downloads automatically on first use.

## Repository layout

```
src/Core/            the engine — platform bridges, session grid, file IPC, actions,
                     voice, key rendering, risk classifier
src/Core/Agents/     IAgentAdapter, AgentCapabilities (the contract)
src/Agents/<agent>/  one adapter per agent: ClaudeCode, CodexCli
src/Products/<name>/ a thin product: plugin class, package metadata, icon → one .lplug4
profiles/            the importable keypad layouts (.lp5) — the plugin is universal and ships none
tests/               one suite covering the engine and every agent
scripts/             the shell writers the agents run (statusline, activity, codex hook)
tools/               icon pipeline, profile sync, voice bundle, packaging
```

Products version and release independently — Claude Console and **Vizhi for Codex** build from the same core.

## Build from source

```bash
# 1. Build the plugin — links + hot-reloads into the Logi Plugin Service
cd src/Products/ClaudeConsole
dotnet build -c Debug

# 2. Build the voice helper AND bundle a self-contained whisper-cli
#    (both installed to ~/.claude/claude-console; no Homebrew needed at runtime)
cd ../../..
bash tools/voice/build.sh
```

`dotnet build` **mutates your live Logi install**: it writes a dev `.link` into the Logi plugin directory and restarts the service. To build without touching the installed plugin add `-p:SkipPluginLink=true`; to only check compilation use `-t:Compile`. Never keep a dev `.link` *and* an installed `.lplug4` — the service sees the plugin twice and refuses the duplicate.

The ~142 MB `base.en` whisper model is fetched automatically (and checksum-verified) the first time you press Dictate. To pre-seed it, drop `ggml-base.en.bin` at `~/.claude/claude-console/whisper/`.

## Tests

```bash
bash tests/run-all.sh
```

Runs the C# unit tests (xUnit — injection guard, IPC file permissions, stale-file pruning, TTY normalisation, voice project matching, answer-key decisions, the keypad layout guard) and the bridge script tests (the bash writers, checked against a temp IPC root for paths, payloads and `0700`/`0600` permissions). Safe to run at any time: the test project builds with `SkipPluginLink=true`, so a run never writes the dev `.link` or reloads the service, and leaves a canary proving the live IPC root survived.

## Icons and profiles

- Icons are the designer's SVG pack rendered to 96 px PNGs by `swift tools/convert-designer-icons.swift` (run from the repo root). Neutral glyphs are tinted to Claude's copper; state icons keep their colour; the Yes/No glyphs render white for their coloured tiles. The SDK cannot tint at draw time, so colour is fixed here.
- The importable layouts in `profiles/` carry the approved first page; `python3 tools/sync-default-profiles.py` rewrites page one (and the preview) of both, and `tests/KeypadLayoutTests.cs` guards the result. The Windows profile derives from the Keypad one via `tools/windows/make-windows-profile.sh`.

## Building & packaging

`tools/voice/build.sh` builds the voice helper + bundles a self‑contained `whisper-cli` (ad‑hoc signed for dev); `tools/voice/sign-and-notarize.sh` produces the Developer‑ID‑signed, notarized release build. The release package:

```bash
DOTNET_ROLL_FORWARD=LatestMajor bash tools/voice/pack-release.sh <version> [Product]
```

It refuses to pack without a smoke-tested **Windows** whisper bundle (`WINDOWS_WHISPER_DIR`, default `~/.claude/claude-console/whisper-bin-win`, carrying a `TRANSCRIPTION_SMOKE_OK` marker produced *on Windows*) and without a macOS bundle that has actually transcribed. Version lives in two files per product (the csproj and `LoupedeckPackage.yaml`) and they must agree. Marketplace packaging, signing and notarization: [SUBMISSION.md](../SUBMISSION.md). Releases go to GitHub Releases with the `.lp5` layouts alongside the `.lplug4`.
