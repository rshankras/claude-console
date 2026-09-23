#!/usr/bin/env python3
"""Exercise actual web AX flattening against our fixed disposable fixture only."""
import json
import os
from pathlib import Path
import plistlib
import signal
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
FIXTURE_ID = 'com.vizhi.desktop.testfixture'


def run(directory):
    directory = directory.resolve()
    directory.mkdir(parents=True, exist_ok=True)
    bundle = directory / 'VizhiWebFixture.app'
    executable = bundle / 'Contents/MacOS/VizhiWebFixture'
    executable.parent.mkdir(parents=True, exist_ok=True)
    (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(dict(
        CFBundleIdentifier=FIXTURE_ID, CFBundleName='Vizhi Test Fixture',
        CFBundleExecutable='VizhiWebFixture', CFBundlePackageType='APPL', CFBundleVersion='1')))
    helper = directory / 'VizhiAxBridge'
    steps = []
    with (directory / 'fixture.log').open('w') as log:
        def command(args):
            subprocess.run(list(map(str, args)), cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=90)

        def ax(verb):
            args = [helper, verb, '--app', FIXTURE_ID, '--copy-response', 'Copy response',
                    '--copy-button', 'Copy', '--copy-completed', 'Copied', '--assistant-heading', 'ChatGPT said:',
                    '--user-heading', 'You said:', '--response-action', 'More actions',
                    '--response-action', 'Branch in new chat', '--stop', 'Stop',
                    '--test-pasteboard', 'com.vizhi.fixture.web-copy']
            result = subprocess.run(list(map(str, args)), capture_output=True, text=True, timeout=10)
            value = json.loads(result.stdout)
            log.write(json.dumps(dict(verb=verb, result=value)) + '\n'); log.flush()
            return value

        try:
            command(['bash', 'tools/desktop/build.sh', '--no-install', '--output', helper])
            command(['swiftc', '-O', 'tests/desktop/web-reply-fixture/main.swift', '-o', executable])
            command(['codesign', '--force', '--sign', '-', bundle])
            preflight = ax('status')
            if preflight.get('error') != 'app-not-running':
                return dict(status='BLOCKED', reason='Fixture already running or runner lacks AX access', preflight=preflight, steps=steps)
            for variant, error in [('extra-editors', None), ('extra-editors-code-only', 'reply-action-row-unrecognized'),
                                   ('extra-editors-duplicate', 'reply-copy-multiple'), ('no-composer', None),
                                   ('normal', None), ('explicit', None), ('code-only', 'reply-action-row-unrecognized'),
                                   ('user-last', 'no-answer'), ('running', 'answer-not-ready'), ('no-ack', 'copy-unconfirmed')]:
                work = directory / variant
                work.mkdir(exist_ok=True)
                for name in ['ready', 'copied']:
                    (work / name).unlink(missing_ok=True)
                process = subprocess.Popen(['open', '-n', '-W', str(bundle), '--args', str(work), variant], stdout=log, stderr=subprocess.STDOUT)
                try:
                    for _ in range(40):
                        if (work / 'ready').exists(): break
                        time.sleep(.2)
                    assert (work / 'ready').exists(), 'Fixture page did not load'
                    assert ax('focus').get('ok')
                    for _ in range(20):
                        snapshot = ax('status')
                        if snapshot.get('surface'): break
                        time.sleep(.15)
                    assert snapshot.get('canCopyAnswer') == (error in [None, 'copy-unconfirmed']), (variant, snapshot)
                    # NSRunningApplication.activate is asynchronous. Reassert only our
                    # fixture after readiness polling and let activation settle before
                    # testing the production helper's frontmost-window requirement.
                    assert ax('focus').get('ok')
                    time.sleep(.25)
                    result = ax('copy-reply')
                    if error:
                        assert result.get('error') == error, (variant, result)
                        if error != 'copy-unconfirmed': assert not (work / 'copied').exists(), variant
                        assert ax('context-clipboard').get('text') == 'Unchanged fixture clipboard', variant
                    else:
                        assert result.get('ok') and result.get('text') == 'Full fixture response', (variant, result)
                        assert (work / 'copied').read_text() == 'reply', variant
                    steps.append(dict(variant=variant, status='PASS'))
                finally:
                    # Stop only our exact fixture executable, never the user's app.
                    for line in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
                        pair = line.strip().split(None, 1)
                        if len(pair) == 2 and pair[1].startswith(str(executable) + ' '):
                            os.kill(int(pair[0]), signal.SIGTERM)
                    process.wait(timeout=5)
            return dict(status='PASS', steps=steps, limitation='Controlled WKWebView only; real ChatGPT/keypad acceptance is separate.')
        except (AssertionError, ValueError, OSError, subprocess.SubprocessError) as error:
            return dict(status='FAIL', reason=str(error), steps=steps)


if __name__ == '__main__':
    destination = Path(sys.argv[1]) if len(sys.argv) == 2 else ROOT / 'artifacts/desktop-web-reply-fixture'
    result = run(destination)
    (destination / 'fixture-result.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    sys.exit(0 if result['status'] == 'PASS' else 1)
