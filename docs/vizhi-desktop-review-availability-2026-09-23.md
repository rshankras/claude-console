# View Changes depends on the current app task

## Owner evidence and diagnosis

The 0.17.14 keypad attempts at 07:32 and 08:33 recorded `panel-shortcut-unconfirmed`. The owner then selected the portfolio task and pressed Control–Shift–G on the Mac keyboard; nothing happened. The supplied screenshot shows the published portfolio in a browser tab and no visible Changes/Review control. The app sidebar says No projects, which by itself is not proof about any repository on disk.

The installed app's static command definition gates local Open Review on a ready Git context, and its Review tab menu is also conditional. The diff count is not the availability predicate. The app supports a No file changes yet review state. Therefore an absent review route cannot honestly be reported as a clean working tree.

The confirmed integration mistake was enabling the key from Codex mode alone and falling back to a default shortcut without evidence that the current task offered Review. The screenshot supports treating this context as unavailable to Vizhi; it does not establish the app's internal repository state or a general Codex defect.

## Version 0.17.15

- The native status scan reports Changes only for a unique enabled summary control or an existing unique Review header. It reuses the existing scan and adds no background process or polling loop.
- Review controls must belong to the unique app web area containing its mode control. Embedded browser areas cannot supply matching buttons for the app command.
- The Tools key is dimmed with Not available when no supported route is exposed. Known unavailable presses do not flash Opening or dispatch an action. A fresh status check handles a changed task before dispatch.
- The native opening operation still pins mode, foreground window and selected conversation, rejects dialogs/menus and ambiguity, rechecks the exact control before pressing, and requires positive panel confirmation.
- The default keyboard fallback is removed. Even legacy callers passing its old arguments cannot cause a shortcut dispatch from open-panel.
- Existing Review panels remain available when their diff is empty. Their app-owned Show files/Hide files marker is sufficient; generic text saying No changes is not.
- Legacy View Changes assignments use the same native guard and availability feedback.

Not available means Vizhi cannot currently see a supported Review route. A hidden control or unfamiliar app layout can also produce this conservative state; it does not mean the workspace is clean or that no Git repository exists. Opened means a panel was confirmed. Couldn't open is reserved for an attempted supported operation that did not complete or could not be confirmed.

The Screenshot-on-Home layout and both installed profiles remain unchanged. This fix does not add Git Review to a website/browser task or turn View Changes into a website preview action.

## Verification

Evidence is in `artifacts/desktop-review-availability/`.

The prior helper reproduced an unnecessary shortcut dispatch and panel-shortcut-unconfirmed with no review route. The new helper returns panel-not-available without clicks, keystrokes, draft edits or clipboard changes. Full repository suites passed: 2,118 C# tests passed and 13 skipped, with AX selector, shortcut, package, shell and harness checks passing.

All 26 native scenarios passed, including the targeted rerun. They cover unsupported tasks, matching buttons inside a browser preview, a real Changes control alongside those browser buttons, empty and already-open panels, formatted counts, disabled and ambiguous controls, missing acknowledgement, mode changes and dialogs. The first browser-decoy run correctly rejected the route and sent zero events; its separate clipboard verification was interrupted by loss of foreground focus. That failed run is retained, and a targeted rerun is recorded separately in the final report list.

The release packer verifies resources and excludes host-provided graphics/PluginApi assemblies. Packaged helper bytes are compared with all native test binaries. The unavailable key face was visually inspected at native size. Installation checks loaded version, runtime hashes and preservation of all profile/configuration contents; see installation.json after installation.

Real app/keypad acceptance remains separate: the portfolio task should show a dimmed Not available key. An app task exposing an actual Changes summary should still open its Review panel and leave an existing panel open on repeat taps.
