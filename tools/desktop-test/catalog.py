"""Coverage gate and owner-driven cases. Reading configuration never launches an app."""
import hashlib
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'tests/desktop/commands.json'
PROFILE_ROOT = ROOT / 'src/Products/VizhiDesktop/package'


def read_catalog():
    return json.loads(CATALOG.read_text())


def registered_parameters(source, root=ROOT):
    """Fail closed on an unrecognized registration expression, including a new loop shape.

    This is an inventory check, not execution evidence; C# scenarios call production handlers.
    User-defined workflow IDs are captured separately from the shipped/default catalog.
    """
    constants = dict(re.findall(r'const String (\w+) = "([^"]*)";', source))
    result = set()
    for expr in re.findall(r'this\.AddParameter\(([^,\n]+),', source):
        if re.fullmatch(r'"[^"]*"', expr):
            result.add(expr[1:-1])
        elif expr in constants:
            result.add(constants[expr])
        elif expr == 'i.ToString()' and 'i <= Slots' in source:
            count_source = (root / 'src/Core/Desktop/DesktopSlotMap.cs').read_text()
            count = int(re.search(r'SlotCount = (\d+)', count_source)[1])
            result.update(str(i) for i in range(1, count + 1))
        elif expr in ('$"slot_{slot}"', '$"task_{slot}"') and 'slot <= 9' in source:
            prefix = 'task' if 'task_' in expr else 'slot'
            result.update(f'{prefix}_{i}' for i in range(1, 10))
        elif expr == 'id' and 'foreach (var id in Pairs.Keys)' in source:
            keys = re.findall(r'\[(\w+)\] = new Pair', source)
            result.update(constants[k] for k in keys)
        elif expr == 'w.Id' and '_codexWorkflows.Concat(ExtraWorkflows)' in source:
            for name in ['CodexDefaults', 'ExtraWorkflows']:
                block = source.split('WorkflowDef[] ' + name + ' =', 1)[1].split('\n        };', 1)[0]
                result.update(re.findall(r'Id = "([^"]+)"', block))
        else:
            raise ValueError(f'Uncovered registration expression: {expr}')
    return result or ({'*'} if ': PluginDynamicFolder' in source else {''})


def profiles(root=ROOT):
    result = []
    for path in sorted((root / 'src/Products/VizhiDesktop/package').rglob('*.lp5')):
        with zipfile.ZipFile(path) as package:
            profile = json.loads(package.read('ProfileInfo.json'))
        pages = []
        for layout in profile['layout']['layoutModes']:
            for workspace in layout['workspaces']:
                for page in workspace['pressPages']:
                    keys = []
                    for control in page['controls']:
                        binding = control.get('pressAction') or ''
                        if not binding.startswith('$VizhiDesktop___'):
                            raise ValueError(f'Uncovered non-Desktop binding: {binding}')
                        parts = binding.split('___')
                        folder = parts[1] == '#DynamicFolder'
                        command = (parts[2] if folder else parts[1]).split('.')[-1]
                        parameter = '*' if folder else parts[2] if len(parts) > 2 else ''
                        keys.append(dict(position=control['controlId'] + 1, command=command, parameter=parameter))
                    if sorted(k['position'] for k in keys) != list(range(1, 10)):
                        raise ValueError('Profile is no longer a 3×3 keypad: ' + page['displayName'])
                    pages.append(dict(name=page['displayName'], keys=keys))
        result.append(dict(id=profile['name'], name=profile['displayName'], pages=pages,
                           path=str(path.relative_to(root))))
    if not result:
        raise ValueError('No packaged profiles found')
    return result


