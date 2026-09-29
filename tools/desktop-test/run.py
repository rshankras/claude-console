#!/usr/bin/env python3
"""Vizhi Desktop: automated evidence + resumable, owner-driven hardware acceptance.

No live ChatGPT control, recording, clipboard reads, plugin installation, or profile edits.
The optional fixture process is restricted to our own test application.
"""
import argparse
import contextlib
import datetime as dt
import hashlib
import html
import http.server
import json
import os
import platform
import re
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.parse
import xml.etree.ElementTree as ET
from pathlib import Path

from catalog import ROOT, read_catalog, validate, owner_cases, add_workflow_configuration

ARTIFACTS = ROOT / 'artifacts/desktop-tests'
STATUSES = {'PASS', 'FAIL', 'BLOCKED', 'NOT_TESTED'}
HERE = Path(__file__).resolve().parent
SOFTWARE_CHECKS = ['catalog', 'command-coverage', 'harness-self-test', 'stable-build',
                   'fixture-app', 'audio-inference', 'package', 'source-build']


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')


def digest(path):
    if not path.is_file():
        return None
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def installed_digest(path):
    # Profile selection and bookkeeping timestamps change during a legitimate hardware run.
    # Bindings, labels, layout and configuration still invalidate acceptance when edited.
    if path.suffix.lower() != '.json':
        return digest(path)
    def stable(value):
        if isinstance(value, dict):
            return {k: stable(v) for k, v in value.items()
                    if k not in {'lastModifiedTimeUtc', 'lastUsedTimeUtc', 'defaultProfileName'}}
        if isinstance(value, list): return [stable(v) for v in value]
        return value
    try:
        return hashlib.sha256(json.dumps(stable(json.loads(path.read_text())), sort_keys=True).encode()).hexdigest()
    except (OSError, ValueError):
        return digest(path)


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def fingerprint(package=None):
    # Hash source contents, including untracked harness code; commit alone cannot identify a dirty build.
    paths = git('ls-files', '-z', '--cached', '--others', '--exclude-standard').split('\0')
    source = {p: digest(ROOT / p) for p in sorted(set(paths)) if p and p.startswith(('src/', 'tests/', 'tools/'))}
    config_root = Path.home() / '.claude/claude-console'
    service = Path.home() / 'Library/Application Support/Logi/LogiPluginService'
    installed = {}
    for directory in [service / 'Plugins/VizhiDesktop', service / 'Applications/Loupedeck70/@_vizhidesktop']:
        if directory.exists():
            for path in sorted(directory.rglob('*')):
                if path.is_file() and (path.suffix.lower() in {'.dll', '.json', '.lp5', '.link'} or path.name == 'VizhiAxBridge'):
                    installed[str(path)] = installed_digest(path)
    for name in ['VizhiAxBridge', 'ClaudeVoiceHelper.app/Contents/MacOS/ClaudeVoiceHelper',
                 'whisper-bin/whisper-cli', 'whisper/ggml-base.en.bin',
                 'whisper/ggml-large-v3-turbo-q5_0.bin',
                 'desktop-voice-shortcut.json', 'desktop-workflows.json', 'desktop-chatgpt-workflows.json',
                 'desktop-conversation-labels.json']:
        path = config_root / name
        installed[str(path)] = digest(path)
    for directory in [Path('/Applications'), Path.home() / 'Applications']:
        for name in ['ChatGPT.app', 'Codex.app']:
            info = directory / name / 'Contents/Info.plist'
            if info.is_file(): installed[str(info)] = digest(info)
    data = dict(commit=git('rev-parse', 'HEAD'), branch=git('branch', '--show-current'),
                dirty=git('status', '--porcelain'), source=source, installed=installed,
                package=str(package.resolve()) if package else None,
                packageHash=digest(package) if package else None, os=platform.platform())
    data['id'] = hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()
    return data


def new_run(package=None):
    catalog = read_catalog()
    layouts = validate(catalog)
    cases = owner_cases(catalog, layouts)
    configuration = add_workflow_configuration(cases)
    run_id = dt.datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + secrets.token_hex(3)
    directory = ARTIFACTS / run_id
    directory.mkdir(parents=True)
    os.chmod(directory, 0o700)
    data = dict(schema=1, id=run_id, created=now(), updated=now(), revision=0,
                fingerprint=fingerprint(package), profiles=layouts, cases=cases, configuration=configuration,
                checks={}, automatedCases={}, results={}, history=[], operator='', appVersion='', device='',
                notice='Automated checks do not establish live app or keypad compatibility. Installed artifacts and source are identified separately. Package verification does not prove that a package was built from this working tree.')
    save(directory, data)
    return directory, data


