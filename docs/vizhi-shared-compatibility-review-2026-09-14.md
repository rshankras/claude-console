# Vizhi shared-code compatibility review — 2026-09-14

Reviewed integration HEAD `82d0aea` against local main `b7f4d99`. This is a source and automated-test review, not fresh Claude Console physical-device or Windows acceptance. No implementation or live configuration was changed.

## Findings and required follow-up

1. **Medium — shared voice helper can be replaced by alternating products.** `src/Core/BridgeManager.cs:73` uses the same persistent ClaudeVoiceHelper.app path for both products. `EnsureVoiceRuntimeInstalled` (around line 1235) compares the complete packaged helper tree and replaces the installed bundle on a mismatch. This branch changes `tools/voice/Info.plist` relative to main, so packages built from the two trees can repeatedly replace that shared helper when users alternate voice between products. The code path is confirmed; an actual dual-install failure or permissions regression was not reproduced. Keep the large speech model shared, but establish compatible/versioned helper ownership and test alternating product use. This is an existing shared-runtime design exposed by different payloads, not a change to voice-capture logic.

2. **Medium — Claude approval clearing lacks a behavioral regression counterpart.** `src/Core/SessionRegistry.cs:147` changes shared ClearPendingApproval behavior to distinguish separate pending files from approvals embedded in state. The new executable test exercises Codex; Claude answer tests largely check the decision function and source-text ordering for clearing. Add Claude fixtures covering successful Yes/No, failed injection, clear then refresh, next approval, and a pending file disappearing while the in-memory snapshot still contains an approval. No Claude failure was reproduced; this is a release-confidence gap in a changed sensitive path.

3. **Low — Context help text leaks Codex behavior into Claude.** `src/Core/Actions/ContextCommand.cs:28` now advertises holding to compact in Codex for both products and removes Claude's live-status setup/disable guidance. Actual Claude long-press behavior still uses LiveStatusGate. Make the description product-aware and preserve Claude's tested interaction.

## Other compatibility changes to acknowledge

- `PromptCommand` shares ~/.claude/claude-console/prompts.json. Loading Vizhi upgrades an exact unedited previous seed from Review to Code Audit, affecting what Claude sees on its next load. User-edited seeds are preserved by existing tests. Treat shared migration as an intentional cross-product change, not a Vizhi-only rename; add a two-product load/migration regression before changing this further.
- Git Status's shared action display name changes from Status to Git Status; its action identifier and prompt are unchanged.
- Approval comments still describe missing Windows Codex approval observation even though the current adapter advertises ApprovalSignal=true on both platforms. Correct these comments.
- ProjectDiscovery and VoiceFailure are identical to main. An explicit nonempty roots list suppresses automatic discovery for both products. The local demo-only roots file explains AlertWala's absence; do not ship personal paths.

## Behaviors preserved by inspection and existing passing tests

- Claude adapter remains unchanged: /context, existing controls, SettingsFileWiring and observed approvals remain enabled.
- Claude's live-status short/long-press gate remains in force; Codex's context-compaction callback only applies without settings wiring.
- Claude's interrupt recovery retains its existing transcript-growth veto; Codex opts into its different interrupt semantics.
- Approval injection now resolves the target once and clears only after successful delivery. The unobserved-approval fallback is not enabled by either current CLI adapter.
- Session pinning, project discovery, core voice capture, screenshot delivery, and Windows focus/injection logic were not replaced with Codex implementations. macOS launch quoting is improved in shared code.
- Product icon selection and IPC roots are set per product assembly; Claude retains the default icon folder and orange selected-session bar. The shared Blue constant changed, but the old selection-corner drawing helper has no callers, so no actual Claude selection-color regression was established.

## Validation

- `dotnet test tests/ClaudeConsolePlugin.Tests.csproj --no-restore --verbosity quiet`: 1,092 passed, 0 failed, 11 skipped, 1,103 total.
- `dotnet build src/Products/ClaudeConsole/ClaudeConsolePlugin.csproj -c Release -p:SkipPluginLink=true --no-restore --verbosity quiet`: passed, 0 warnings/errors.
- Logs: /tmp/vizhi-shared-review-tests.log and /tmp/vizhi-review-claude-build.log.
- Existing tests compile shared Core and both agent implementations, but do not execute actual product Plugin classes in a Logi host. Source assertions and pure policy tests do not prove rendering or two-product runtime coexistence.

## Recommendation

Reuse the extensive Claude Console suite and historical QA evidence. Add targeted compatibility tests and repeat affected device workflows (approval, Context setup/hold, prompts, and alternating voice installations), rather than restarting all Claude QA. Windows acceptance remains pending on the user's Windows machine. Keep deferred issue #94 architecture work out of this change.
