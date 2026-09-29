#!/usr/bin/env python3
"""Compare local models on the same explicit audio manifest. Never records or controls apps.

Manifest: {"samples": [{"id": "1", "wav": "/path.wav", "expected": "words"}]}
Results include every sample (including failures), model/audio hashes and wall time including
model loading. Use a fixed sample selection before running; this is not a training benchmark.
"""
import argparse
import json
import statistics
import subprocess
import sys
import time
from pathlib import Path
from inference import file_hash, normalize, word_error_rate


def benchmark(manifest, models, whisper, output, without_homebrew=False):
    output.mkdir(parents=True, exist_ok=True)
    prefix = []
    if without_homebrew:
        if sys.platform != 'darwin': raise ValueError('--without-homebrew requires macOS')
        sandbox = output / 'no-homebrew.sb'
        sandbox.write_text('(version 1)\n(allow default)\n(deny file-read* (subpath "/opt/homebrew"))\n')
        prefix = ['/usr/bin/sandbox-exec', '-f', str(sandbox)]
    data = json.loads(manifest.read_text())
    samples = data['samples']
    if not samples or len({s['id'] for s in samples}) != len(samples):
        raise ValueError('Manifest requires samples with unique IDs')
    result = dict(manifestHash=file_hash(manifest), whisperHash=file_hash(whisper),
                  homebrewDenied=without_homebrew, source=data.get('source'), selection=data.get('selection'), models={},
                  limitation='Fixed-file read speech; does not establish owner accent/slang quality or microphone/app/hardware behavior.')
    for model in models:
        runs = []
        for index, sample in enumerate(samples):
            wav = Path(sample['wav'])
            if not wav.is_absolute(): wav = manifest.parent / wav
            audio_hash = file_hash(wav)
            if sample.get('sha256') and sample['sha256'] != audio_hash:
                raise ValueError(f'Audio hash mismatch: {sample["id"]}')
            start = time.monotonic()
            args = prefix + [str(whisper), '-m', str(model), '-f', str(wav), '-nt']
            # Match the installed helper exactly: its current language default is English.
            with (output / f'{model.stem}-{index:03}.log').open('w') as log:
                try:
                    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=log, text=True, timeout=120)
                    text = p.stdout.strip()
                    run = dict(id=sample['id'], status='OK' if p.returncode == 0 else 'FAIL', exitCode=p.returncode,
                               expected=sample['expected'], transcript=text, audioHash=audio_hash,
                               seconds=round(time.monotonic()-start, 3))
                    if p.returncode == 0:
                        words = len(normalize(sample['expected']))
                        run.update(words=words, wordErrors=round(word_error_rate(sample['expected'], text)*words))
                except subprocess.TimeoutExpired:
                    run = dict(id=sample['id'], status='TIMEOUT', seconds=120, audioHash=audio_hash)
                runs.append(run)
                print(model.name, sample['id'], run['status'], run['seconds'], run.get('wordErrors'), flush=True)
        good = [r for r in runs if r['status'] == 'OK']
        result['models'][model.name] = dict(hash=file_hash(model), bytes=model.stat().st_size, runs=runs,
            failures=len(runs)-len(good), wordErrorRate=sum(r['wordErrors'] for r in good)/sum(r['words'] for r in good) if good else None,
            medianSeconds=statistics.median(r['seconds'] for r in good) if good else None,
            maxSeconds=max((r['seconds'] for r in runs), default=None))
        (output / 'results.json').write_text(json.dumps(result, indent=2))
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--model', type=Path, action='append', required=True)
    parser.add_argument('--whisper', type=Path, default=Path.home()/'.claude/claude-console/whisper-bin/whisper-cli')
    parser.add_argument('--without-homebrew', action='store_true', help='macOS: deny /opt/homebrew so backends must load from the bundle')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = benchmark(args.manifest, args.model, args.whisper, args.output, args.without_homebrew)
    print(json.dumps({name: {k: v for k, v in value.items() if k != 'runs'} for name, value in result['models'].items()}, indent=2))