def validate(catalog=None, root=ROOT):
    catalog = catalog or read_catalog()
    commands = catalog['commands']
    ids = [c['id'] for c in commands]
    if len(set(ids)) != len(ids):
        raise ValueError('Duplicate catalog IDs')
    declared = {(c['command'], c['parameter']) for c in commands}
    actual = set()
    for file in (root / 'src/Core/DesktopActions').glob('*.cs'):
        source = file.read_text()
        for abstract, cls in re.findall(r'\b(?:(abstract)\s+)?class\s+(\w+)\s*:\s*(?:Loupedeck\.)?(?:PluginDynamic(?:Command|Folder)|DesktopCommandBase)\b', source):
            if abstract:
                continue
            actual.update((cls, p) for p in registered_parameters(source, root))
    if actual != declared:
        raise ValueError(f'Catalog drift: missing={sorted(actual-declared)}, obsolete={sorted(declared-actual)}')
    for c in commands:
        if c['modes'] != ['ChatGPT', 'Codex']:
            raise ValueError('Both adaptive modes need coverage: ' + c['id'])
        for field in ['title', 'prepare', 'action', 'expectedApp', 'expectedKey', 'kind']:
            if not c.get(field):
                raise ValueError('Missing ' + field + ': ' + c['id'])
    for scenario in catalog['manualScenarios']:
        if scenario['parent'] not in ids or scenario['id'] in ids:
            raise ValueError('Invalid manual scenario: ' + scenario['id'])
        ids.append(scenario['id'])
    layouts = profiles(root)
    for profile in layouts:
        for page in profile['pages']:
            for key in page['keys']:
                if (key['command'], key['parameter']) not in declared:
                    raise ValueError('Uncovered profile binding: ' + str(key))
    return layouts


def in_mode(value, mode):
    return value.get(mode, '') if isinstance(value, dict) else value


def owner_cases(catalog, layouts):
    """One case for each actual binding × mode, plus optional actions and regressions.
    A pass on one profile never auto-passes another physical binding.
    """
    cases = []
    for profile in layouts:
        for page in profile['pages']:
            for key in page['keys']:
                c = next(c for c in catalog['commands'] if (c['command'], c['parameter']) == (key['command'], key['parameter']))
                for mode in c['modes']:
                    cases.append(make_case(c, mode, profile['name'], page['name'], key['position']))
    bound = {(k['command'], k['parameter']) for p in layouts for page in p['pages'] for k in page['keys']}
    for c in catalog['commands']:
        if (c['command'], c['parameter']) not in bound:
            for mode in c['modes']:
                cases.append(make_case(c, mode, 'Optional actions', 'Assign a spare key', None))
    for s in catalog['manualScenarios']:
        parent = next(c for c in catalog['commands'] if c['id'] == s['parent'])
        for mode in s['modes']:
            case = make_case(dict(parent, **s), mode, 'Regression checks', 'Use assigned action', None)
            case['automatedCase'] = None  # related unit tests are not proof of this exact live procedure
            case['relatedCommand'] = s['parent'] + '.' + mode.lower()
            cases.append(case)
    return cases


def make_case(c, mode, profile, page, position):
    return dict(id=f"{profile}/{page}/{position or 'assigned'}/{c['id']}.{mode.lower()}", commandId=c['id'], command=c['command'], parameter=c['parameter'],
                mode=mode, profile=profile, page=page, position=position,
                title=in_mode(c['title'], mode), prepare=c['prepare'], action=c['action'],
                expectedApp=c['expectedApp'], expectedKey=c['expectedKey'], smoke=c['smoke'],
                capability=c.get('capabilities', {}).get(mode, c.get('capability', 'supported')),
                automatedCase=c['id'] + '.' + mode.lower())


