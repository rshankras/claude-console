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

Uninstall intentionally preserves user settings. The public vizhi.dev FAQ has not
been edited or published; the product README documents the new paths and cleanup
behavior.

The Windows recovery clipboard owns a temporary window before publishing data,
following the [Win32 clipboard ownership contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata).

## Review follow-up, 1 October

A review of the first cut (fifteen findings, four of them release blockers) led to
these changes. Suite after the changes: 2,432 C# passed, 29 skipped; every shell,
metadata and AX stage green; all three products build; both helpers compile.

Blockers:

1. **`Load()` aborted on a failed receipt restore**, leaving every key dead until the
   receipt expired. Restore is now `TryRestoreRegistration`: best effort, never throws,
   and a receipt that fails to restore is discarded so the failure is not repeated.
2. **Every upgrade deleted the pending dictation**, because Options+ runs
   `Uninstall()` on replacement. `Clean` now parks the dictation beside the
   registration receipt; a reinstall within ten minutes takes it back
   (`RestoreRuntimeCache`, run in the plugin constructor before the draft file is
   read). The speech model is deliberately not parked: keeping it alive past an
   uninstall would need a timer process the plugin cannot own once it is gone, and
   a lingering process after an uninstall is its own finding. After an upgrade the
   first voice press copies the model from a sibling product or downloads it again,
   with the download face showing. Decided with the owner on 1 October against the
   SDK contract (the service cannot tell an upgrade from an uninstall).
   Same decision: the runtime home is now the SDK's own plugin data directory
   (`Plugin.GetPluginDataDirectory()`, `PluginData/VizhiDesktop` under the service
   tree), as the Actions SDK documents and Logitech's plugins practise; the product
   hands it to `ProductRuntime.SdkHome` at construction.
3. **The 180 s recording cap kept a 20 s transcript wait.** The wait is now sized from
   the recording (`TranscriptWaitSeconds`: 20 s plus half the recorded seconds, 110 s
   at the cap), `StaleAfter` is derived from cap plus wait, and the uninstall wait
   uses the same budget.
