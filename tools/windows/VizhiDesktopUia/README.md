# Vizhi Desktop Windows reconnaissance

This helper is the Windows UI Automation backend and the W0 discovery tool. Run these commands
on the Windows machine that has both ChatGPT and Logi Options+ installed.

Build or copy `vizhi-desktop-uia.exe`, open the ChatGPT desktop app, and run:

```powershell
.\vizhi-desktop-uia.exe inspect > vizhi-windows.json
```

The output lists each visible top-level window with its exact `title`, `process`, PID, and window
class. Use the ChatGPT row's exact title for the bounded UI tree capture:

```powershell
.\vizhi-desktop-uia.exe inspect --window "ChatGPT" > vizhi-chatgpt-tree.json
```

Before enabling Windows packaging, verify that the tree contains the visible controls and run the
read-only status contract:

```powershell
.\vizhi-desktop-uia.exe status --process "<process from inspect>" `
  --approve "Allow once" --deny "Deny" --stop "Stop" `
  --attention "needs attention" --mode-prefix "Switch mode, current mode: " `
  --conv-marker "Pin chat" --state-awaiting "Awaiting approval" --state-unread "Unread"
```

Send back `vizhi-windows.json` and `vizhi-chatgpt-tree.json`. They contain accessibility labels and
process/window metadata, not conversation message bodies unless those are exposed as UIA text;
inspect the files before sharing if the open conversation is sensitive.

Release gating after capture:

1. Put the confirmed executable name in `OpenAiDesktopAdapter.WindowsProcessNames` and
   `VizhiDesktopApplication.GetProcessName()`.
2. Validate Status, Approve/Deny guard, Stop, composer write, mode switching, conversations, and
   focus on the live Windows app. Test focus with ChatGPT minimized, covered by another app, and
   on another virtual desktop.
3. Prepare a Windows whisper.cpp directory containing `whisper-cli.exe`, its required DLLs, and a
   `TRANSCRIPTION_SMOKE_OK` marker created only after a real WAV transcription succeeds on Windows.
   Pass it to the packer as `WINDOWS_WHISPER_DIR`.
4. Add `VizhiDesktop` to the `SHIPS_WINDOWS=1` case in `tools/voice/pack-release.sh` and add
   `pluginFolderWin: bin` to the package metadata only after all checks pass. The payload builder
   will then include both `vizhi-desktop-uia.exe` and `claude-console-voice.exe`; the packer will
   refuse a release that lacks the Windows whisper runtime.
