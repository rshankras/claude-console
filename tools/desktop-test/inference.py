"""Optional fixed-file Whisper inference; never invokes the recorder or microphone."""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

PHRASE = 'This is a test of local voice transcription. The draft should remain ready for review.'


def file_hash(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''): h.update(block)
    return h.hexdigest()


def normalize(text):
    return re.findall(r"[a-z0-9]+", text.lower())


def word_error_rate(expected, actual):
    a, b = normalize(expected), normalize(actual)
    if not a: raise ValueError('Expected transcript must contain words')
    row = list(range(len(b) + 1))
    for i, word in enumerate(a, 1):
        current = [i]
        for j, other in enumerate(b, 1):
            current.append(min(current[-1]+1, row[j]+1, row[j-1]+(word != other)))
        row = current
    return row[-1] / len(a)


def run_inference(directory, wav=None, expected=None, whisper=None, model=None):
    runtime = Path.home() / '.claude/claude-console'
    whisper = whisper or runtime / 'whisper-bin/whisper-cli'
    model = model or runtime / 'whisper/ggml-base.en.bin'
    missing = [str(p) for p in [whisper, model] if not p.is_file()]
    if missing: return dict(status='BLOCKED', reason='Missing local runtime/model: ' + ', '.join(missing))
    if wav is not None and (not wav.is_file() or not expected):
        return dict(status='BLOCKED', reason='A provided WAV requires an existing --wav and nonempty --expected text')
    work = directory / 'audio'
    work.mkdir(exist_ok=True)
    try:
        with (work / 'inference.log').open('w') as log:
            if wav is None:
                if sys.platform != 'darwin': return dict(status='BLOCKED', reason='Provide --wav and --expected on this platform')
                # say writes to a file, without playing it. No audio device or microphone is opened.
                expected = PHRASE
                aiff, wav = work / 'fixed-test.aiff', work / 'fixed-test.wav'
                subprocess.run(['/usr/bin/say', '-o', str(aiff), expected], check=True, stdout=log, stderr=log, timeout=30)
                subprocess.run(['/usr/bin/afconvert', '-f', 'WAVE', '-d', 'LEI16@16000', '-c', '1', str(aiff), str(wav)], check=True, stdout=log, stderr=log, timeout=30)
            prefix = work / 'transcript'
            textfile = prefix.with_suffix('.txt'); textfile.unlink(missing_ok=True)
            result = subprocess.run([str(whisper), '-m', str(model), '-f', str(wav), '-l', 'en', '-nt', '-otxt', '-of', str(prefix)], stdout=log, stderr=log, timeout=120)
            if result.returncode != 0 or not textfile.is_file():
                return dict(status='FAIL', reason='Whisper did not produce a transcript', exitCode=result.returncode, log='audio/inference.log')
            actual = textfile.read_text().strip()
            error = word_error_rate(expected, actual)
            return dict(status='PASS' if error <= .15 else 'FAIL', wordErrorRate=round(error, 4), maximumWordErrorRate=.15,
                        expected=expected, transcript=actual, wav=str(wav), audioHash=file_hash(wav),
                        whisperHash=file_hash(whisper), modelHash=file_hash(model), log='audio/inference.log',
                        limitation='Fixed-file inference only; microphone capture and app insertion still require owner checks.')
    except (OSError, subprocess.SubprocessError, ValueError) as error:
        return dict(status='FAIL', reason=str(error), log='audio/inference.log')
