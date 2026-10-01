# Vizhi Desktop: issues 138–147 and 149

Implementation and verification, 1 October 2026. Based on `origin/main` at
`4c25b8d`, in branch `fix/vizhi-desktop-138-149`. PR #148 is excluded from the
issue scope; its merged Windows implementation is part of the baseline.

## Plan and implementation

1. **Profile lifecycle (#138).** Respect the installed revision marker when the
   user deletes its profile. Registration defaults to no automatic service
   restart, covering both initial registration and updates.
2. **Workflow migrations (#149).** Flow migration recognizes stock scope/source
   fields as well as prompts. Cache by file contents, invalidate on edits, and
   verify that both cached loads and a fresh uncached load leave file timestamps
   unchanged. Custom scope/source values and existing backups survive.
3. **Runtime ownership (#141).** Desktop settings, helpers, voice model, captures
   and IPC use its own Application Support / Local AppData directory. Copy legacy
   Desktop settings once, preserve originals, and never replace existing settings.
   Screenshot helpers and Tools workflow loading use the same destination.
4. **Model startup (#142).** Copy a hash-verified model from another product when
   possible, with verification of the copied bytes. Otherwise download on first
   voice use, show a persistent 148 MB download face, and surface failure. Cancel
   an active download before uninstall cleanup.
5. **Uninstall (#143).** Stop an active capture before removing runtime, private
   IPC, legacy temporary IPC, and explicitly owned registration. Keep user settings
   in a small sibling backup. Preserve registration/profile data in a private
   receipt for the Options+ replacement callback sequence; restore it only within
   ten minutes. This also preserves an intentionally deleted stock profile.
6. **Voice recovery (#139).** Failed sends retain a draft and attempt a clipboard
   copy; retry only inserts. Pending drafts persist privately across reloads, with
   recoverable instructions for a missing app or Accessibility permission. Raise
   capture to 180 seconds, signal completion, protect completed transcripts from
   stale-state restart, and ignore an old capture watcher's completion. Helpers
   delete raw audio after transcription. The Mac helper preserves only a small
   signal/silence result for Find Chat's silence gate.
7. **Voice packaging (#140).** Build the helper from source for each package,
   apply product-specific bundle identity, display name, microphone explanation,
   and version, then sign/notarize it. Verify those values in the final package.
   The whisper dependency remains a separately prepared, smoke-tested payload.
8. **Mac search (#144, #147).** Support the observed pressable `AXStaticText`
   options beneath the command menu's Chats group, as well as existing URL links.
   Join the title fragments in the first group; exclude commands and duplicate
   result identities. Send the result-group adapter setting to the Mac helper.
9. **Mac conversations (#145).** Use one sidebar scanner for status, navigation,
   Copy Reply and draft identity. Prefer the Recents representation of a nested
   project chat, retaining project-only chats, duplicate Recents, differing
   explicit identities, and conflicting selected states.
10. **Changes and Voice Chat (#146, #147).** Prefer a unique summary opener;
    otherwise use the latest turn's View changes button. Keep duplicate summaries
    ambiguous. Voice availability and button selection share the same ordered
    label preference, and duplicate buttons under one label remain ambiguous.

## Verification

- `bash tests/run-all.sh`: passed, including C#, shell contracts, Swift matching,
  AX package staging, signing gates, and concurrent input-lock tests. At that run:
  **2,410 C# passed, 29 skipped** (platform-dependent Windows cases).
- Final complete C# rerun after the capture-watcher change: **2,411 passed, 29 skipped**.
- Final capture-watcher regression group: **39 passed**, including the additional
  test that an earlier capture cannot change a later capture's face.
- Native AppKit fixture: **37 checks passed**, covering real AX traversal,
  selection, draft preservation, guarded sends, search, shortcuts, and window
  targeting. The fixture uses its own app and pasteboard.
- Live Mac Find Chat: the original helper returned no results for `notes`; the
  patched helper returned **nine results**. Opening, writing the query, and reading
  results succeeded in both ChatGPT and Codex modes. ChatGPT mode was restored.
- Desktop plugin, Windows voice helper, and Windows UIA helper compiled. Plugin
  builds used `SkipPluginLink=true`, leaving the installed plugin untouched.
- Both Swift helpers compiled. A fresh, ad-hoc-signed Vizhi Desktop voice app was
  built outside the runtime directory; its identity, prompt and version matched.
- Metadata regression tests cover all three products and reject stale identity,
  microphone text and version. Shell scripts pass syntax checks.
- `git diff --check`: passed.

## Remaining release checks

No signed release package was produced, no installed plugin was replaced, and no
issues were closed. Apple notarization and fresh/upgraded microphone-consent
behavior require the release package. Windows live behavior and physical keypad
feedback still need a device pass.

Project/Recents deduplication relies on the app's nesting/title convention when AX
exposes no stable identity. Explicit different identities or selection states
remain separate. Verify a real project chat and intentionally equal chat titles
on the Mac before release. Multi-turn Changes and dual-start Voice Chat have
regression fixtures; their full live Mac/keypad acceptance remains to be run.

Uninstall intentionally preserves user settings. A registration receipt can remain
on disk after a real uninstall; an expired receipt is discarded on the next
install/load and is never restored. It contains profile data, not audio or pending
dictation. The public vizhi.dev FAQ has not been edited or published; the product
README documents the new paths and cleanup behavior.

The Windows recovery clipboard owns a temporary window before publishing data,
following the [Win32 clipboard ownership contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata).
