# Vizhi Desktop — the Windows UI Automation helper

`vizhi-desktop-uia.exe` is the Windows half of the desktop seam: what `tools/desktop/VizhiAxBridge`
is on macOS. One short-lived process per verb, one line of JSON on stdout, the same snapshot
contract on both operating systems. `Program.cs` holds the verbs and the UIA plumbing;
`UiaMatching.cs` holds the matching rules and has no UIA in it, so the test suite links it and
holds it to the macOS helper's answers (`tests/UiaMatchingTests.cs`).

Built like every other Windows helper since #83: self-contained, trimmed, single file, UI
Automation through its COM interface (`UiaInterop.cs`) rather than the WPF wrapper. About 12 MB.

## What the reconnaissance established (2026-09-30, on the live app)

- The Windows app is the Store package **OpenAI.Codex** (family `OpenAI.Codex_2p2nqsd0c76g0`,
  display name "ChatGPT"), executable `app\ChatGPT.exe`, Chromium 153 under OpenAI's "owl"
  runtime. One package carries both surfaces, like the Mac bundle. The adapter names the process
  `ChatGPT`; several processes share the name and one owns the window.
- **Every label the macOS adapter uses reads the same on Windows**: `Allow once`, `Deny`, `Stop`,
  `Switch mode, current mode: …`, `Pin chat`, `New chat`, `Search`, `Add files and more`,
  `Change permissions`, `Start new voice chat`, `Work with ChatGPT`, the status containers.
- A popup button (`Switch mode`, `Add files and more`, `Change permissions`) exposes
  ExpandCollapse, not Invoke; a pressed-state button exposes Toggle. The helper treats all of
  Invoke, Toggle, ExpandCollapse and SelectionItem as "pressable" and presses through whichever
  the control offers.
- **Chromium serves no page content while the window is not being shown** — screen locked,
  and to be confirmed for minimised and fully covered. The document element stays with nothing
  under it. `status` reports `surface:false` for that, never an empty page; the keys read Hidden.
- A cache request scoped to the subtree fetches the whole tree in one cross-process call:
  ~320 ms for 365 nodes, against ~2 s walking it property by property.

## Running it by hand

Open the ChatGPT app, then:

```powershell
.\vizhi-desktop-uia.exe inspect                                   # every visible window
.\vizhi-desktop-uia.exe inspect --process ChatGPT --require-process         # controls only
.\vizhi-desktop-uia.exe inspect --process ChatGPT --require-process --all   # every node
.\vizhi-desktop-uia.exe frontmost --process ChatGPT
.\vizhi-desktop-uia.exe status --require-process --process ChatGPT `
  --approve "Allow once" --deny "Deny" --stop "Stop" `
  --attention "needs attention" --mode-prefix "Switch mode, current mode: " `
  --conv-marker "Pin chat" --state-awaiting "Awaiting approval" --state-unread "Unread" --state-running "Working"
.\vizhi-desktop-uia.exe press --require-process --process ChatGPT --label "New chat" --dry
```

`--dry` reports what a press would hit without pressing. `--all` includes message text; inspect
the file before sharing it if the open conversation is sensitive.

## Still to do before Windows packaging

1. Live checks of every verb, with the app in front, minimised, covered and on another virtual
   desktop; `write` in particular (a Chromium composer may refuse a direct value write).
2. The verbs the macOS helper has and this one does not yet: `draft-target`, `append-target`,
   `append`, `send`, `search`, `open-panel`, `copy-reply`, `voice`, `attach-image`,
   `attach-files`, `context-*`. Until each lands, the matching key reads unavailable.
3. `SHIPS_WINDOWS=1` for VizhiDesktop in `tools/voice/pack-release.sh`, this helper in
   `tools/windows/build-windows-payload.sh`, the signing list, `pluginFolderWin: bin` in the
   package metadata, and a Windows whisper bundle with its `TRANSCRIPTION_SMOKE_OK` marker.
