# Windows Codex hook timeout fix — 2026-09-15

## Finding

Installed Vizhi hooks have a five-second Codex deadline, except SessionEnd (three seconds).
The helper's watchdog waited eight seconds. Its parent and interpreter lookups could
start PowerShell/CIM, whose individual waits allowed four seconds. Those paths can
outlive the caller's deadline. The invocation log does not identify which phase stalled
in the reported incident; this fixes confirmed timeout hazards without claiming a trace
of that specific failure.

## Change

- Codex invocations arm a two-second watchdog before diagnostics or process enumeration.
- Codex parent traversal uses native process queries exclusively. It requests limited
  query access instead of the broader access obtained by `Process.Handle`.
- Node/Bun/Deno/npx command lines use a bounded native query buffer, preserving Unicode.
  A failed or unsupported query returns no match. Codex does not fall back to PowerShell.
- Claude's watchdog and WMI fallback remain available. Shared event output still precedes
  Codex session lookup.

Native command-line layout: [System Informer headers](https://github.com/winsiderss/phnt/blob/master/ntpsapi.h).

## Validation and limits

Tests cover native Unicode command-line lookup, nonexistent/exited processes, and a
published Codex SessionEnd hook with stdin held open completing within three seconds
and writing its event. Full-suite and package results are recorded beside the package.

The watchdog begins at managed entry: delays starting PowerShell or loading the runtime
before that point remain outside its bound. A timeout may drop an update. Installed
hardware acceptance remains limited to the retest recorded below.
The screenshot picker's default-profile switch remains a separate open issue.

The preview package is based on the acceptance-fixes build and replaces only the hook
executable. Existing profiles can be retained.

## Results

- Full C# suite: 1,160 passed, zero failed or skipped.
- Final rebuilt executable: 54 hook regression tests passed.
- Package: all four contract checks passed; only the hook executable differs from the prior preview.
- Publish succeeded with existing Windows platform analyzer warnings; NuGet audit was not refreshed.


## Installed retest

The installed hook SHA256 matched the rebuilt executable (B0F561D67E291C1F06FB5C3CEC16C8C0D6956AFEE49ADD42AEAF3C7AF25A787A). Prompt and tool events wrote hook-transport state for the correct session. The owner reported no Hook failed message during this check. This is one successful installed retest, not a prolonged soak test.

The owner also confirmed that keyboard Escape closes the screenshot picker and the keypad automatically returns to Vizhi. While the picker is open, the default profile still hides the Vizhi Escape key; that workflow remains unresolved.