def add_workflow_configuration(cases, home=Path.home()):
    configurations = {}
    for mode, filename in [('ChatGPT', 'desktop-chatgpt-workflows.json'), ('Codex', 'desktop-workflows.json')]:
        path = home / '.claude/claude-console' / filename
        if not path.exists():
            configurations[mode] = {'status': 'default (no local override)'}
            continue
        try:
            items = json.loads(path.read_text())
            if not isinstance(items, list):
                raise ValueError('Expected a workflow array')
            if any(x is not None and not isinstance(x, dict) for x in items):
                raise ValueError('Workflow entries must be objects or null')
            valid = [dict((k.lower(), v) for k, v in x.items()) for x in items if isinstance(x, dict)]
            for workflow in valid:
                if any(workflow.get(k) is not None and not isinstance(workflow[k], str)
                       for k in ['id', 'label', 'icon', 'prompt', 'input']):
                    raise ValueError('Workflow text properties must be strings or null')
                if workflow.get('submit') is not None and not isinstance(workflow['submit'], bool):
                    raise ValueError('Workflow Submit must be a boolean or null')
            valid = [x for x in valid if isinstance(x.get('id'), str) and x['id'].strip()
                     and isinstance(x.get('prompt'), str) and x['prompt'].strip()
                     and (str(x.get('input', '')).lower() != 'voice' or '{brief}' in x['prompt'])][:9]
            # Empty arrays trigger production defaults, but nonempty unusable arrays do not.
            configurations[mode] = {'status': 'custom' if items else 'default (empty override)',
                                    'path': str(path), 'entries': valid}
            if not items:
                continue
            for case in cases:
                p = case['parameter']
                if case['command'] == 'DesktopToolsCommand' and mode == 'Codex' and case['mode'] == mode:
                    p = {'clipboard_review':'review_changes', 'screenshot_tests':'run_tests'}.get(p)
                    if not p: continue
                elif case['command'] != 'DesktopWorkflowCommand':
                    continue
                if (p.startswith('slot_') or (p.startswith('task_') and mode == 'Codex')) and case['mode'] == mode:
                    index = int(p[5:]) - 1
                    workflow = valid[index] if index < len(valid) else None
                elif p == 'draft_reply' and mode == 'ChatGPT' and case['mode'] == mode:
                    workflow = next((w for w in valid if w['id'] == 'draft'), None)
                elif not p.startswith(('slot_', 'task_')) and p != 'draft_reply' and mode == 'Codex':
                    workflow = next((w for w in valid if w['id'] == p), None)
                    if workflow is None:
                        continue
                else:
                    continue
                case['configuredWorkflow'] = workflow
                if workflow:
                    case['title'] = workflow.get('label') or workflow['id']
                    case['expectedKey'] = case['title'] + ' / ' + ('SPEAK' if str(workflow.get('input', '')).lower() == 'voice' else 'DRAFT' if workflow.get('submit') is False else 'SEND')
                else:
                    case['expectedApp'] = 'This configured slot has no usable workflow; no text should be written.'
                    case['expectedKey'] = 'Mode? / UNAVAILABLE'
            known = {c['parameter'] for c in cases if c['command'] == 'DesktopWorkflowCommand'}
            for workflow in valid:
                if mode == 'Codex' and workflow['id'] not in known:
                    for app_mode in ['ChatGPT', 'Codex']:
                        c = next(c.copy() for c in cases if c['command'] == 'DesktopWorkflowCommand' and c['profile'] == 'Optional actions')
                        c.update(id=f"Custom actions/{workflow['id']}.{app_mode.lower()}", parameter=workflow['id'],
                                 commandId='custom.' + workflow['id'], mode=app_mode, profile='Custom actions', title=workflow.get('label') or workflow['id'],
                                 configuredWorkflow=workflow, automatedCase=None, smoke=False,
                                 expectedApp='The configured named Codex brief is drafted or sent exactly once.',
                                 expectedKey='SPEAK' if str(workflow.get('input', '')).lower() == 'voice' else 'DRAFT' if workflow.get('submit') is False else 'SEND')
                        cases.append(c)
                    known.add(workflow['id'])
        except (ValueError, OSError, TypeError) as error:
            configurations[mode] = {'status': 'unreadable; production falls back to defaults', 'reason': str(error)}
    return configurations