def save(directory, data):
    data['updated'] = now()
    with tempfile.NamedTemporaryFile('w', dir=directory, prefix='.result-', delete=False) as out:
        json.dump(data, out, indent=2, ensure_ascii=False)
        out.write('\n'); out.flush(); os.fsync(out.fileno())
        temp = Path(out.name)
    temp.replace(directory / 'run.json')


def resolve_run(value):
    if not value or value == 'latest':
        choices = sorted(p for p in ARTIFACTS.glob('*/run.json'))
        if not choices:
            raise ValueError('No run yet. Use: python3 tools/desktop-test/run.py check')
        return choices[-1].parent
    path = Path(value).expanduser()
    if not path.is_dir():
        path = ARTIFACTS / value
    if not (path / 'run.json').is_file():
        raise ValueError('Run not found: ' + value)
    return path.resolve()


def load(directory):
    return json.loads((directory / 'run.json').read_text())


def stale(data):
    package = data['fingerprint']['package']
    return fingerprint(Path(package) if package else None)['id'] != data['fingerprint']['id']


@contextlib.contextmanager
def locked(directory):
    import fcntl
    with (directory / '.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ValueError('This run is open in another runner/server. Stop it before changing the run.')
        yield


def run_check(directory, data, name, command, timeout=600, env=None):
    log = directory / (name + '.log')
    print(f'▶ {name}', flush=True)
    started = time.monotonic()
    result = dict(status='FAIL', started=now(), command=command, log=log.name)
    try:
        with log.open('w') as output:
            p = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                               timeout=timeout, env=dict(os.environ, **(env or {})))
        result.update(status='PASS' if p.returncode == 0 else 'FAIL', exitCode=p.returncode)
    except (subprocess.TimeoutExpired, OSError) as error:
        result['reason'] = str(error)
    result['seconds'] = round(time.monotonic() - started, 2)
    data['checks'][name] = result
    save(directory, data)
    print(f"  {result['status']} ({result['seconds']}s) — {log}", flush=True)
    return result['status'] == 'PASS'


def trx_results(path, case_ids):
    root = ET.parse(path).getroot()
    rows = [r.attrib for r in root.iter() if r.tag.endswith('UnitTestResult')]
    counts = {outcome: sum(r.get('outcome') == outcome for r in rows) for outcome in ['Passed', 'Failed', 'NotExecuted']}
    coverage = {}
    for case_id in sorted(case_ids):
        matches = [r for r in rows if 'DesktopCommandRig.Catalog_dispatch(' in r.get('testName', '')
                   and f'caseId: "{case_id}"' in r.get('testName', '')]
        # Each catalog case must actually execute both success and refusal paths, not merely be discovered.
        values = {re.search(r'success: (True|False|true|false)', r['testName'])[1].lower()
                  for r in matches if re.search(r'success: (True|False|true|false)', r['testName'])}
        coverage[case_id] = dict(status='PASS' if len(matches) == 2 and values == {'true', 'false'}
                                and all(r['outcome'] == 'Passed' for r in matches) else 'FAIL',
                                tests=[dict(name=r['testName'], outcome=r['outcome']) for r in matches])
    return counts, coverage


