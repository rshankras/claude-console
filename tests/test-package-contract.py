#!/usr/bin/env python3
"""Run artifact regression checks: python3 tests/test-package-contract.py <verified .lplug4>."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import zipfile


def main():
    package = Path(sys.argv[1]).resolve()
    verifier = Path(__file__).resolve().parents[1] / 'tools/verify-package.sh'
    env = dict(os.environ, CC_VERIFY_OFFLINE='1')  # content tests; release verification checks links

    def verify(path, expected_error=None):
        result = subprocess.run(['bash', str(verifier), str(path)], env=env,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if expected_error is None:
            assert result.returncode == 0, result.stdout
        else:
            assert result.returncode != 0 and expected_error in result.stdout, result.stdout

    verify(package)
    print('PASS: original package accepted')
    # Preserve every entry and attribute except the deliberate defect. Test the actual release
    # verifier against the actual bundled executable, not a mocked file list or source strings.
    cases = [
        ('no-toolkit', 'bin/claude-console-tools.exe', None, 'missing and no shared toolkit'),
        ('no-hook', 'bin/claude-console-hook.exe', None, 'missing — live status cannot work'),
        ('redundant-runtime', None, 'bin/claude-console-inject.exe', 'duplicates the shared toolkit'),
    ]
    with tempfile.TemporaryDirectory(prefix='vizhi-package-test-') as scratch:
        for label, removed, added, error in cases:
            target = Path(scratch) / (label + '.lplug4')
            with zipfile.ZipFile(package) as source, zipfile.ZipFile(target, 'w') as dest:
                for entry in source.infolist():
                    if entry.filename.replace('\\', '/') != removed:
                        dest.writestr(entry, source.read(entry))
                if added:
                    toolkit = next(e for e in source.infolist()
                                   if e.filename.replace('\\', '/') == 'bin/claude-console-tools.exe')
                    dest.writestr(added, source.read(toolkit), compress_type=zipfile.ZIP_DEFLATED)
            verify(target, error)
            print('PASS: rejected ' + label)


if __name__ == '__main__':
    main()
