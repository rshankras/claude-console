# Speak Query accuracy — 0.12.9

Implemented on `integrate/vizhi-desktop-main`. Scope: local **Find Chat → Speak Query** on
macOS. Native ChatGPT Voice Chat still uses the app shortcut. Composer dictation, Voice Draft
and terminal products retain their existing base.en model. No model selector or profile rebinding.

## Decision and user experience

Use **Whisper large-v3-turbo Q5_0** as one default for Speak Query. On plugin load, prepare the
model automatically in the background. The keypad shows **Preparing Voice / Download N%**,
then **Speak Query / SEARCH** when both the model and app search field are ready. Options+ also
shows download progress. A failure shows **Voice Setup / Tap to retry**. Retry works even when
app search is unavailable. Type Query stays usable while the model downloads.

The model is 574,041,195 bytes (574 MB / 547 MiB), downloaded separately from the plugin and
cached at `~/.claude/claude-console/whisper/ggml-large-v3-turbo-q5_0.bin`. No API key, cloud audio
upload, or model choice is required. Transcription works offline once the runtime and model are
installed. Internet is needed to fetch or repair the weights.

Preparation never starts the microphone. After readiness, press Speak Query, speak, and press
again to finish. The existing search session/window/query guards still control delivery. Back
invalidates the capture's destination. The native app search field remains the place to review
and correct the recognized query. A stronger model cannot guarantee exact recognition.

## Comparison performed on the owner's Mac

Apple M1 Pro, 16 GiB. Same existing whisper-cli, same decoder defaults, English transcription,
16 kHz mono PCM. Each measurement launches a fresh process, including model loading; filesystem
cache was not flushed. No microphone or ChatGPT app control was used.