def check(directory, data, suite='desktop'):
    if stale(data):
        raise ValueError('Build/configuration changed. Create a new run; old sign-off is retained as stale.')
    validate()
    data['checks']['catalog'] = dict(status='PASS', entries=len(read_catalog()['commands']),
                                   bindings=sum(len(page['keys']) for p in data['profiles'] for page in p['pages']))
    trx = directory / 'automated.trx'
    if trx.exists():
        trx.unlink()  # A crashed test process must not reuse old passing evidence.
    common = ['--logger', 'trx;LogFileName=automated.trx', '--results-directory', str(directory)]
    if suite == 'full':
        run_check(directory, data, 'repository-suite', ['bash', 'tests/run-all.sh', *common], timeout=1200)
    else:
        run_check(directory, data, 'desktop-unit', ['dotnet', 'test', 'tests/ClaudeConsolePlugin.Tests.csproj', '--nologo',
                  '--filter', 'FullyQualifiedName~Desktop|FullyQualifiedName~Voice|FullyQualifiedName~AxBridge', *common])
        for script in ['test-desktop-ax.py', 'test-desktop-shortcut.py', 'test-desktop-package.py']:
            if sys.platform == 'darwin':
                run_check(directory, data, script[:-3], ['python3', 'tests/scripts/' + script])
            else:
                data['checks'][script[:-3]] = dict(status='BLOCKED', reason='Requires macOS / Swift toolchain')
    try:
        ids = {c['id'] + '.' + mode.lower() for c in read_catalog()['commands'] for mode in c['modes']}
        counts, coverage = trx_results(trx, ids)
        data['automatedCases'] = coverage
        data['checks']['command-coverage'] = dict(status='PASS' if all(c['status'] == 'PASS' for c in coverage.values()) else 'FAIL', counts=counts, log='automated.trx')
    except (OSError, ET.ParseError, KeyError) as error:
        data['automatedCases'] = {}
        data['checks']['command-coverage'] = dict(status='FAIL', reason='Missing/invalid TRX: ' + str(error))
    if suite == 'full':
        data['checks']['harness-self-test'] = dict(status=data['checks']['repository-suite']['status'],
            log='repository-suite.log', reason='Included in tests/run-all.sh')
    else:
        run_check(directory, data, 'harness-self-test', ['python3', 'tests/scripts/test-desktop-harness.py'])
    build_output = directory / 'source-build/bin'
    run_check(directory, data, 'source-build', ['dotnet', 'build',
        'src/Products/VizhiDesktop/VizhiDesktopPlugin.csproj', '--nologo', '-c', 'Release',
        '-p:SkipPluginLink=true', '--output', str(build_output)])
    data['checks']['source-build']['dllHash'] = digest(build_output / 'VizhiDesktopPlugin.dll')
    package = data['fingerprint']['package']
    if package:
        run_check(directory, data, 'package', ['bash', 'tools/verify-package.sh', package], env={'CC_VERIFY_OFFLINE': '1'})
        data['checks']['package-links'] = dict(status='NOT_TESTED', reason='Offline package verification; external URLs not checked')
    else:
        data['checks']['package'] = dict(status='NOT_TESTED', reason='Pass --package path/to/VizhiDesktop.lplug4 to verify a specific archive')
    data['checks'].setdefault('fixture-app', dict(status='NOT_TESTED', reason='Run the fixture subcommand in a macOS GUI session'))
    data['checks'].setdefault('audio-inference', dict(status='NOT_TESTED', reason='Optional audio subcommand; no microphone needed'))
    if stale(data):
        data['checks']['stable-build'] = dict(status='FAIL', reason='Files/configuration changed during the run; create a fresh run')
    else:
        data['checks']['stable-build'] = dict(status='PASS')
    save(directory, data)
    export(directory, data)
    return not any(c['status'] == 'FAIL' for c in data['checks'].values())


def summary(data, is_stale):
    counts = {s: 0 for s in STATUSES}
    for case in data['cases']:
        counts[data['results'].get(case['id'], {}).get('status', 'NOT_TESTED')] += 1
    required_checks = ['catalog', 'command-coverage', 'harness-self-test', 'stable-build', 'fixture-app', 'package', 'source-build']
    required_ok = all(data['checks'].get(k, {}).get('status') == 'PASS' for k in required_checks)
    required_ok &= any(data['checks'].get(k, {}).get('status') == 'PASS' for k in ['repository-suite', 'desktop-unit'])
    any_failure = any(c['status'] == 'FAIL' for c in data['checks'].values())
    # A release decision remains a human decision: these are recorded test results only.
    state = 'STALE' if is_stale else 'FAILURES RECORDED' if any_failure or counts['FAIL'] else 'ACCEPTANCE RECORDED' if required_ok and counts['PASS'] == len(data['cases']) else 'INCOMPLETE'
    return dict(state=state, hardware=counts, software=software_summary(data),
                unsupported=sum(c['capability'] == 'unsupported' for c in data['cases']))


