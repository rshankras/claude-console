# Clean-room fresh-install test (MX Creative Keypad)

A repeatable check that a brand-new user can install the plugin, import the ready-made
layout, and get a working keypad — with **zero prior config**. Run this in a **separate
macOS user account** (or on another Mac) before each release.

> Your everyday account is **not** a valid test bed: it already has the plugin loaded and
> your own key assignments, and the shipped `.lp5` shares a profile GUID with your existing
> Terminal profile, so importing it there would collide.
>
> Since 2.2.0 the plugin is **universal**: it registers no application and ships no layout. So the
> correct result of step 2 is a keypad showing *nothing new* and an Options+ strip with *no* Claude
> Console icon — the actions appear only in the action panel until step 3 gives them keys.

## What you need
- `ClaudeConsole_<ver>.lplug4` — from [Releases](https://github.com/rshankras/claude-console/releases) (or a `dotnet build`).
- `ClaudeConsole-Keypad.lp5` — from the Release assets, or [`profiles/`](../profiles/) in this repo.

## Steps

### 0. Enter a clean account
- Create a Standard account (System Settings → Users & Groups → Add Account), e.g. `cctest`.
- **Log out** of your main account and **log in** as the test user — a full logout, not
  fast-user-switch (two sessions contend for the keypad).

### 1. Connect the keypad
- Launch **Logi Options+** and confirm the **MX Creative Keypad** is detected. Bolt-receiver
  pairing carries across accounts; Bluetooth usually does too — re-pair if it doesn't appear.

### 2. Install the plugin
- Double-click `ClaudeConsole_<ver>.lplug4`. If Gatekeeper blocks it: right-click → **Open**,
  or `xattr -dr com.apple.quarantine <file>`.
- Confirm a **clean load** (not crash-disabled):
  ```sh
  tail -5 "$HOME/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/ClaudeConsole.log"
  # want:     Plugin 'ClaudeConsole' version '<ver>' loaded ... in N ms
  # red flag: disabled as it had crashed before
  ```
- Confirm it's a **package** install, not a dev `.link`:
  ```sh
  ls "$HOME/Library/Application Support/Logi/LogiPluginService/Plugins/"   # expect a ClaudeConsole* folder
  ```

### 3. Import the layout
- Logi Options+ → MX Creative Keypad → profile menu (the `⋯` / profile dropdown) →
  **Import Profile** → choose `ClaudeConsole-Keypad.lp5`.
- It imports as a **Terminal** profile with the keys populated. Terminal appears in the
  application strip (it is Terminal's entry, not the plugin's) and the layout shows when
  Terminal.app is frontmost.

### 4. Grant permissions (fresh per account)
- **Accessibility** → Privacy & Security → Accessibility → enable **Logi Plugin Service**.
- **Microphone** → press the **Voice** key once and grant the helper when prompted.

### 5. Verify
- With **Terminal.app** frontmost, the Claude Console layout shows across pages.
- Run `claude`, press a prompt key (e.g. *Fix Bug*) → it types the prompt and submits.
- **Voice** key → *Tink* → speak → press again → transcribed into the terminal.
- **Answer** keys (Up/Down/Return, Yes/No) drive a Claude prompt.
- **Live status (opt-in, #31):** the Cost / Context / Activity keys read **Set up**, and that
  account's `~/.claude/settings.json` is **unchanged** by the install (check it, or note that it
  does not exist). Press Cost once: it flashes *Press again* and a card appears in Options+ naming the exact
  change a second press makes; the file is still unchanged. Wait 20 s — the flash clears and the
  file is still unchanged (a late press never writes). Press Cost twice within 15 s: a card confirms
  the edit, the keys read **Restart Claude**, and `~/.claude/settings.json` now holds five hooks +
  a `statusLine` (backup at `~/.claude/settings.json.claude-console.bak`). (**Enable Live Status**,
  page 2 key 9, does the same in one press.)
  Start a fresh `claude` and confirm the Model / Cost / Context / Activity keys show live data.
  Press **Disable Live Status** (page 4, key 9): the keys read **Off** and the file carries none
  of the plugin's entries.

### 6. Cleanup
- Log back into your main account; optionally delete the test account.
- ⚠️ **Don't** import the `.lp5` into your main account — the profile GUID collides with your
  existing Terminal profile.

## Pass criteria
- ✅ Package installs and **loads clean**, with **no** application entry and **nothing** on the keypad
- ✅ **Import** produces the full layout on the keypad
- ✅ prompt / voice / answer keys fire with Terminal focused
- ✅ install leaves `~/.claude/settings.json` untouched; one press on a live key, or a late second press, changes nothing
- ✅ two presses within 15 s (or Enable Live Status) wire it and say so; live keys show data on the next `claude` session; Disable takes it out
