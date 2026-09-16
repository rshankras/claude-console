# Windows input serialization (#89)

Every delivery owns a named mutex keyed by PID and exact UTC process start ticks, including
identity revalidation, console attachment, text, settling delay and Return. Separate helper
processes and products use the same name for the same console. Separate targets do not share a
lock. Uncontended requests introduce no extra sleep or polling. Ordering among competing
requests is unspecified; intentional repeated requests are not deduplicated.

Waiting is bounded to 1.5 seconds. Timeout returns delivery failure, not successful typing.
The helper rechecks process identity after acquiring the lock. Detectable abandoned ownership
is also rejected because the preceding helper might have left partial text. This is not a
persistent crash-recovery journal: if all handles have vanished, an OS mutex cannot tell a later
request that a prior helper died. Inspect/clear the composer after interrupted delivery.

Run the normal suite on Windows, or:

```powershell
python tests/scripts/test-windows-input.py
```

The test builds the current toolkit, creates two isolated test consoles, concurrently delivers
three identical `/cost` commands to one and three distinct commands to the other, and checks
complete lines and stale/exited PID rejection. Both test consoles exit afterward; a failure
kills only the processes created by the test. It requires Python and .NET 10 SDK, as does the
existing test suite. To verify a packaged toolkit use `--helper C:\path\claude-console-tools.exe`.

`test-input-lock.py` separately launches 12 competing processes and measures lock overhead;
SessionInputLockTests cover timeout, independent generations/targets, handoff and abandonment.
These portable tests do not establish actual Windows console or sandbox behavior.

Physical Windows checks still required: rapid three-press Cost/Context, distinct prompts,
repeated arrows in a picker, Yes/No during approvals, simultaneous sessions, normal/sandboxed
callers, target exit while a request waits, and comparison of uncontended helper latency.
The original #89 closure requires this device evidence. No changed input path has been installed
by this implementation work.