def software_summary(data):
    """Link recorded dispatch evidence to bindings without creating hardware results.

    Repeated bindings reuse a command/mode test. They are coverage, not extra test executions.
    Regression scenarios and custom IDs without an exact mapping are left unmapped.
    """
    ids = {c['automatedCase'] for c in data['cases'] if c.get('automatedCase')}
    variants = {s: sum(data['automatedCases'].get(i, {}).get('status', 'NOT_TESTED') == s for i in ids)
                for s in STATUSES}
    groups = []
    for profile in dict.fromkeys(c['profile'] for c in data['cases']):
        cases = [c for c in data['cases'] if c['profile'] == profile]
        modes = {}
        for mode in ['ChatGPT', 'Codex']:
            selected = [c for c in cases if c['mode'] == mode]
            covered = sum(data['checks'].get('catalog', {}).get('status') == 'PASS'
                          and data['automatedCases'].get(c.get('automatedCase'), {}).get('status') == 'PASS'
                          for c in selected)
            modes[mode] = dict(total=len(selected), covered=covered,
                               unmapped=sum(not c.get('automatedCase') for c in selected))
        groups.append(dict(profile=profile, modes=modes, total=len(cases),
                           liveRecorded=sum(data['results'].get(c['id'], {}).get('status', 'NOT_TESTED') != 'NOT_TESTED' for c in cases)))
    return dict(variants=variants, variantTotal=len(ids), groups=groups,
                coveredCases=sum(m['covered'] for g in groups for m in g['modes'].values()),
                unmappedCases=sum(m['unmapped'] for g in groups for m in g['modes'].values()))


def automation_exit_code(data, is_stale=False):
    statuses = [data['checks'].get(k, {}).get('status', 'NOT_TESTED') for k in SOFTWARE_CHECKS]
    suite = data.get('automation', {}).get('suite', 'full')
    statuses.append(data['checks'].get('repository-suite' if suite == 'full' else 'desktop-unit', {}).get('status', 'NOT_TESTED'))
    if is_stale or any(c['status'] == 'FAIL' for c in data['checks'].values()):
        return 1
    return 0 if all(s == 'PASS' for s in statuses) else 2


def automate(directory, data, suite='full', wav=None, expected=None, whisper=None, model=None):
    """One unattended software run. Every stage saves evidence; blocked stages do not stop later ones."""
    if stale(data):
        raise ValueError('Run is stale; create a new run')
    from fixture import run_fixture
    from inference import run_inference
    data['automation'] = dict(started=now(), status='RUNNING', suite=suite, stage='checks')
    # Never leave an earlier stage's success behind after a rerun is interrupted.
    data['checks'] = {}
    data['automatedCases'] = {}
    save(directory, data)
    stages = [('checks', lambda: check(directory, data, suite)),
              ('fixture-app', lambda: run_fixture(directory)),
              ('audio-inference', lambda: run_inference(directory, wav, expected, whisper, model))]
    try:
        for name, operation in stages:
            if stale(data):
                data['checks']['stable-build'] = dict(status='FAIL', reason='Build/configuration changed during automation; remaining stages were not run')
                break
            data['automation']['stage'] = name
            save(directory, data)
            print('▶ unattended stage: ' + name, flush=True)
            try:
                result = operation()
                if name != 'checks':
                    data['checks'][name] = result
                    print(f"  {result['status']}: {result.get('reason', result.get('log', name))}", flush=True)
            except (OSError, ValueError, subprocess.SubprocessError) as error:
                data['checks'][name] = dict(status='FAIL', reason=str(error))
            save(directory, data)
        code = automation_exit_code(data, stale(data))
        data['automation'].update(status={0: 'PASS', 1: 'FAIL', 2: 'INCOMPLETE'}[code], finished=now(), stage=None)
    except KeyboardInterrupt:
        data['automation'].update(status='INTERRUPTED', finished=now())
        code = 130
    save(directory, data)
    export(directory, data)
    return code


def record_result(data, payload, is_stale=False):
    if is_stale:
        raise ValueError('Build/configuration changed; this run is stale. Start a new run to record new acceptance.')
    if payload.get('revision') != data['revision']:
        raise ValueError('This page is out of date. Reload before saving.')
    case_id = payload.get('caseId')
    if case_id not in {c['id'] for c in data['cases']}:
        raise ValueError('Unknown case ID')
    status = payload.get('status')
    if status not in STATUSES:
        raise ValueError('Invalid result status')
    result = dict(status=status, when=now())
    for key in ['notes', 'evidence']:
        value = payload.get(key, '')
        if not isinstance(value, str) or len(value) > 10000:
            raise ValueError('Invalid ' + key)
        result[key] = value
    if status in {'FAIL', 'BLOCKED'} and not result['notes'].strip():
        raise ValueError('Add a brief reason for a failed or blocked case.')
    data['history'].append(dict(caseId=case_id, previous=data['results'].get(case_id), result=result.copy()))
    data['results'][case_id] = result
    data['revision'] += 1


