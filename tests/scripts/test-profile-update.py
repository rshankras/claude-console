#!/usr/bin/env python3
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('profile_update', root / 'tools/profile-update.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class ProfileUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='vizhi-profile-update-')
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.default = root / 'profiles/VizhiCodex-Keypad.lp5'
        self.custom = self.base / 'custom.lp5'
        files, profile, _ = module.read_profile(self.default)
        profile['macroCommands'] = [{'name': 'my custom macro', 'keys': ['x', 'y']}]
        profile['layout']['layoutModes'][0]['workspaces'][0]['pressPages'][0]['controls'][0]['pressAction'] = '$custom___action'
        files['ProfileInfo.json'] = json.dumps(profile).encode()
        with zipfile.ZipFile(self.custom, 'w') as archive:
            for name, data in files.items(): archive.writestr(name, data)
    def prepare(self, mode='keep-custom', name='result', current=None, updated=None):
        return module.prepare(current or self.custom, updated or self.default, self.base/name, mode, '2026-09-14.1')
    def test_keep_custom_backs_up_exact_bytes_and_reports_custom_macros_and_keys(self):
        report = self.prepare()
        self.assertEqual(self.custom.read_bytes(), (self.base/'result/backup.lp5').read_bytes())
        self.assertFalse((self.base/'result/candidate.lp5').exists())
        self.assertTrue(any('/macroCommands/' in d['path'] for d in report['differences']))
        self.assertTrue(any(d['current'] == '$custom___action' for d in report['differences']))
    def test_adopt_has_consistent_fresh_identity_and_preserves_host_and_original(self):
        before = self.custom.read_bytes()
        report = self.prepare('adopt-defaults')
        files, profile, app = module.read_profile(self.base/'result/candidate.lp5')
        _, default, default_app = module.read_profile(self.default)
        self.assertNotEqual(default['name'], profile['name'])
        self.assertEqual(profile['name'], app['defaultProfileName'])
        self.assertEqual(profile['name'], profile['packageName'])
        self.assertIn(('name: ' + profile['name']).encode(), files['metadata/LoupedeckPackage.yaml'])
        self.assertEqual(default['layout'], profile['layout'])
        self.assertEqual(default_app['processOrBundleName'], app['processOrBundleName'])
        self.assertEqual(before, self.custom.read_bytes())
        second = self.prepare('adopt-defaults', 'second')
        self.assertEqual(report['candidate_identity'], second['candidate_identity'])
    def test_existing_output_never_overwritten(self):
        self.prepare()
        with self.assertRaises(FileExistsError): self.prepare('adopt-defaults')
        self.assertEqual(self.custom.read_bytes(), (self.base/'result/backup.lp5').read_bytes())
    def test_platform_and_product_mismatch_rejected_before_writes(self):
        for updated in ['VizhiCodex-Windows.lp5', 'ClaudeConsole-Keypad.lp5']:
            with self.assertRaises(ValueError): self.prepare(updated=root/'profiles'/updated)
            self.assertFalse((self.base/'result').exists())
    def test_unchanged_defaults_have_no_differences(self):
        self.assertEqual([], self.prepare(current=self.default)['differences'])
    def test_revision_manifest_matches_archives(self):
        import hashlib
        manifest = json.loads((root/'profiles/profile-revisions.json').read_text())
        for entry in manifest['profiles']:
            path = root/'profiles'/entry['file']
            _, layout_profile, _ = module.read_profile(path)
            self.assertEqual(entry['layout_sha256'], hashlib.sha256(json.dumps(layout_profile['layout'], sort_keys=True, separators=(',', ':')).encode()).hexdigest())
            files, profile, app = module.read_profile(path)
            self.assertIn(('name: ' + profile['name']).encode(), files['metadata/LoupedeckPackage.yaml'])
            self.assertEqual(['VizhiCodex'], json.loads(files['metadata/AdvancedInfo.json'])['additionalPluginNames'])

if __name__ == '__main__': unittest.main()
