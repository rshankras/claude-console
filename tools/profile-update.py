#!/usr/bin/env python3
"""Prepare an exported-profile backup and explicit update; never access Options+ storage."""
import argparse
import hashlib
import json
import io
from pathlib import Path
import re
import uuid
import zipfile


def read_profile(path):
    with zipfile.ZipFile(io.BytesIO(path) if isinstance(path, bytes) else path) as archive:
        if len(archive.namelist()) != len(set(archive.namelist())):
            raise ValueError('duplicate archive entries are not supported')
        files = {name: archive.read(name) for name in archive.namelist()}
    profile = json.loads(files['ProfileInfo.json'])
    app = json.loads(files['ApplicationInfo.json'])
    if profile['applicationName'] != app['name']:
        raise ValueError('profile and application host identities disagree')
    return files, profile, app


def changes(old, new, path=''):
    """Report all fields, including macros, wheels, and custom bindings; never infer a merge."""
    if isinstance(old, dict) and isinstance(new, dict):
        result = []
        for key in sorted(old.keys() | new.keys()):
            pointer = path + '/' + key.replace('~', '~0').replace('/', '~1')
            if key not in old or key not in new:
                result.append({'path': pointer, 'current': old.get(key), 'updated': new.get(key)})
            else:
                result.extend(changes(old[key], new[key], pointer))
        return result
    if isinstance(old, list) and isinstance(new, list):
        result = []
        for index in range(max(len(old), len(new))):
            pointer = path + '/' + str(index)
            if index >= len(old) or index >= len(new):
                result.append({'path': pointer, 'current': old[index] if index < len(old) else None,
                               'updated': new[index] if index < len(new) else None})
            else:
                result.extend(changes(old[index], new[index], pointer))
        return result
    return [] if old == new else [{'path': path, 'current': old, 'updated': new}]


def prepare(current, updated, output, mode, revision):
    if mode not in ('keep-custom', 'adopt-defaults'):
        raise ValueError('unsupported update mode')
    current, updated, output = Path(current), Path(updated), Path(output)
    old_bytes, new_bytes = current.read_bytes(), updated.read_bytes()
    _, old, old_app = read_profile(old_bytes)
    files, new, new_app = read_profile(new_bytes)
    if (old['applicationName'], old.get('deviceType'), old_app.get('processOrBundleName')) != (
            new['applicationName'], new.get('deviceType'), new_app.get('processOrBundleName')):
        raise ValueError('choose an updated profile for the same platform, terminal and device')
    products = lambda p: set(p.get('additionalNativePluginNames', [])) & {'VizhiCodex', 'ClaudeConsole'}
    if len(products(new)) != 1 or products(old) != products(new):
        raise ValueError('current and updated profiles must belong to the same product')
    old_hash, new_hash = (hashlib.sha256(data).hexdigest() for data in (old_bytes, new_bytes))
    report = {'revision': revision, 'mode': mode, 'current_sha256': old_hash, 'updated_sha256': new_hash,
              'host': new['applicationName'], 'differences': changes(old, new),
              'instructions': 'Keep the original imported profile. Review differences.json. '
              + ('Apply desired action changes manually to your custom layout.' if mode == 'keep-custom' else
                 'Import candidate.lp5 using Options+ and select the new profile. Verify it appears separately before removing anything. '
                 'If it does not, stop and retain the original. To roll back, select the original profile or import backup.lp5.')}
    # Refuse reuse so no backup or candidate from another run is overwritten.
    output.mkdir(parents=True, exist_ok=False)
    (output / 'backup.lp5').write_bytes(old_bytes)
    if mode == 'adopt-defaults':
        identity = uuid.uuid5(uuid.NAMESPACE_URL, 'vizhi-profile-update:' + old_hash + ':' + new_hash + ':' + revision).hex.upper()
        title = new['displayName'] + ' — Update ' + revision
        new['name'] = identity
        new['packageName'] = identity
        new['displayName'] = title
        new_app['defaultProfileName'] = identity
        files['ProfileInfo.json'] = json.dumps(new, indent=2).encode()
        files['ApplicationInfo.json'] = json.dumps(new_app, indent=2).encode()
        metadata = files['metadata/LoupedeckPackage.yaml'].decode()
        metadata = re.sub(r'^name:.*$', 'name: ' + identity, metadata, flags=re.M)
        metadata = re.sub(r'^displayName:.*$', 'displayName: ' + json.dumps(title, ensure_ascii=False), metadata, flags=re.M)
        files['metadata/LoupedeckPackage.yaml'] = metadata.encode()
        files['metadata/AdvancedInfo.json'] = json.dumps({'additionalPluginNames': sorted(products(new))}, indent=2).encode()
        with zipfile.ZipFile(output / 'candidate.lp5', 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, data in files.items():
                archive.writestr(name, data)
        report['candidate_identity'] = identity
        report['candidate_sha256'] = hashlib.sha256((output / 'candidate.lp5').read_bytes()).hexdigest()
    (output / 'differences.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')
    (output / 'README.txt').write_text(report['instructions'] + '\n\n'
        'The backup is the exact supplied export. This tool has not installed or changed any profile.\n'
        'JSON paths use zero-based indices: pressPages/0 is page 1; controlId identifies the key.\n'
        'Differences include all settings, custom macros and bindings, not only key positions.\n')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--current', required=True, help='Your profile exported from Options+, not an installed database')
    parser.add_argument('--updated', required=True, help='New default .lp5 for the same platform/product')
    parser.add_argument('--output', required=True, help='New directory for backup and review files')
    parser.add_argument('--mode', choices=['keep-custom', 'adopt-defaults'], default='keep-custom')
    parser.add_argument('--revision', default='2026-09-14.1')
    args = parser.parse_args()
    try:
        report = prepare(args.current, args.updated, args.output, args.mode, args.revision)
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(1, str(error) + '\n')
    print(f"Prepared {len(report['differences'])} differences in {args.output}. No installed profile was changed.")


if __name__ == '__main__':
    main()
