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
- **Chromium serves no page content while the screen is locked.** The document element stays
  with nothing under it. `status` reports `surface:false` for that, never an empty page; the keys
  read Hidden. A minimised window and one fully covered by a maximised window both keep serving
  the whole page (device pass, 2026-09-30: the same status 2 s and 12 s after minimising), and a
  press on the minimised window lands — it takes the foreground without restoring the window,
  and `restore-front` hands it back.
- A cache request scoped to the subtree fetches the whole tree in one cross-process call:
  ~320 ms for 365 nodes, against ~2 s walking it property by property.
- **A UIA press activates the app's window.** Chromium performs Invoke as a click and a click
  focuses; the Mac's AXPress does not. Verified with the terminal in front: after `press`,
  ChatGPT is in front. The process that made the UIA call is then refused
  `SetForegroundWindow`, while a fresh process is accepted at once — so `press` reports
  `frontMoved` and `frontBeforeHwnd`, and the client runs `restore-front` as a second
  invocation (~150 ms). The conversation key skips that, since it shows the chat on purpose.
- The composer accepts a direct value write (`write` → ValuePattern.SetValue, ~600 ms), after
  which Send appears and `canSend` reads true.

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

## Every macOS verb has its Windows counterpart

`Program.cs` (target, scan, status, press, restore-front, voice, open-panel, focus),
`ComposerVerbs.cs` (draft-target, append-target, write, append, send, attach-image,
attach-files), `ContextVerbs.cs` (copy-reply, context-clipboard / selection / window /
screenshot / return / paste) and `SearchVerbs.cs` (search). Checked live on 2026-09-30:
status, presses, the focus hand-back, write, append, send's refusals, Copy Reply, the clipboard
read, the window-behind capture, and the whole Find Chat flow (open, type, results, select).

What the Windows app does differently, and how the port answers it:

- The composer is written through the value pattern, which replaces the whole value. `write`
  goes only into an empty composer; `append` writes original + separator + text and proves by
  fingerprint that the original survived.
- Footer buttons are each wrapped in their own group, so Copy is tied to its response actions
  by the buttons' bounding boxes (the macOS geometry fallback, now the usual route).
- Search results are list items with no URL: a result's id is its title's fingerprint, and the
  key shows the item's first text — the chat title — not the accessible name that runs title,
  project, shortcut and snippet together.
- Attachments go in by a verified paste: file list on the clipboard, composer focused through
  UIA and proven focused, one Ctrl+V, attachment confirmed by name, clipboard given back.
- The region screenshot is the shared toolkit's snip (`claude-console-tools.exe shot`), which
  ships beside this helper; the window-behind capture is `PrintWindow`.

## Not yet checked live (the device pass)

- A real approval card: Approve and Deny from another app, with the focus hand-back running
  from LogiPluginService rather than a terminal.
- `voice` (starts a real voice chat), `send` (sends a real message), `attach-image` and
  `attach-files` (need a real file to attach), `context-selection` (copies from another app),
  `context-paste`, `context-screenshot` (the snip overlay).
- `open-panel` in Codex mode with a review available.
- The tree when the window is on another virtual desktop.
- Options+ binding to a Store app, and the Vizhi Home layout importing on Windows.

## Packaging

`SHIPS_WINDOWS=1` for VizhiDesktop; `build-windows-payload.sh` stages this helper and the
toolkit (no hook); `verify-package.sh` refuses a helper nothing launches; the package declares
`pluginFolderWin: bin`. Packing and signing run on the Mac.
