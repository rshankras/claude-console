#!/usr/bin/env python3
"""Run the release packer's AX staging block, with real compilation/signing and no install."""
from pathlib import Path
import hashlib
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
script = (root / 'tools/voice/pack-release.sh').read_text()
start = script.index('PKG_DESKTOP=')
end = script.index('# --- pack ', start)
block = script[start:end]
runtime = Path.home() / '.claude/claude-console/VizhiAxBridge'
def runtime_hash():
    return hashlib.sha256(runtime.read_bytes()).hexdigest() if runtime.exists() else None
before = runtime_hash()
with tempfile.TemporaryDirectory(prefix='vizhi-package-test-') as temp:
    build = Path(temp) / 'Release'
    staged = build / 'bin/desktop/VizhiAxBridge'
    staged.parent.mkdir(parents=True)
    staged.write_text('stale helper must not ship')
    env = dict(os.environ, ROOT=str(root), BUILD_DIR=str(build), SHIPS_DESKTOP='1', SIGN_IDENTITY='-')
    subprocess.run(['bash', '-euc', block], env=env, check=True)
    subprocess.run(['codesign', '--verify', '--strict', str(staged)], check=True)
    help_text = subprocess.check_output([str(staged), '--help'], text=True)
    assert 'VizhiAxBridge' in help_text
    assert b'composer-target-changed' in staged.read_bytes()
    assert b'ambiguous-conversation' in staged.read_bytes()
    assert runtime_hash() == before, 'release staging changed the installed helper'
print('Desktop release helper rebuilt, signed, and staged without changing runtime')
