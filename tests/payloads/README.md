# Captured agent payloads

What the agents actually hand our hooks on stdin, one file per event, frozen at a version. The
contract tests (`WindowsHookContractTests`, the shell suites under `scripts/`) feed these to the
REAL hook — the published exe on Windows, the bash scripts on macOS — and read the result back
with the real reader. They test our reading of the contract, not the agent: when an agent changes a
payload's shape, re-record the file and let the diff be the review.

| File | Provenance |
|---|---|
| `claude-code/PermissionRequest.json` | The payload Logitech QA's Windows retest captured for item 2 (#74): a recursive force-delete proposed by the PowerShell tool. Paths anonymised. |
| `claude-code/Status.json` | The status-line payload recorded in `StatePayloadTests` (Claude Code 2.1.223), with Windows paths and in-flight figures. |

Re-recording: wire a hook that copies its stdin to a file (`cat > /tmp/payload.json` on macOS,
`cmd /c more > %TEMP%\payload.json` on Windows), trigger the event once, anonymise the paths, and
replace the file here.