def export(directory, data):
    is_stale = stale(data)
    totals = summary(data, is_stale)
    def md(value):
        return html.escape(str(value)).replace('|', '\\|').replace('\n', '<br>')
    lines = ['# Vizhi Desktop test report', '', f"Run: `{data['id']}` — **{totals['state']}**", '',
             f"Branch: `{data['fingerprint']['branch']}`; commit: `{data['fingerprint']['commit']}`; fingerprint: `{data['fingerprint']['id']}`.", '',
             data['notice'], '', f"Operator: {md(data['operator'] or 'not recorded')}; app version: {md(data['appVersion'] or 'not recorded')}; device: {md(data['device'] or 'not recorded')}.", '',
             '## Automated evidence', '', '| Check | Result | Evidence / limitation |', '|---|---|---|']
    for name, result in data['checks'].items():
        lines.append(f"| {md(name)} | {result['status']} | {md(result.get('reason', result.get('log', result.get('counts', ''))))} |")
    software = totals['software']
    coverage_note = (f"{software['variants']['PASS']}/{software['variantTotal']} command/mode variants have passing dispatch evidence. "
                     'Bindings reuse these tests; the numbers below are coverage, not additional test executions. '
                     'A covered binding still needs live app/keypad observation. Unmapped scenarios have no exact dispatch link.')
    lines += ['', '## Software coverage by profile', '', coverage_note, '',
              '| Profile / set | ChatGPT covered | Codex covered | Unmapped | Live results recorded |', '|---|---|---|---|---|']
    coverage_rows = []
    for group in software['groups']:
        row = [group['profile'], *[f"{group['modes'][m]['covered']}/{group['modes'][m]['total']}" for m in ['ChatGPT', 'Codex']],
               sum(m['unmapped'] for m in group['modes'].values()), f"{group['liveRecorded']}/{group['total']}"]
        coverage_rows.append(row)
        lines.append('| ' + ' | '.join(map(md, row)) + ' |')
    lines += ['', '## Owner hardware acceptance', '',
              'Unsupported Copy Answer cases can pass only the disabled-behavior check; the capability remains unsupported.', '',
              '| Case | Profile / key | Mode | Capability | Simulated dispatch | Owner result | Notes / evidence |', '|---|---|---|---|---|---|---|']
    for c in data['cases']:
        result = data['results'].get(c['id'], {})
        automated = data['automatedCases'].get(c.get('automatedCase'), {}).get('status', 'NOT_TESTED')
        lines.append('| ' + ' | '.join(map(md, [c['title'], c['profile'] + ' / ' + c['page'] + ' / ' + str(c['position'] or 'assigned'), c['mode'], c['capability'], automated, result.get('status', 'NOT_TESTED') + (' (STALE)' if is_stale else ''), result.get('notes', '') + ' ' + result.get('evidence', '')])) + ' |')
    (directory / 'report.json').write_text(json.dumps(dict(data, stale=is_stale, summary=totals, exportedAt=now()), indent=2, ensure_ascii=False) + '\n')
    text = '\n'.join(lines) + '\n'
    (directory / 'report.md').write_text(text)
    # Standalone tables, with all owner/configuration text escaped and no remote resources.
    def table(headers, rows):
        return '<table><thead><tr>' + ''.join('<th>' + html.escape(h) + '</th>' for h in headers) + '</tr></thead><tbody>' + ''.join(
            '<tr>' + ''.join('<td>' + html.escape(str(cell)).replace('\n', '<br>') + '</td>' for cell in row) + '</tr>' for row in rows) + '</tbody></table>'
    checks = [[name, c['status'], c.get('reason', c.get('log', c.get('counts', '')))] for name, c in data['checks'].items()]
    hardware = []
    for c in data['cases']:
        result = data['results'].get(c['id'], {})
        hardware.append([c['title'], c['profile'] + ' / ' + c['page'] + ' / ' + str(c['position'] or 'assigned'),
            c['mode'], c['capability'], data['automatedCases'].get(c.get('automatedCase'), {}).get('status', 'NOT_TESTED'),
            result.get('status', 'NOT_TESTED') + (' (STALE)' if is_stale else ''),
            result.get('notes', '') + '\n' + result.get('evidence', '')])
    body = '<h1>Vizhi Desktop test report</h1><p class="state">' + html.escape(totals['state']) + '</p>'
    body += '<p>Run ' + html.escape(data['id']) + ' · commit ' + html.escape(data['fingerprint']['commit']) + '</p>'
    body += '<p>' + html.escape(data['notice']) + '</p>'
    body += '<p>Tester: ' + html.escape(data['operator'] or 'not recorded') + ' · App version: ' + html.escape(data['appVersion'] or 'not recorded') + ' · Device: ' + html.escape(data['device'] or 'not recorded') + '</p>'
    body += '<h2>Automated evidence</h2>' + table(['Check', 'Result', 'Evidence / limitation'], checks)
    body += '<h2>Software coverage by profile</h2><p>' + html.escape(coverage_note) + '</p>'
    body += table(['Profile / set', 'ChatGPT covered', 'Codex covered', 'Unmapped', 'Live results recorded'], coverage_rows)
    body += '<h2>Owner hardware acceptance</h2><p>Copy Answer remains unsupported; its cases check disabled behavior. Automated dispatch does not establish live app compatibility.</p>'
    body += table(['Case', 'Profile / key', 'Mode', 'Capability', 'Simulated dispatch', 'Owner result', 'Notes / evidence'], hardware)
    (directory / 'report.html').write_text('<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Vizhi Desktop report</title><style>body{font:14px/1.5 system-ui;margin:2rem;color:#173336}h1{font-size:28px}h2{margin-top:2rem}.state{font-weight:bold}table{border-collapse:collapse;width:100%;font-size:12px}th,td{text-align:left;vertical-align:top;padding:10px;border:1px solid #dce5df;overflow-wrap:anywhere}th{background:#e8efe9}tr:nth-child(even){background:#f7f9f5}@media print{thead{display:table-header-group}tr{break-inside:avoid}body{margin:0}}</style>' + body + '</html>')
    return totals


