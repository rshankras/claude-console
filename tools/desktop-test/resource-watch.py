#!/usr/bin/env python3
"""Opt-in macOS process sampling. No UI, clipboard, audio, or conversation inspection."""
import argparse
import json
import os
from pathlib import Path
import statistics
import subprocess
import time

SERVICE = '/Applications/Utilities/LogiPluginService.app/Contents/MacOS/LogiPluginService'


def sample():
    rows = []
    output = subprocess.run(['ps', '-axo', 'pid=,ppid=,%cpu=,rss=,time=,comm='],
                            check=True, capture_output=True, text=True).stdout
    for line in output.splitlines():
        parts = line.strip().split(None, 5)
        if len(parts) != 6:
            continue
        pid, parent, cpu, rss, cpu_time, executable = parts
        if executable != SERVICE and Path(executable).name != 'VizhiAxBridge':
            continue
        rows.append(dict(pid=int(pid), parent=int(parent), cpu_percent=float(cpu),
                         rss_mib=round(int(rss) / 1024, 3), cpu_time=cpu_time,
                         kind='service' if executable == SERVICE else 'ax-helper'))
    return dict(elapsed_seconds=0, processes=rows)


def summarize(samples):
    hosts = {}
    for point in samples:
        for row in point['processes']:
            if row['kind'] == 'service':
                hosts.setdefault(row['pid'], []).append(row)
    result = []
    for pid, rows in hosts.items():
        rss = [row['rss_mib'] for row in rows]
        window = max(1, min(5, len(rows) // 2))
        result.append(dict(pid=pid, samples=len(rows), rss_min_mib=min(rss), rss_max_mib=max(rss),
                           rss_first_median_mib=statistics.median(rss[:window]),
                           rss_last_median_mib=statistics.median(rss[-window:]),
                           cpu_median_percent=statistics.median(row['cpu_percent'] for row in rows),
                           cpu_max_percent=max(row['cpu_percent'] for row in rows)))
    return dict(service_processes=result, sampled_helper_peak=max(
        (sum(p['kind'] == 'ax-helper' for p in s['processes']) for s in samples), default=0),
        limitation='Shared Logitech host, sampled observations only. Short-lived helpers may be missed. '
                   'This is not heap attribution or proof that no long-term leak exists.')


def write(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(report, indent=2) + '\n')
    os.replace(temp, path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=float, default=300)
    parser.add_argument('--interval', type=float, default=5)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.seconds < 0 or not 1 <= args.interval <= 60:
        parser.error('Use nonnegative seconds and an interval from 1 to 60 seconds.')
    start = time.monotonic()
    report = dict(status='sampling', samples=[])
    try:
        while True:
            point = sample()
            point['elapsed_seconds'] = round(time.monotonic() - start, 3)
            report['samples'].append(point)
            report['summary'] = summarize(report['samples'])
            write(args.output, report)
            remaining = args.seconds - (time.monotonic() - start)
            if remaining <= 0:
                break
            time.sleep(min(args.interval, remaining))
        report['status'] = 'complete'
    except KeyboardInterrupt:
        report['status'] = 'interrupted'
    finally:
        write(args.output, report)
    print(json.dumps(report['summary'], indent=2))


if __name__ == '__main__':
    main()
