# Spoken workflow presentation — 2026-09-22

Version **0.17.9**, branch `integrate/vizhi-desktop-main`.

## Problem and plan

The owner could not recognize their spoken Debug brief because it was embedded halfway
through a long instruction. The transcript was present; its presentation made it look missing.
Keep the same keypad interaction, move the recognized request to the beginning, separate the
task instruction with a blank line and label, and shorten the instruction. Debug should first
investigate whether a bug exists, with fixes conditional on finding one.

Apply the format consistently to the eight stock spoken recipes: Debug, Refactor, Fix CI,
Rewrite, Draft Reply, Compare, Research, and optional Review PR. Preserve custom recipes.

## Result

```text
Your request:
I just would like to check whether is there any bug in this.

Debug task:
Investigate using the current workspace and supplied context. If you find a bug, reproduce it, explain the cause, make the smallest fix, and verify it with an appropriate test. If no bug is found, say so. Ask if the target is unclear.
```

The recognized words, punctuation and internal line breaks remain intact. The existing
delivery path trims surrounding whitespace and substitutes the transcript once. It does not
correct the owner's grammar, add punctuation to their sentence, or expand placeholders found
inside the transcript. Existing composer material and attachments remain ahead of the appended
workflow draft; the request comes first within that new draft.

Tap the workflow, speak, tap to finish, review the draft, then press Send or Send Draft.
Nothing sends merely because transcription has finished. Recovery and retry behavior remain
the same. Keypad pages, positions, icons and command bindings are unchanged.

## Saved settings

`DesktopSpokenWorkflowMigration` recognizes the frozen 0.17.8 stock voice recipes by their
known fields. It updates only the Prompt property, preserving casing, extra metadata and slot
order. A changed label, icon, scope, input, submission setting, source prompt or prompt text
keeps the recipe untouched. Duplicate IDs are skipped. Partial/reordered lists and the optional
Review PR recipe are supported.

Before writing atomically, preserve the original settings in a `.before-0.17.9` sidecar.
Repeated loads do not overwrite that backup or rewrite already-upgraded settings. Existing
earlier-version migrations remain in place; fresh installations seed the new defaults.

## Verification

Unit coverage exercises each spoken recipe through start, finish, transcript delivery and
explicit Send, including multiline Unicode and a literal `{brief}` inside the request.
Migration checks cover casing, unknown metadata, custom fields, partial/reordered lists,
duplicate IDs, backups, idempotence and settings changed since loading.

The isolated Chromium fixtures insert all eight example recipes in their respective modes,
verify the complete request and line breaks, prevent duplicate retries, and wait for explicit
Send. They use synthetic transcripts and a named clipboard; microphone and physical-keypad
acceptance remain separate. Source/example consistency and release evidence are retained under
`artifacts/desktop-spoken-prompts/`.

The first migration test caught that an older upgrade reformatted the settings before the
new backup ran. The spoken migration now runs first so that backup preserves the original.
During native-fixture development, a harness variable collision was corrected and one run
refused focus before insertion. A later raw contenteditable run expanded consecutive newlines
through Chromium's default `execCommand` paste behavior and was correctly refused by the
unchanged helper. The spoken fixture uses an explicit literal-text paste handler to verify
the intended text contract; this is a controlled editor model, not proof of the live app's
rich-text representation. Those earlier runs remain in the artifacts for comparison.