4. **`conversationRows` skipped a matched row's subtree**, so a chat nested under a
   pressable project row was unfindable. A row now has to OWN its marker (outside
   nested rows' subtrees); containers are never rows, their chats are visited, and
   the nesting rule covers both a pressable project row and a second list.

Also fixed:

5. A leftover transcript was adopted as a draft whatever intent produced it. The
   capture's intent is written to a `.intent` sidecar at start; only `Desktop` /
   `DesktopDraft` leftovers are retained, anything else is discarded as before.
6. Dictate & Send silently became a draft-insert key. Its face now shows `Insert
   Draft` with the retry hint while a draft is pending, and it has the same
   hold-to-discard gesture as Dictate.
7. The voice helper deleted the WAV but wrote `.signal` only when it could judge the
   audio. It now always writes `signal`, `silent` or `unknown`, and the plugin's
   `SearchSignalVerdict` yields no verdict (transcript proceeds) when neither the
   sidecar nor the WAV can decide.
8. A sideloaded registration was written but never adopted, with no notice.
   `RegisterIfMissing` returns true only when it scheduled a restart; otherwise the
   message centre says the layout appears after the next service start
   (`BridgeNotice.LayoutAwaitsServiceRestart`), which also needed `Notify` wired in
   the Desktop plugin — it never was, so the model-download notices were dropped too.
9. `DesktopDraftRecovery.Save` empties the file when a delete fails, so a consumed
   draft cannot come back after a reload.
10. The stale-capture disk probe runs before `VoiceCaptureState` takes its lock.
11. The content-keyed workflow cache is gone; the migration fix is the #149 fix.
12. A download cancelled by uninstall no longer reports a failure.
13. The Windows voice helper deletes the stop flag and the WAV in separate `try`s.
14. `tools/voice/build.sh` installs into the product's runtime home (`VOICE_PRODUCT`)
    and `bundle-whisper.sh` follows it (`VOICE_RUNTIME_HOME`).
15. `pack-release.sh` checks the whisper bundles before the notarization round-trip.

Still owed: the Windows deferred-cleanup command and the Windows helper changes are
compiled but not run on hardware; the live checks listed above are unchanged.

## Device-pass finding, 2 October: #151

The Mac pass of the first 1.1.0 package (`c4f0a3ef…`) opened a Find Chat result and
the keypad stayed on the result cards; the next Find Chat press showed the same page
with a dead *Open Search* key. The service log had the cause: on the result press the
command asked the host to run `$@Generic___@DynamicFolderGoUp`, which the host answered
with `Unknown command '@DynamicFolderGoUp'`. The folder never closed, so `Deactivate`
never ran, the search session stayed cleared (a successful `Select` sets it to null),
and `TypeQuery` returned at its `_session == null` guard. Re-entering the folder created
the layout mode again without calling `Activate`.

Fix: `DesktopSearch` raises `Selected` after a successful selection;
`FindChatDynamicFolder` subscribes in `Activate`, calls its own `Close()` on it (the
only call that leaves the page), and unsubscribes in `Deactivate`. `DesktopSearchCommand`
no longer drives the generic action (the `close` callback stays for the test rig).
`TypeQuery` with no session re-opens the search in the mode it was pinned to instead
of returning. The Mac log now records `FindChat: open in <mode> — …` and
`FindChat: result opened` / `select refused — <error>` (outcomes only, never titles),
which the Windows helper already did. Three tests pin the event, the re-open and the
no-op callback. Suite: 2,437 passed, 29 skipped.

## Device-pass finding, 2 October: #147 second press

Step 5.2 pressed Find Chat while the search box was already open; the folder closed
three times with "the app's mode is unreadable" (11:53:15–18) and the keypad bounced
back to Tools with no face. The box hides the mode switcher, so neither the plugin's
status read nor the helper's `open` could name the mode — #147 had recorded this as
"press Esc first", and the pass doc's expectation was never implemented.

Fix: `FindChatDynamicFolder.Open` no longer closes on an unreadable mode; it opens with
the mode the last search was pinned to (ChatGPT before any) and lets the helper decide.
Both helpers treat an `open` that finds the field already present as pinned
(`checkSearchMode(…, pinned: searchSurface(nodes) != nil)` on the Mac, the same clause
in `SearchVerbs.CheckedSearch` on Windows), so a hidden switcher is excused only when
the box is open; a visible, different mode is still `mode-changed`, and a missing
switcher with no box stays `mode-unavailable` on the retry page instead of a silent
bounce. Test `An_unreadable_mode_opens_with_the_last_pinned_mode` replaces
`An_unreadable_mode_opens_no_search`. The Windows change is compiled, not run.

## Device-pass finding, 2 October: #147 Voice Chat never shows End Voice

Step 5.5: the key read *Voice Chat*, the press started a session in the app, and the
key stayed on *Voice Chat* — there was nothing to end with (12:17). With the session
open the app has two windows: the standard window (sidebar now carrying a "New voice
chat" button and a "Voice chat active" image, composer still offering "Start voice
chat") and an `AXDialog` holding "Mute microphone", **"Stop voice chat"**, "Mute voice
chat", "Hide activity". `targetWindows()` resolves to the focused or main window, so
the dialog was never scanned, `voiceState` saw a start button and no end button, and
reported Ready through the whole session; a second press would have started another.

Fix: `scanWindows` is now `walkTree(operationWindows)`, and `voiceDialogNodes()` walks
the app's other `AXDialog` windows. Only `status`'s `voiceChat` and the `voice` verb
add those nodes; every other verb keeps the single-window contract. The adapter's
labels were already right ("Stop voice chat"). Not unit-testable offline (AX walk);
verified on the device in the next cut. Windows untouched: the UIA helper scans the
process, and the Windows pass had already seen start and end.
