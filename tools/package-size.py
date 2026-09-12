#!/usr/bin/env python3
"""Report compressed .lplug4 contents; sizes are download bytes, not extracted file sizes."""
import argparse
from collections import defaultdict
from pathlib import Path
import zipfile


def breakdown(path):
    groups = defaultdict(int)
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            name = entry.filename.replace('\\', '/')
            if name.startswith('bin/claude-console-') and name.endswith('.exe'):
                group = name.rsplit('/', 1)[-1]
            elif name.startswith('bin/voice/'):
                group = 'voice payload (both platforms)'
            else:
                group = 'plugin and metadata'
            groups[group] += entry.compress_size
    groups['ZIP overhead'] = Path(path).stat().st_size - sum(groups.values())
    return groups


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package', type=Path)
    parser.add_argument('--compare', type=Path)
    args = parser.parse_args()
    for name, size in sorted(breakdown(args.package).items(), key=lambda row: -row[1]):
        print(f'{size / 2**20:7.2f} MiB  {name}')
    size = args.package.stat().st_size
    print(f'{size / 2**20:7.2f} MiB  TOTAL ({size:,} bytes)')
    if args.compare:
        old = args.compare.stat().st_size
        print(f'Saving: {(old-size)/2**20:.2f} MiB ({100*(old-size)/old:.1f}%) versus {args.compare.name}')


if __name__ == '__main__':
    main()
