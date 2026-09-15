# Windows acceptance fixes — 15 September 2026

## Plan and scope

1. Supply startup project directories before the first Codex prompt.
2. Reproduce intermittent focus failures and preserve verified session targeting.
3. Route keypad Escape to active screenshot cancellation.
4. Improve project-name speech decoding using discovered names, without relaxing destination matching.
5. Run regression/runtime checks, build both products and package a separately identified Windows preview.

## Changes and evidence

### Startup labels

Windows now supplies the existing SessionDirectories seam from a bounded read of each live CLI process's working directory. The lookup verifies process creation time, rejects cross-bitness/malformed data, and never falls back to another process's folder. Entries are refreshed and pruned with process discovery. The session registry can therefore label a fresh process before hooks or rollout metadata arrive.

The native reader follows the PEB and RTL_USER_PROCESS_PARAMETERS layouts documented in the [System Informer native headers](https://github.com/winsiderss/phnt/blob/master/ntrtl.h). This is best-effort native metadata: access denial or unsupported process architecture retains the generic label. Process identity and actual current-process directory reads passed tests. Physical startup-label retest on the installed candidate remains required. This does not claim an immediate post-fork session-ID metadata fix.

### Terminal focus

Old helper: two of eight live checks failed, both against the busy session. Diagnostics showed its animated title overwriting the identity marker before UI Automation observed it. The new helper refreshes the marker throughout the challenge and stops the refresher before restoring the title. It goes directly to identity verification, reducing redundant title searches. Candidate selection still requires the exact random marker, with rollback on failure and serialized focus requests.

Intermediate helper: 12/12 live checks passed. Final helper: 8/8 passed across four processes, including the busy one. Final measured wall times: 530–1,457 ms. These are local functional measurements, not a performance guarantee.

### Screenshot Escape

The Windows bridge owns one cancellation event per active capture. Escape consumes that event rather than sending an interrupt to the terminal. A second concurrent capture is rejected. A cancelled operation cannot deliver a racing successful image result.

The initial posted-key approach failed on the actual picker and was replaced. The final helper sends WM_CLOSE only to the observed capture window, then checks disappearance. The actual helper logged `capture picker dismissed`, exited, and created no image. The owner confirmed it closed by itself. Keyboard cancellation watches visible overlay windows rather than waiting for a persistent SnippingTool process to exit.

### Project voice

Project recording passes a bounded vocabulary of discovered project-folder names through the helper to whisper-cli's supported --prompt option. Ordinary Dictate and Draft are unaffected. Exact/ambiguous destination matching is unchanged; the code does not hardcode a guessed replacement for the misheard phrases.

Vocabulary sanitization, deduplication and bounds passed tests. Installed whisper-cli --help confirms the option. Recognition of the owner's actual phrase still needs a microphone retest on the candidate; no claim of a demonstrated accuracy improvement yet.

## Validation

- 1,158 C# tests passed, zero failures/skips.
- Both product Release builds passed with zero warnings/errors, SkipPluginLink=true.
- Five helper executable-only startup checks passed.
- Real input fixture: two consoles, three complete concurrent prompts each; wrong process generations and exited processes rejected.
- Final focus helper: 8/8 live checks passed, following 12/12 on the intermediate revision.
- Actual screenshot cancellation: no output file; helper verified window dismissal; owner confirmed.
- Package regression/verification results are included with the preview.

Existing toolkit COM trimming warning remains. NuGet vulnerability metadata could not be fetched even after an unrestricted retry; compilation/publishing succeeded, but no fresh vulnerability audit is claimed. Initial sandbox test-runner environment/SDK failures were rerun successfully outside the sandbox with .NET 10. Mac signing/notarization and Mac device checks were not run.

Changes remain uncommitted on fix/vizhi-backlog-followup, based on 075529f. Keep the existing profile. Hooks and bundled whisper/model assets are preserved; plugin DLL and shared Windows toolkit are rebuilt.

## Additional final verification

The native directory reader was also run against all four live Codex CLI processes: PID 1860 -> claude-console; PID 7452 -> the user home directory (fresh, no first prompt); PID 13872 -> mx-keypad-launch-demo; PID 22304 -> gh-portable. All four reads succeeded. The fresh session was launched in the home directory, so its correct startup label is the home folder, not MX.

All four package contract cases passed: valid package accepted; missing toolkit, missing hook and redundant runtime packages rejected. Package SHA256: 7d2f940c3137ab1fa6a6d4ef13bf6266a1fa2cb7fa2c2905522c010de9e26dd3.

## Installed voice retest

Owner installed the preview (DLL SHA256 verified) and confirmed Go to Project worked. At 17:25:53 IST the hinted transcript `mxkeeper launch demo` matched the correct mx-keypad-launch-demo directory among 16 candidates. This closes the previously outstanding successful project-selection retest for this phrase; it does not establish verbatim recognition or general speech accuracy.