def build_server(directory, data, port, token):
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass  # Never log the authorization token or owner notes.
        def respond(self, status, body, content_type='application/json'):
            if not isinstance(body, bytes):
                body = json.dumps(body).encode()
            self.send_response(status)
            self.send_header('Content-Type', content_type)
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Referrer-Policy', 'no-referrer')
            self.send_header('Content-Security-Policy', "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'")
            self.send_header('Content-Length', str(len(body)))
            self.end_headers(); self.wfile.write(body)
        def authorized(self):
            origin = f'http://127.0.0.1:{self.server.server_port}'
            return self.headers.get('Host') == origin[7:] and self.headers.get('Origin', origin) == origin and secrets.compare_digest(self.headers.get('X-Vizhi-Token', ''), token)
        def do_GET(self):
            route = urllib.parse.urlsplit(self.path).path
            static = {'/': ('index.html', 'text/html; charset=utf-8'), '/app.js': ('app.js', 'text/javascript'), '/style.css': ('style.css', 'text/css')}
            if route in static:
                name, mime = static[route]; self.respond(200, (HERE / name).read_bytes(), mime); return
            if not self.authorized():
                self.respond(403, {'error': 'Open the full local URL printed by the runner.'}); return
            if route == '/api/run':
                is_stale = stale(data)
                self.respond(200, dict(data, stale=is_stale, summary=summary(data, is_stale)))
            elif route in ['/export/report.md', '/export/report.html', '/export/report.json']:
                export(directory, data)
                path = directory / route.rsplit('/', 1)[1]
                self.respond(200, path.read_bytes(), 'application/octet-stream')
            else:
                self.respond(404, {'error': 'Not found'})
        def do_POST(self):
            if not self.authorized():
                self.respond(403, {'error': 'Invalid local authorization/origin'}); return
            try:
                length = int(self.headers.get('Content-Length', '0'))
                if length < 1 or length > 32000:
                    raise ValueError('Invalid request size')
                payload = json.loads(self.rfile.read(length))
                if not isinstance(payload, dict):
                    raise ValueError('Expected an object')
                if self.path == '/api/result':
                    record_result(data, payload, stale(data))
                elif self.path == '/api/operator':
                    if stale(data) or payload.get('revision') != data['revision']:
                        raise ValueError('Stale run/page; reload or create a fresh run')
                    values = {k: payload.get(k, '') for k in ['operator', 'appVersion', 'device']}
                    if any(not isinstance(v, str) or len(v) > 200 for v in values.values()):
                        raise ValueError('Invalid operator details')
                    data.update(values); data['revision'] += 1
                else:
                    self.respond(404, {'error': 'Not found'}); return
                save(directory, data)
                self.respond(200, {'revision': data['revision']})
            except (ValueError, TypeError, KeyError, UnicodeError) as error:
                self.respond(409, {'error': str(error)})
    return http.server.HTTPServer(('127.0.0.1', port), Handler)