Fixed selection: first 20 train rows from
[Common Voice Indian-accent subset](https://huggingface.co/datasets/ishands/commonvoice-indian_accent),
revision `e438aca479db7f4773411400fbd5ac11606fe2dd`. All 20 rows were selected before inference,
with 190 reference words and distinct speaker IDs. The source includes South Asian and mixed
accent labels. These are read sentences, often proper names, not the owner's conversational
speech. The dataset card describes CC0 provenance. This is not a held-out training-overlap
claim, a statistically conclusive benchmark, or proof of Tamil/code-switching quality.

Final comparison denied access to `/opt/homebrew`, forcing compute backends to load from the
shipped bundle. Each of the 60 inferences completed successfully.

| Model | Word errors / 190 | WER | Median time | Max time | Download |
|---|---:|---:|---:|---:|---:|
| base.en | 61 | 32.1% | 0.335 s | 0.388 s | 148 MB |
| small.en | 51 | 26.8% | 0.649 s | 0.772 s | 488 MB |
| large-v3-turbo Q5_0 | 49 | 25.8% | 1.463 s | 1.546 s | 574 MB |

Turbo made about 20% fewer word errors than base.en here. Its lead over small.en is only two
words; the sample does not establish a robust difference between those two models. Turbo was
chosen for the lowest observed errors with acceptable short-query latency on this Mac. It still
regressed on several individual utterances. Other Macs, noise conditions and accents need
validation. A 60-second repeated-sentence fixture took 2.93 s to transcribe, within the existing
20-second delivery wait, but omitted repetitions; that test establishes latency, not long-form
transcript completeness. Speak Query is intended for short topics.

Evidence (ignored artifacts, on this machine):
- `artifacts/desktop-search-voice/benchmark-manifest.json`: selection, references, audio hashes.
- `artifacts/desktop-search-voice/benchmark-bundled/results.json`: all transcripts, word errors,
  model/CLI hashes and timings; per-inference logs identify bundled compute backends.
- `artifacts/desktop-search-voice/edge-inference.json`: silence and 60-second fixture results.
- `artifacts/desktop-search-voice/key-preview/`: rendered 90-pixel preparation/retry faces.

## Download and capture safeguards

Model revision pinned to `5359861c739e955e79d9a303bcbc70fb988958b1` in
[whisper.cpp model repository](https://huggingface.co/ggerganov/whisper.cpp).
SHA-256: `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2`.
Existing weights are hashed asynchronously once per load; new transfers require both size and
SHA-256 validation before atomic promotion. Concurrent presses share one transfer. Failures keep
an existing file intact, remove partial output and allow an explicit retry. Unload cancels the
transfer; a later load restarts it. Transfers have a 30-minute budget. There is no silent fallback
to a less accurate model, and no automatic recording when a download finishes.

All three models returned "you" from five seconds of digital silence. Speak Query now validates
the completed WAV before delivering text: silence or near-digital silence reports No speech;
invalid/truncated audio reports No response. The gate accepts a 20 ms window above -60 dBFS.
It preserves quiet signal but is not voice activity detection: environmental noise can still
pass. Unit tests cover silence, quiet audio, extra WAV chunks and malformed input. New search
transcript success logging records character count rather than the query text.

The signed microphone helper is unchanged: it already accepts an explicit model path. This
avoids replacing its identity or microphone permission. The macOS helper still uses English
recognition; choosing a multilingual weight does not itself add a language-selection workflow.

## Validation and remaining owner checks

Automated validation covers model checksum/size failures and retry, offline reuse, single-flight
transfers, cancellation, deleted/corrupted cache, failed renderer callbacks, product/intent
selection, capture refusal during preparation, and Type Query independence. The existing full
software harness also covers all command variants and the controlled GUI fixture. Its hardware
and actual ChatGPT checks remain separately marked untested.

Owner acceptance: try previously misheard short queries, then check silence, noisy surroundings,
Back during transcription, and query correction. Record what was said and the resulting text.
A user-recorded fixed WAV can be added to a local benchmark manifest without recording through
this harness. Actual owner accent/slang quality and keypad behavior have not been claimed as
passing.

The 0.12.9 repository run passed 1,766 C# tests (13 Windows-only skips), script suites, 20
harness self-tests, all 92 command variants, offline package verification and fixed-file
inference. The GUI fixture could not activate because the console was locked, including on
retry; its current result remains BLOCKED. Its helper SHA-256 is byte-identical to the prior
0.12.8 helper that passed all 18 fixture steps. This is prior evidence for the unchanged helper,
not a new fixture pass. The release receipt records this exception and the installation hashes.
The harness fingerprints the installed Turbo weights so replacing them invalidates old evidence.

## Re-run a model comparison

`benchmark-voice.py` accepts a manifest with `samples: [{id, wav, expected}]`; relative WAV paths
resolve beside the manifest. Optional `sha256` values pin the audio. Every model sees every row.

```sh
python3 tools/desktop-test/benchmark-voice.py \
  --manifest artifacts/desktop-search-voice/benchmark-manifest.json \
  --model "$HOME/.claude/claude-console/whisper/ggml-base.en.bin" \
  --model "$HOME/.claude/claude-console/whisper/ggml-large-v3-turbo-q5_0.bin" \
  --without-homebrew --output artifacts/desktop-search-voice/recheck
```

The English WER normalizer ignores punctuation/case and splits ASCII words. It does not equate
numerals with spelled numbers, and must not be used to score Tamil or other non-Latin text.

## Installed result

Installed 0.12.9 on 2026-09-19. LogiPluginService logged the new version loaded at 07:21:44
local time. The plugin itself downloaded the previously absent model on load; its promoted
file passed SHA-256 verification. The package is 3,169,315 bytes (3.02 MiB); weights are separate.
Temporary comparison model copies were removed after verification; the Turbo artifact path
links to the installed cache. The base.en model and signed recorder/Whisper runtime hashes
were preserved, as were all 28 keypad profile/application files and existing desktop settings.

Installation receipt: `artifacts/desktop-search-voice/installation.json`. Software report:
`artifacts/desktop-tests/20260919-072015-10994c/report.html`. This report identifies the tested
source and archive before adoption; installed fingerprints naturally change after installation.
The receipt proves that the installed DLL matches that run's source-build DLL. The current GUI
fixture result remains BLOCKED; no owner hardware observations were manufactured.
