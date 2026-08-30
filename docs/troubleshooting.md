# Troubleshooting

The plugin's own log — handy for any of these — is at
`~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeConsole.log` on macOS and
`%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\ClaudeConsole.log` on Windows.

## Nothing appears on the keypad after installing

That is expected: the plugin is universal (2.2.0+) — it binds to no application and ships no layout. Import [the ready-made layout](../README.md#import-the-ready-made-layout), or add Terminal as an application in Options+ and drag the Claude Console actions on. The actions are listed under **Claude Console Actions** in the Options+ action panel; if that list is missing, the plugin did not load — check the log.

## Keys show only an exclamation mark / plain text right after building from source

If you've *both* installed the released `.lplug4` *and* run `dotnet build` (which writes a dev `.link`), the plugin is registered twice and the service refuses the duplicate — the plugin log shows `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` and the keys don't resolve. Keep **one** source: uninstall the packaged plugin in Logi Options+ to develop against the `.link`, or remove the dev `.link` (`scripts/uninstall.sh` does this) to run the installed package.

## The log says `Cannot load plugin … because plugin 'ClaudeConsole' is already loaded` at service start

On its own this is benign boot noise, not a failure: the service loads every sideloaded plugin twice at startup (once from its internal plugin record, once from the folder scan) and the second attempt logs this while refusing the duplicate — every sideloaded plugin on the machine shows the same pair, on macOS and Windows alike. It only signals a real problem when paired with a dev `.link` (see above).

## The session keys are stuck

Symptom: only one session ever shows, the state bar never changes, the Yes badge never lights — but everything still *works* when pressed.

A key you have opened in the **Logi Options+ icon editor** stops being live. The editor saves a `.ict` next to the profile — a *snapshot* of that key, a baked image plus the literal text that was on it at that moment ("Session 2") — and from then on the service draws the snapshot and never asks the plugin for an image. The giveaway is that pressing the key does the right thing (the plugin knows about the session) while the picture never changes; a profile created fresh renders live, because it has no `.ict` files. Restore live rendering with:

```bash
bash scripts/unfreeze-keys.sh          # lists what's frozen
bash scripts/unfreeze-keys.sh --apply  # backs them up to your Desktop, then removes them
```

Deliberate icon customizations on those keys are lost — that's the trade. **Avoid customizing the live keys** (the session slots, Yes/No, and the Model / Cost / Context / Activity displays); the static keys are safe to restyle.

## Windows: session/navigation keys beep, or Options+ says "Windows Terminal required"

Open Windows Terminal and start the agent there. For a classic Command Prompt or PowerShell console, set **Settings → System → For developers → Terminal** to **Windows Terminal**, then start a new session. Direct typing can still reach a supported unelevated console session, but tab navigation, session focus, Go to Project, and the Screenshot key require Windows Terminal. Elevated sessions cannot be controlled: Windows blocks an unelevated Options+ service from attaching across integrity levels.

With a Windows Terminal window open, a nav press from a classic console acts on *that* WT window — by design, not silently.

## Yes / No beep and do nothing

They only act on a permission prompt the plugin can see (a `PermissionRequest` hook payload). Two causes: live status is not turned on (press a live key — see [The live status bridge](../README.md#the-live-status-bridge)), or the session is waiting on something that is not a permission menu (a plain-text question, an idle prompt) — type into the session for those. This refusal is deliberate: guessing at a menu the plugin cannot see is how 2.0.1 approved the very thing the user pressed **No** on.

## Voice: the key says "Mic denied", "No speech" or "Model loading"

- **Mic denied** — the helper was refused the microphone. Allow **ClaudeVoiceHelper** in System Settings → Privacy & Security → Microphone, or reset and re-grant on the next press: `tccutil reset Microphone com.rshankar.claudeconsole.voicehelper`. A rebuilt or re-signed helper resets the grant (macOS ties it to the code signature); a stable Developer‑ID signature via `tools/voice/sign-and-notarize.sh` avoids this.
- **No speech** — it recorded but heard nothing usable.
- **Model loading** — the speech model is still downloading; try again shortly.

Until 2.2.0 these failed *silently* (nothing typed, no beep, no prompt), which is why the key now says so.

## Before 2.2.0

- **The Claude Console icon vanished from Options+ after a reinstall, or never appeared after a first sideloaded install.** Those versions registered their own application entry in Options+, and the service could lose it (a reinstall dropped it from memory, a sideloaded install never created it). 2.2.0 removed the entry altogether, so there is nothing to lose; the layout lives on Terminal's own entry, which is yours. If you are on an older version, update — or restart the Logi Plugin Service (`killall LogiPluginService`) and then Options+ so it re-reads the registration from disk.
- **Keys show only an exclamation mark or plain text, then the whole plugin disappears (you drop to the default profile), and a Mac restart brings it back.** A thread leak, **fixed in 1.3.1**: the live‑status poller could accumulate threads until the *Logi Plugin Service* hit the OS thread limit and crashed. Update.
