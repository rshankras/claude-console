#!/usr/bin/env python3
"""Tests the test harness: coverage drift, persistence, stale evidence, HTTP and exports."""
import copy
import http.client
import json
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/desktop-test'))
import catalog
import run as runner
from inference import word_error_rate


class HarnessTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='vizhi-harness-self-test-')
        self.directory = Path(self.temp.name)
        self.catalog = catalog.read_catalog()
        self.profiles = catalog.validate(self.catalog)
        self.cases = catalog.owner_cases(self.catalog, self.profiles)
        self.data = dict(schema=1, id='self-test-only', cases=self.cases, profiles=self.profiles, revision=0,
                         results={}, history=[], checks={}, automatedCases={}, operator='', appVersion='', device='',
                         notice='Synthetic run; not owner acceptance.', fingerprint=dict(id='synthetic', package=None, branch='test', commit='abc'))

    def tearDown(self):
        self.temp.cleanup()

    def test_every_binding_and_mode_has_a_unique_owner_case(self):
        ids = [c['id'] for c in self.cases]
        self.assertEqual(len(ids), len(set(ids)))
        for p in self.profiles:
            matches = [c for c in self.cases if c['profile'] == p['name']]
            self.assertEqual(sum(len(page['keys']) for page in p['pages']) * 2, len(matches))
            self.assertEqual({'ChatGPT', 'Codex'}, {c['mode'] for c in matches})
        self.assertTrue(any(c['capability'] == 'unsupported' for c in self.cases))
        self.assertTrue(all(c['automatedCase'] is None for c in self.cases if c['profile'] == 'Regression checks'))

    def test_missing_and_obsolete_command_entries_fail_coverage(self):
        bad = copy.deepcopy(self.catalog); bad['commands'].pop()
        with self.assertRaisesRegex(ValueError, 'Catalog drift'): catalog.validate(bad)
        bad = copy.deepcopy(self.catalog); bad['commands'][0]['parameter'] = '99'
        with self.assertRaisesRegex(ValueError, 'Catalog drift'): catalog.validate(bad)

    def test_new_registration_expression_fails_closed(self):
        with self.assertRaisesRegex(ValueError, 'Uncovered registration'):
            catalog.registered_parameters('this.AddParameter(NewExpression(), "label", "group");')
        self.assertEqual({'new_parameter'}, catalog.registered_parameters('this.AddParameter("new_parameter", "label", "group");'))

    def test_duplicate_case_or_missing_mode_fails(self):
        bad = copy.deepcopy(self.catalog); bad['commands'][1]['id'] = bad['commands'][0]['id']
        with self.assertRaisesRegex(ValueError, 'Duplicate'): catalog.validate(bad)
        bad = copy.deepcopy(self.catalog); bad['commands'][0]['modes'] = ['Codex']
        with self.assertRaisesRegex(ValueError, 'Both adaptive modes'): catalog.validate(bad)

    def result(self, status='PASS', notes=''):
        return dict(caseId=self.cases[0]['id'], revision=self.data['revision'], status=status, notes=notes, evidence='owner screenshot.png')

    def test_failed_retest_preserves_history_and_resumes_from_disk(self):
        runner.record_result(self.data, self.result('FAIL', 'Key says No Voice'))
        runner.save(self.directory, self.data)
        loaded = runner.load(self.directory)
        self.assertEqual('FAIL', loaded['results'][self.cases[0]['id']]['status'])
        runner.record_result(self.data, self.result('PASS', 'Retested after checking shortcut'))
        self.assertEqual('FAIL', self.data['history'][1]['previous']['status'])
        self.assertEqual('PASS', self.data['results'][self.cases[0]['id']]['status'])
        self.assertEqual({}, self.data['automatedCases'])

    def test_invalid_results_and_stale_browser_revision_cannot_write(self):
        for payload in [self.result('INVALID'), self.result('FAIL'), self.result('BLOCKED'),
                        dict(self.result(), caseId='unknown'), dict(self.result(), notes='x'*10001),
                        dict(self.result(), revision=-1)]:
            with self.assertRaises(ValueError): runner.record_result(self.data, payload)
        self.assertEqual({}, self.data['results'])
        with self.assertRaisesRegex(ValueError, 'stale'): runner.record_result(self.data, self.result(), is_stale=True)

    def test_unsupported_capability_and_unrun_checks_never_become_accepted(self):
        for c in self.cases:
            self.data['results'][c['id']] = {'status':'PASS'}
        self.assertEqual('INCOMPLETE', runner.summary(self.data, False)['state'])
        self.assertEqual('STALE', runner.summary(self.data, True)['state'])
        self.assertGreater(runner.summary(self.data, False)['unsupported'], 0)

    def test_trx_requires_both_executed_paths_for_each_case(self):
        trx = self.directory / 'test.trx'
        import xml.etree.ElementTree as ET
        root = ET.Element('TestRun')
        for success in ['True', 'False']:
            ET.SubElement(root, 'UnitTestResult', testName=f'Loupedeck.Tests.DesktopCommandRig.Catalog_dispatch(caseId: "voice.press.chatgpt", success: {success})', outcome='Passed')
        ET.ElementTree(root).write(trx)
        counts, coverage = runner.trx_results(trx, {'voice.press.chatgpt','missing'})
        self.assertEqual(2, counts['Passed']); self.assertEqual('PASS', coverage['voice.press.chatgpt']['status'])
        self.assertEqual('FAIL', coverage['missing']['status'])
        root[1].set('outcome','NotExecuted'); ET.ElementTree(root).write(trx)
        self.assertEqual('FAIL', runner.trx_results(trx, {'voice.press.chatgpt'})[1]['voice.press.chatgpt']['status'])

    def test_exports_escape_owner_text_and_retain_stale_results(self):
        runner.record_result(self.data, self.result('FAIL', '<script>alert(1)</script> | newline\nnext'))
        with patch.object(runner, 'stale', return_value=True): runner.export(self.directory, self.data)
        report = (self.directory / 'report.html').read_text()
        self.assertNotIn('<script>', report); self.assertIn('STALE', report); self.assertIn('<table>', report)
        self.assertIn('unsupported', (self.directory / 'report.md').read_text())
        exported = json.loads((self.directory / 'report.json').read_text())
        self.assertTrue(exported['stale']); self.assertEqual('STALE', exported['summary']['state'])

    def test_configured_slots_and_custom_ids_are_explicit(self):
        root = self.directory / '.claude/claude-console'; root.mkdir(parents=True)
        (root / 'desktop-workflows.json').write_text(json.dumps([dict(Id='personal',Label='My draft',Prompt='fixed custom brief',Submit=False)]))
        configuration = catalog.add_workflow_configuration(self.cases, self.directory)
        self.assertEqual('custom', configuration['Codex']['status'])
        first = next(c for c in self.cases if c['commandId'] == 'workflow.slot_1' and c['mode'] == 'Codex')
        self.assertEqual('My draft / DRAFT', first['expectedKey'])
        custom = [c for c in self.cases if c['profile'] == 'Custom actions']
        self.assertEqual(2, len(custom)); self.assertTrue(all(c['automatedCase'] is None for c in custom))

    def test_malformed_workflow_types_match_production_fallback(self):
        root = self.directory / '.claude/claude-console'; root.mkdir(parents=True)
        path = root / 'desktop-workflows.json'
        for items in [[{'Id':'x','Prompt':'custom', 'Submit':'false'}], [42], [{'Id':42,'Prompt':'custom'}]]:
            with self.subTest(items=items):
                path.write_text(json.dumps(items))
                cases = copy.deepcopy(self.cases)
                configuration = catalog.add_workflow_configuration(cases, self.directory)
                self.assertIn('defaults', configuration['Codex']['status'])
                self.assertTrue(all('configuredWorkflow' not in c for c in cases))

    def test_fingerprint_changes_for_source_and_local_configuration(self):
        source = self.directory / 'src/test.cs'; source.parent.mkdir(); source.write_text('one')
        config = self.directory / '.claude/claude-console/desktop-voice-shortcut.json'; config.parent.mkdir(parents=True)
        def fake_git(*args):
            return 'src/test.cs' if args[0] == 'ls-files' else 'fixed'
        with patch.object(runner, 'ROOT', self.directory), patch.object(runner, 'git', fake_git), patch.object(Path, 'home', return_value=self.directory):
            first = runner.fingerprint()['id']
            source.write_text('two'); second = runner.fingerprint()['id']
            config.write_text('{"toggleVoiceChat":"Control+Shift+V"}'); third = runner.fingerprint()['id']
            config.with_name('desktop-conversation-labels.json').write_text('{"ChatGPT":{"Full title":"Short label"}}')
            fourth = runner.fingerprint()['id']
            model = config.parent / 'whisper/ggml-large-v3-turbo-q5_0.bin'; model.parent.mkdir()
            model.write_bytes(b'first-model'); fifth = runner.fingerprint()['id']
            model.write_bytes(b'new-model'); sixth = runner.fingerprint()['id']
        self.assertNotEqual(first, second); self.assertNotEqual(second, third)
        self.assertNotEqual(third, fourth)
        self.assertNotEqual(fourth, fifth); self.assertNotEqual(fifth, sixth)

    def test_profile_selection_does_not_invalidate_but_binding_edits_do(self):
        path = self.directory / 'ApplicationInfo.json'
        path.write_text(json.dumps(dict(defaultProfileName='A', lastModifiedTimeUtc='first', binding='unchanged')))
        first = runner.installed_digest(path)
        path.write_text(json.dumps(dict(defaultProfileName='B', lastModifiedTimeUtc='later', binding='unchanged')))
        self.assertEqual(first, runner.installed_digest(path))
        path.write_text(json.dumps(dict(defaultProfileName='B', binding='new action')))
        self.assertNotEqual(first, runner.installed_digest(path))

    def test_http_requires_token_origin_and_known_case_and_persists_success(self):
        with patch.object(runner, 'stale', return_value=False):
            server = runner.build_server(self.directory, self.data, 0, 'test-token')
            thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
            def request(method, path, payload=None, headers=None):
                connection = http.client.HTTPConnection('127.0.0.1', server.server_port)
                connection.request(method, path, body=json.dumps(payload) if payload else None, headers=headers or {})
                response = connection.getresponse(); status, body = response.status, response.read(); connection.close()
                return status, body
            try:
                self.assertEqual(200, request('GET','/')[0])
                self.assertEqual(403, request('GET','/api/run')[0])
                auth = {'X-Vizhi-Token':'test-token'}
                self.assertEqual(200, request('GET','/api/run', headers=auth)[0])
                self.assertEqual(403, request('POST','/api/result', self.result(), dict(auth, Origin='https://untrusted.example'))[0])
                self.assertEqual(403, request('POST','/api/result', self.result(), dict(auth, Host='untrusted.example'))[0])
                self.assertEqual(404, request('GET','/../../etc/passwd', headers=auth)[0])
                self.assertEqual(200, request('POST','/api/result', self.result(), auth)[0])
                self.assertEqual('PASS', runner.load(self.directory)['results'][self.cases[0]['id']]['status'])
                with patch.object(runner, 'stale', return_value=True):
                    self.assertEqual(409, request('POST','/api/result', self.result(), auth)[0])
                self.assertEqual(200, request('GET','/export/report.html', headers=auth)[0])
            finally:
                server.shutdown(); server.server_close(); thread.join(timeout=3)

    def test_system_context_page_is_additive_idempotent_and_ownership_checked(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location('context_installer', catalog.ROOT / 'tools/install-desktop-context-page.py')
        installer = importlib.util.module_from_spec(spec); spec.loader.exec_module(installer)
        existing = dict(name='my-page', displayName='Personal', controls=[dict(pressAction='my-action')])
        current = dict(applicationName='@_defaultmac', nativePluginName='DefaultMac',
                       additionalNativePluginNames=['ClaudeConsole'],
                       layout=dict(layoutModes=[dict(workspaces=[dict(pressPages=[existing])])]))
        original = copy.deepcopy(current)
        updated = installer.updated_profile(current, dict(name='context', controls=[dict(pressAction='$VizhiDesktop___test')]))
        self.assertEqual(original, current)
        pages = updated['layout']['layoutModes'][0]['workspaces'][0]['pressPages']
        self.assertEqual(existing, pages[0]); self.assertEqual(2, len(pages))
        self.assertEqual(['ClaudeConsole','VizhiDesktop'], updated['additionalNativePluginNames'])
        pages[1]['controls'].append(dict(pressAction='my-customization'))
        self.assertEqual(updated, installer.updated_profile(updated, {}))
        current['nativePluginName'] = 'SomeoneElse'
        with self.assertRaises(ValueError): installer.updated_profile(current, {})

    def test_context_reply_uses_chatgpt_draft_configuration_only(self):
        root = self.directory / '.claude/claude-console'; root.mkdir(parents=True)
        (root / 'desktop-chatgpt-workflows.json').write_text(json.dumps([dict(Id='draft',Label='My reply',Prompt='Reply: {brief}',Input='voice')]))
        (root / 'desktop-workflows.json').write_text(json.dumps([dict(Id='draft_reply',Label='Wrong mode',Prompt='wrong')]))
        catalog.add_workflow_configuration(self.cases, self.directory)
        selected = [c for c in self.cases if c.get('parameter') == 'draft_reply' and c['command'] == 'DesktopWorkflowCommand']
        self.assertTrue(selected)
        for case in selected:
            if case['mode'] == 'ChatGPT': self.assertEqual('My reply / SPEAK', case['expectedKey'])
            else: self.assertNotIn('configuredWorkflow', case)

    def test_fixed_audio_metric_reports_bad_or_empty_transcripts(self):
        self.assertEqual(0, word_error_rate('Hello, test.', 'hello test'))
        self.assertEqual(1, word_error_rate('hello test', ''))
        self.assertGreater(word_error_rate('correct draft text', 'unrelated words'), .15)
        with self.assertRaises(ValueError): word_error_rate('', 'words')

    def test_software_coverage_reuses_dispatch_without_creating_live_results(self):
        ids = {c['automatedCase'] for c in self.cases if c.get('automatedCase')}
        self.data['automatedCases'] = {i: dict(status='PASS') for i in ids}
        self.data['checks']['catalog'] = dict(status='PASS')
        summary = runner.summary(self.data, False)
        self.assertEqual(sum(len(c['modes']) for c in self.catalog['commands']), summary['software']['variantTotal'])
        self.assertEqual(sum(bool(c.get('automatedCase')) for c in self.cases), summary['software']['coveredCases'])
        self.assertEqual(sum(not c.get('automatedCase') for c in self.cases), summary['software']['unmappedCases'])
        self.assertEqual(len(self.cases), summary['hardware']['NOT_TESTED'])
        for group in summary['software']['groups']:
            if group['profile'] in {p['name'] for p in self.profiles}:
                expected = sum(len(page['keys']) for p in self.profiles if p['name'] == group['profile'] for page in p['pages'])
                self.assertEqual(expected, group['modes']['ChatGPT']['covered'])
                self.assertEqual(expected, group['modes']['Codex']['covered'])
                self.assertEqual(0, group['liveRecorded'])
        missing = self.cases[0]['automatedCase']
        self.data['automatedCases'].pop(missing)
        affected = sum(c.get('automatedCase') == missing for c in self.cases)
        self.assertEqual(summary['software']['coveredCases'] - affected, runner.software_summary(self.data)['coveredCases'])
        self.data['checks']['catalog']['status'] = 'FAIL'
        self.assertEqual(0, runner.software_summary(self.data)['coveredCases'])

    def seed_software_checks(self, directory, data, suite):
        self.assertEqual('full', suite)
        data['checks'].update({name: dict(status='PASS') for name in runner.SOFTWARE_CHECKS})
        data['checks']['repository-suite'] = dict(status='PASS')
        data['checks']['package-links'] = dict(status='NOT_TESTED')  # Deliberately offline.
        return True

    def test_unattended_pipeline_exit_codes_and_preserved_owner_results(self):
        runner.record_result(self.data, self.result('FAIL', 'Existing real observation'))
        owner_results = copy.deepcopy(self.data['results'])
        for fixture_status, package_status, expected_code in [('PASS','PASS',0), ('BLOCKED','PASS',2), ('FAIL','PASS',1), ('PASS','NOT_TESTED',2)]:
            with self.subTest(fixture=fixture_status, package=package_status):
                def checks(directory, data, suite):
                    self.seed_software_checks(directory, data, suite)
                    data['checks']['package']['status'] = package_status
                with patch.object(runner, 'stale', return_value=False), patch.object(runner, 'check', side_effect=checks), \
                     patch('fixture.run_fixture', return_value=dict(status=fixture_status)) as fixture, \
                     patch('inference.run_inference', return_value=dict(status='PASS')) as audio:
                    code = runner.automate(self.directory, self.data)
                self.assertEqual(expected_code, code)
                fixture.assert_called_once(); audio.assert_called_once()
                self.assertEqual(owner_results, self.data['results'])
                self.assertEqual(owner_results, runner.load(self.directory)['results'])
                self.assertTrue((self.directory / 'report.html').exists())
                self.assertEqual(len(self.cases) - 1, runner.summary(self.data, False)['hardware']['NOT_TESTED'])
                self.assertNotEqual('ACCEPTANCE RECORDED', runner.summary(self.data, False)['state'])

    def test_unattended_stage_error_is_saved_and_later_stage_runs(self):
        with patch.object(runner, 'stale', return_value=False), patch.object(runner, 'check', side_effect=self.seed_software_checks), \
             patch('fixture.run_fixture', side_effect=OSError('fixture unavailable')), \
             patch('inference.run_inference', return_value=dict(status='PASS')) as audio:
            self.assertEqual(1, runner.automate(self.directory, self.data))
        audio.assert_called_once()
        saved = runner.load(self.directory)
        self.assertEqual('FAIL', saved['checks']['fixture-app']['status'])
        self.assertEqual('PASS', saved['checks']['audio-inference']['status'])
        self.assertEqual({}, saved['results'])

    def test_unattended_stale_build_stops_remaining_stages(self):
        changed = False
        def checks(directory, data, suite):
            nonlocal changed
            self.seed_software_checks(directory, data, suite)
            changed = True
        with patch.object(runner, 'stale', side_effect=lambda data: changed), patch.object(runner, 'check', side_effect=checks), \
             patch('fixture.run_fixture') as fixture, patch('inference.run_inference') as audio:
            self.assertEqual(1, runner.automate(self.directory, self.data))
        fixture.assert_not_called(); audio.assert_not_called()
        self.assertEqual('FAIL', self.data['checks']['stable-build']['status'])

    def test_interrupted_rerun_does_not_reuse_older_passes(self):
        self.data['checks']['fixture-app'] = dict(status='PASS')
        self.data['automatedCases']['old'] = dict(status='PASS')
        with patch.object(runner, 'stale', return_value=False), patch.object(runner, 'check', side_effect=KeyboardInterrupt):
            self.assertEqual(130, runner.automate(self.directory, self.data))
        saved = runner.load(self.directory)
        self.assertEqual('INTERRUPTED', saved['automation']['status'])
        self.assertEqual({}, saved['automatedCases'])
        self.assertNotIn('fixture-app', saved['checks'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
