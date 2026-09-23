# Key-face previews (macOS)

Render the built plugin's real key drawing functions at the keypad's 90-pixel size with synthetic
conversation names and representative states. This does not instantiate the plugin or access
ChatGPT, microphone, clipboard, live profiles, or hardware.

```sh
dotnet build src/Products/VizhiDesktop -c Release -p:SkipPluginLink=true
dotnet run --project tools/desktop-test/preview -- \
  bin/VizhiDesktop/Release/bin/VizhiDesktopPlugin.dll artifacts/desktop-key-preview
```

`pages.json` groups the PNG files into review pages. They are plugin renders, not hardware
screenshots. The SDK owns the All Chats opener's caption, Back key, layout padding and pagination;
those require host/hardware acceptance. Conversation entry images use the same rendering as Home.
The graphics dependency belongs only to this preview tool and must not be packaged in the plugin.

The preview includes the long-title regression from Dashboard.jpg (with a synthetic completion of
the first truncated title), proposed short labels, and all active conversation states. It can also
render the previous 0.12.4 DLL for a before/after comparison; the renderer entry point is selected
from the assembly being inspected. Only display code is called in either version.