def serve(directory, data, port=8765):
    token = secrets.token_urlsafe(32)
    server = build_server(directory, data, port, token)
    server.timeout = 1
    print(f'\nHardware checklist: http://127.0.0.1:{server.server_port}/#{token}', flush=True)
    print(f'Resume: python3 tools/desktop-test/run.py serve --run {directory}\nCtrl+C stops the server; each saved result is retained.', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print('\nProgress saved.')
    finally:
        server.server_close(); export(directory, data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['auto', 'start', 'check', 'new', 'serve', 'report', 'catalog', 'fixture', 'audio'])
    parser.add_argument('--run', help='Run directory or ID; serve/report default to latest')
    parser.add_argument('--package', type=Path, help='Specific package archive to fingerprint and verify (offline)')
    parser.add_argument('--suite', choices=['desktop', 'full'], help='auto defaults to full; start/check default to desktop')
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--wav', type=Path, help='Fixed test audio for optional inference; never a microphone')
    parser.add_argument('--expected', help='Expected fixed audio transcript')
    parser.add_argument('--whisper', type=Path)
    parser.add_argument('--model', type=Path)
    args = parser.parse_args()
    if args.action == 'catalog':
        layouts = validate()
        print(f"Coverage valid: {len(read_catalog()['commands'])} command/parameter entries; {len(layouts)} profiles; {len(owner_cases(read_catalog(), layouts))} owner cases.")
        return 0
    if args.package and not args.package.is_file():
        raise ValueError('Package does not exist: ' + str(args.package))
    if args.action in ['auto', 'start', 'check', 'new'] and not args.run:
        directory, data = new_run(args.package)
    else:
        directory = resolve_run(args.run); data = load(directory)
        if args.package and str(args.package.resolve()) != data['fingerprint']['package']:
            raise ValueError('A different package requires a new run')
    print('Run:', directory, flush=True)
    with locked(directory):
        if args.action == 'auto':
            code = automate(directory, data, args.suite or 'full', args.wav, args.expected, args.whisper, args.model)
            print(json.dumps(summary(data, stale(data)), indent=2))
            print('Automation:', data['automation']['status'], '— exit', code)
            print('Report:', directory / 'report.html')
            print('View dashboard: python3 tools/desktop-test/run.py serve --run', data['id'])
            return code
        if args.action in ['start', 'check', 'fixture', 'audio']:
            # An individual retry is not the original unattended pipeline result.
            data.pop('automation', None)
        if args.action in ['start', 'check']:
            ok = check(directory, data, args.suite or 'desktop')
            if args.action == 'check':
                print(json.dumps(summary(data, stale(data)), indent=2)); return 0 if ok else 1
        if args.action in ['start', 'serve']:
            serve(directory, data, args.port)
        elif args.action == 'fixture':
            if stale(data): raise ValueError('Run is stale; create a new run')
            from fixture import run_fixture
            data['checks']['fixture-app'] = run_fixture(directory)
            save(directory, data); print(json.dumps(data['checks']['fixture-app'], indent=2)); export(directory, data)
            return 0 if data['checks']['fixture-app']['status'] == 'PASS' else 2
        elif args.action == 'audio':
            if stale(data): raise ValueError('Run is stale; create a new run')
            from inference import run_inference
            data['checks']['audio-inference'] = run_inference(directory, args.wav, args.expected, args.whisper, args.model)
            save(directory, data); print(json.dumps(data['checks']['audio-inference'], indent=2)); export(directory, data)
            return 0 if data['checks']['audio-inference']['status'] == 'PASS' else 2
        else:
            print(json.dumps(export(directory, data), indent=2))
            print('Report:', directory / 'report.html')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, OSError) as error:
        print('Error:', error, file=sys.stderr); raise SystemExit(2)
