# Windows package size optimization

Four interactive Windows helpers now ship as `claude-console-tools.exe`, selected by the
first argument: `inject`, `focus`, `voice`, or `shot`. Each invocation remains a separate
process with its existing deadlines and behavior. The frequently invoked
`claude-console-hook.exe` stays separate, preserving its watchdog and process cap.
Standalone helper projects remain available for diagnostics. Plugin lookup prefers the
shared toolkit and falls back to standalone executables when the toolkit is absent.

This removes three redundant self-contained .NET runtimes from the download. Both platforms’
voice libraries remain bundled, and the approximately 142 MB speech model still downloads
on first use. No new network dependency or runtime installation is introduced.

## Measured package

| Artifact | Download size |
| --- | ---: |
| Existing ClaudeConsole_2.2.2.lplug4 | 33,462,914 bytes / 31.91 MiB |
| Optimized preview | 17,268,149 bytes / 16.47 MiB |
| Reduction | 16,194,765 bytes / 48.4% |

The preview was packed with `logiplugintool` using the existing package’s voice payload and
metadata, replacing the plugin DLL/PDB and Windows helpers with Release builds from this
branch. Its package hash was regenerated. It is a size/validation candidate, not a published
release; its metadata still says 2.2.2. Neither the installed plugin nor runtime was changed.

Compressed contents: toolkit 5.64 MiB, hook 5.19 MiB, voice payload 4.97 MiB, plugin and
metadata 0.66 MiB. `python3 tools/package-size.py <package> --compare <baseline>` reports
these measurements. The release pack script now prints a breakdown automatically.

The release build stages only the hook and toolkit, removes superseded generated helpers,
and the verifier rejects redundant standalone helpers beside a toolkit or a toolkit missing
its command-contract marker. This marker is a packaging check, not proof of Windows behavior.

## Validation and release follow-up

- Release plugin build and Windows x64 toolkit publish succeeded.
- `bash tests/run-all.sh`: 1,035 C# tests passed, 11 skipped; all shell suites passed.
- Preview passed `tools/verify-package.sh`, including signatures and three public links.
  The existing warning about 2.2.2 remaining under Unreleased still applies.
- Added argument-dispatch and helper-resolution regression coverage. The Windows standalone
  smoke script now exercises the toolkit manifest and all four verbs with runtime search
  overrides pointing to an empty directory.
- Windows smoke and device tests have not run for this optimization on this Mac. Before
  release, publish the toolkit and run `tests/run-all.ps1 -Strict`, then verify typing,
  tab switching, microphone readiness/cancellation/transcription, and screenshot capture
  on Windows with the packaged build. Prior Windows results predate this consolidation.

The normal release path remains `tools/voice/pack-release.sh`: it requires the separately
smoke-tested, notarized voice payloads. The preview reused those from the baseline only
for a controlled size comparison. Choose a release version and perform the Windows checks
before publishing a release package.
