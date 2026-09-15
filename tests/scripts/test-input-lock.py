#!/usr/bin/env python3
"""Real helper-process contention; no console/device or live session is touched."""
import os
from pathlib import Path
import subprocess
import tempfile
import time
root = Path(__file__).resolve().parents[2]
project = root / 'tests/fixtures/InputLockProbe/InputLockProbe.csproj'
subprocess.run(['dotnet', 'build', str(project), '--nologo', '--verbosity', 'quiet'], check=True)
probe = project.parent / 'bin/Debug/net10.0/InputLockProbe.dll'
with tempfile.TemporaryDirectory(prefix='vizhi-input-lock-') as tmp:
    log = Path(tmp) / 'delivery.log'
    generation = str(time.time_ns())
    children = [subprocess.Popen(['dotnet', str(probe), str(os.getpid()), generation, str(log), str(n)]) for n in range(12)]
    assert all(child.wait(timeout=15) == 0 for child in children)
    lines = log.read_text().splitlines()
    assert len(lines) == 24, lines
    for n in range(0, len(lines), 2):
        assert lines[n].endswith(' text') and lines[n + 1] == lines[n].replace(' text', ' enter'), lines
    assert len({line.split()[0] for line in lines}) == 12
print('PASS: 12 independent helper processes delivered complete text/Enter pairs without loss or interleaving')

subprocess.run(['dotnet', str(probe), '--benchmark'], check=True)
