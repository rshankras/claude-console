#!/usr/bin/env python3
"""Exercise the shipped cleanup and hooks against disposable homes, with real processes."""
import concurrent.futures
import fcntl
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest

REPO = Path(__file__).resolve().parents[2]


class UnwireConcurrencyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="cc-unwire-test-")
        self.home = Path(self.temp.name)
        self.runtime = self.home / ".claude/claude-console"
        (self.runtime / "scripts").mkdir(parents=True)
        shutil.copy2(REPO / "scripts/uninstall.sh", self.runtime / "scripts/uninstall.sh")
        self.settings = self.home / ".claude/settings.json"
        self.chain = self.runtime / "statusline-chain"
        self.marker = self.runtime / "no-autowire"
        self.missing = self.runtime / "plugin-missing-since"
        self.breadcrumb = self.runtime / "unwired-after-uninstall"
        self.original = {
            "model": "user-model",
            "statusLine": {"type": "command", "command": "bash /x/statusline-handler.sh"},
            "hooks": {"Stop": [{"hooks": [
                {"type": "command", "command": "bash /x/activity-hook.sh done"},
                {"type": "command", "command": "echo user's hook — keep me"},
            ]}]},
        }
        self.settings.write_text(json.dumps(self.original), encoding="utf-8")
        self.chain.write_text("echo original-status", encoding="utf-8")
        (self.runtime / "plugin-home").write_text(str(self.home / "removed-plugin"))
        self.missing.write_text(str(int(time.time()) - 120))
        self.env = dict(os.environ, HOME=str(self.home),
                        CLAUDE_CONSOLE_IPC_ROOT=str(self.home / "ipc"))

    def tearDown(self):
        self.temp.cleanup()

    def run_script(self, name, *args):
        return subprocess.run(["bash", str(REPO / "scripts" / name), *args],
                              input="{}", text=True, capture_output=True, env=self.env, timeout=15)

    def assert_restored(self):
        result = json.loads(self.settings.read_text(encoding="utf-8"))
        self.assertEqual("echo original-status", result["statusLine"]["command"])
        self.assertEqual("user-model", result["model"])
        self.assertEqual([self.original["hooks"]["Stop"][0]["hooks"][1]],
                         result["hooks"]["Stop"][0]["hooks"])
        backup = self.home / ".claude/settings.json.claude-console.bak"
        self.assertEqual(self.original, json.loads(backup.read_text(encoding="utf-8")))
        self.assertTrue(self.marker.exists())
        self.assertFalse(self.chain.exists())
        self.assertEqual([], list(self.settings.parent.glob("settings.json.cc.*.tmp")))

    def test_simultaneous_cleanups_keep_original_status_line_and_backup(self):
        with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
            results = list(pool.map(lambda _: self.run_script("uninstall.sh", "--unwire"), range(24)))
        self.assertTrue(any(r.returncode == 0 for r in results))
        self.assertTrue(all(r.returncode in (0, 75) for r in results),
                        [(r.returncode, r.stderr) for r in results])
        self.assert_restored()

    def test_busy_lock_preserves_recovery_data_and_hooks_retry(self):
        lock_path = self.settings.parent / ".claude-console-unwire.lock"
        with lock_path.open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            self.assertEqual(75, self.run_script("uninstall.sh", "--unwire").returncode)
            for script, args in [("activity-hook.sh", ("busy",)), ("statusline-handler.sh", ())]:
                self.assertEqual(0, self.run_script(script, *args).returncode)
            self.assertEqual(self.original, json.loads(self.settings.read_text()))
            self.assertTrue(self.chain.exists())
            self.assertTrue(self.missing.exists())
            self.assertFalse(self.marker.exists())
            self.assertFalse(self.breadcrumb.exists())
        self.assertEqual(0, self.run_script("activity-hook.sh", "busy").returncode)
        self.assert_restored()
        self.assertTrue(self.breadcrumb.exists())
        self.assertFalse(self.missing.exists())

    def test_invalid_settings_keep_chain_and_do_not_claim_success(self):
        self.settings.write_text("{not valid json")
        self.assertNotEqual(0, self.run_script("uninstall.sh", "--unwire").returncode)
        self.assertEqual(0, self.run_script("statusline-handler.sh").returncode)
        self.assertEqual("{not valid json", self.settings.read_text())
        self.assertTrue(self.chain.exists())
        self.assertTrue(self.missing.exists())
        self.assertFalse(self.marker.exists())
        self.assertFalse(self.breadcrumb.exists())

    def test_backup_failure_preserves_settings_and_removes_temporary_output(self):
        # Force copy2 to fail by occupying its destination with a directory. This works even
        # when tests run with privileges that bypass ordinary file permission restrictions.
        backup = self.settings.parent / "settings.json.claude-console.bak"
        (backup / self.settings.name).mkdir(parents=True)
        self.assertNotEqual(0, self.run_script("uninstall.sh", "--unwire").returncode)
        self.assertEqual(self.original, json.loads(self.settings.read_text()))
        self.assertTrue(self.chain.exists())
        self.assertFalse(self.marker.exists())
        self.assertEqual([], list(self.settings.parent.glob("settings.json.cc.*.tmp")))


if __name__ == "__main__":
    unittest.main()
