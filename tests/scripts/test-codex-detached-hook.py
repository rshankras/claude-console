#!/usr/bin/env python3
"""Real detached hook processes with PTY-owning ancestors; no live sessions are touched."""
import json
import os
from pathlib import Path
import pty
import subprocess
import sys
import tempfile
import time
import unittest

HOOK = Path(__file__).resolve().parents[2] / 'scripts/codex-hook.sh'

# A second detached wrapper models hook runners that insert a shell between Codex and its hook.
if len(sys.argv) > 1 and sys.argv[1] == '--wrapper':
    result = subprocess.run(['bash', str(HOOK), sys.argv[2]], input=sys.argv[3], text=True,
                            capture_output=True, start_new_session=True)
    sys.stdout.write(result.stdout)
    raise SystemExit(result.returncode)


def launch(root, identity, detached=True, wrapper=False):
    pid, fd = pty.fork()
    if pid == 0:
        try:
            tty = os.ttyname(0).rsplit('/', 1)[-1]
            payload = json.dumps({'session_id': identity, 'cwd': '/demo/' + identity,
                                 'hook_event_name': 'PermissionRequest', 'tool_name': 'Bash',
                                 'tool_input': {'command': 'echo ' + identity}})
            env = dict(os.environ, CODEX_CONSOLE_IPC_ROOT=str(root))
            args = ([sys.executable, __file__, '--wrapper', 'PermissionRequest', payload] if wrapper else
                    ['bash', str(HOOK), 'PermissionRequest'])
            result = subprocess.run(args, input=payload, text=True, capture_output=True,
                                    start_new_session=detached, env=env, timeout=8)
            Path(root, identity + '.result').write_text(json.dumps({'tty': tty, 'code': result.returncode,
                                                                    'stdout': result.stdout, 'stderr': result.stderr}))
            # Keep the PTY allocated until the parent has launched and observed both sessions.
            os.read(0, 1)
            os._exit(0)
        except BaseException:
            os._exit(1)
    return pid, fd


class DetachedHookTests(unittest.TestCase):
    def test_unavailable_invalid_or_cyclic_parent_fails_closed_with_bounded_lookup(self):
        for parent in ['', 'invalid', '0', '1', 'self', 'cycle']:
            with self.subTest(parent=parent), tempfile.TemporaryDirectory(prefix='codex-hook-ps-') as tmp:
                root = Path(tmp)
                binary = root / 'bin'
                binary.mkdir()
                fake = binary / 'ps'
                fake.write_text('#!/bin/bash\necho call >> "$PS_CALLS"\n'
                                'if [ "$2" = tty= ]; then echo "??"; '
                                'elif [ "$FAKE_PARENT" = self ]; then echo "$4"; '
                                'elif [ "$FAKE_PARENT" = cycle ]; then '
                                'if [ "$4" = 100 ]; then echo 101; else echo 100; fi; '
                                'else echo "$FAKE_PARENT"; fi\n')
                fake.chmod(0o700)
                calls = root / 'calls'
                env = dict(os.environ, CODEX_CONSOLE_IPC_ROOT=str(root),
                           PATH=str(binary) + os.pathsep + os.environ['PATH'],
                           PS_CALLS=str(calls), FAKE_PARENT=parent)
                result = subprocess.run(['bash', str(HOOK), 'PermissionRequest'], input='{}',
                                        capture_output=True, text=True, env=env, timeout=3)
                self.assertEqual(0, result.returncode)
                self.assertEqual('{}', result.stdout)
                self.assertTrue((root/'sessions/shared.json').exists())
                self.assertLessEqual(len(calls.read_text().splitlines()), 12)
                self.assertEqual(['shared.json'], [p.name for p in (root/'sessions').glob('*.json')])

    def test_attached_detached_and_wrapped_hooks_keep_two_session_identities(self):
        for detached, wrapper in [(False, False), (True, False), (True, True)]:
            with self.subTest(detached=detached, wrapper=wrapper), tempfile.TemporaryDirectory(prefix='codex-detached-hook-') as tmp:
                root = Path(tmp)
                children = [launch(root, name, detached, wrapper) for name in ['alpha', 'beta']]
                try:
                    deadline = time.monotonic() + 10
                    while not all((root / (name + '.result')).exists() for name in ['alpha', 'beta']):
                        self.assertLess(time.monotonic(), deadline, 'hook fixture did not finish')
                        time.sleep(.02)
                    results = [json.loads((root / (name + '.result')).read_text()) for name in ['alpha', 'beta']]
                    self.assertNotEqual(results[0]['tty'], results[1]['tty'])
                    for name, result in zip(['alpha', 'beta'], results):
                        self.assertEqual(0, result['code'], result)
                        self.assertEqual('{}', result['stdout'])
                        path = root/'sessions'/(result['tty'] + '.json')
                        self.assertTrue(path.exists(), f"{name}: expected {path.name}, found {list((root/'sessions').glob('*.json'))}")
                        envelope = json.loads(path.read_text())
                        self.assertEqual(name, envelope['payload']['session_id'])
                        self.assertEqual('PermissionRequest', envelope['event'])
                    self.assertFalse((root/'sessions/shared.json').exists(), 'a detached hook lost its terminal identity')
                finally:
                    for pid, fd in children:
                        try: os.kill(pid, 15)
                        except ProcessLookupError: pass
                        os.close(fd)
                        os.waitpid(pid, 0)


if __name__ == '__main__':
    if sys.platform != 'darwin':
        print('SKIP: macOS controlling-terminal integration fixture')
    else:
        unittest.main()
