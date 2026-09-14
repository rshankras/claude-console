#!/usr/bin/env python3
"""Exercise actual WriteConsoleInput against two disposable consoles, never a user session."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import time
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--helper', help='Published claude-console-tools.exe; otherwise publish current source')
args = parser.parse_args()
if os.name != 'nt':
    print('SKIP: actual Windows console injection requires Windows')
    raise SystemExit(0)
root = Path(__file__).resolve().parents[2]
project = root/'tests/fixtures/ConsoleInputProbe/ConsoleInputProbe.csproj'
subprocess.run(['dotnet', 'build', str(project), '--nologo', '--verbosity', 'quiet'], check=True)
probe = project.parent/'bin/Debug/net10.0/ConsoleInputProbe.dll'
with tempfile.TemporaryDirectory(prefix='vizhi-console-injection-') as tmp:
    tmp = Path(tmp)
    helper = Path(args.helper).resolve() if args.helper else tmp/'helper/claude-console-tools.exe'
    if not args.helper:
        subprocess.run(['dotnet', 'publish', str(root/'tools/windows/ClaudeConsoleTools/ClaudeConsoleTools.csproj'),
                        '-c', 'Release', '-r', 'win-x64', '-o', str(helper.parent)], check=True)
    processes = []
    try:
        sessions = []
        for index in range(2):
            ready, log = tmp/f'{index}.ready', tmp/f'{index}.log'
            child = subprocess.Popen(['dotnet', str(probe), str(ready), str(log), '3'])
            processes.append(child)
            deadline = time.monotonic() + 15
            while not ready.exists() or not ready.read_text():
                if child.poll() is not None or time.monotonic() > deadline:
                    raise AssertionError('isolated console did not become ready')
                time.sleep(.05)
            ticks = ready.read_text()
            sessions.append((child, ticks, log))
        requests = []
        started = time.monotonic()
        for index, (child, ticks, log) in enumerate(sessions):
            for n in range(3):
                request = subprocess.Popen([str(helper), 'inject', 'text', '--pid', str(child.pid),
                    '--start-ticks', ticks, '--submit', 'true', '--text', '/cost' if index == 0 else f'/probe-{index}-{n}'])
                processes.append(request)
                requests.append(request)
        assert all(p.wait(timeout=15) == 0 for p in requests), 'a delivery failed'
        for index, (child, ticks, log) in enumerate(sessions):
            assert child.wait(timeout=15) == 0
            expected = ['/cost'] * 3 if index == 0 else [f'/probe-{index}-{n}' for n in range(3)]
            assert sorted(log.read_text().splitlines()) == expected, log.read_text()
            # Exited/recycled identity must never be considered a successful delivery.
            result = subprocess.run([str(helper), 'inject', 'text', '--pid', str(child.pid),
                '--start-ticks', str(int(ticks) + 1), '--submit', 'true', '--text', '/must-not-deliver'], timeout=15)
            assert result.returncode == 2, result.returncode
        print(f'PASS: two isolated consoles, three whole concurrent prompts each; exited/stale targets rejected ({time.monotonic()-started:.2f}s)')
    finally:
        for child in processes:
            if child.poll() is None:
                child.kill()
            child.wait(timeout=10)
