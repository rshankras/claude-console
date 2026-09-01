namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Models;

    /// <summary>
    /// Context key (group "Core") — a gauge icon with live context-window capacity. Claude shows
    /// used context; Codex shows the more actionable amount left. A short press opens the agent's
    /// native context/status breakdown; on Codex, a long press compacts the conversation.
    /// </summary>
    public class ContextCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private readonly LiveStatusGate _gate;
        private Int32 _percent;
        private Int32 _usedTokens;
        private Int32 _maxTokens;

        // "33%" — plus "325k/1M" once the window size is known.
        private Boolean _hasData;

        private Boolean ShowsRemaining => _bridge.Agent.Id == "codex-cli";

        public ContextCommand()
            : base(displayName: "Context", description: "Live context-window capacity; press for details, hold to compact in Codex", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
            // Until live status is set up the key says so instead of a value; the first press arms it and
            // says what a second press will change, the second press enables (#31) — see LiveStatusGate.
            _gate = new LiveStatusGate(_bridge, "Context", () => this.ActionImageChanged());

            _bridge.OnStateChanged += (state) =>
            {
                // Codex's per-tab state file is a hook envelope, not a Claude status-line object.
                // Its context figure is parsed by the agent adapter into the session registry.
                // Reading the envelope through ClaudeState yields an empty ContextWindow and used
                // to paint the very plausible—but false—value "100% left" on every Codex tab.
                if (this.ShowsRemaining)
                {
                    this.RefreshCodexContext();
                    return;
                }

                var ctx = state.ContextWindow;
                var used = ctx?.TotalInputTokens ?? 0;
                var max = ctx?.MaxTokens ?? 0;
                var pctD = ctx?.UsedPercentage ?? 0;
                // Status line provides used_percentage directly; fall back to tokens/size if absent.
                var pct = pctD > 0 ? (Int32)Math.Round(pctD)
                        : max > 0 ? (Int32)Math.Round(100.0 * used / max)
                        : 0;

                if (pct != _percent || used != _usedTokens || max != _maxTokens || !_hasData)
                {
                    _percent = pct;
                    _usedTokens = used;
                    _maxTokens = max;
                    _hasData = true;
                    this.ActionImageChanged();
                }
            };

            // The Codex rollout grows without necessarily changing the hook envelope. Grid.Refresh
            // reparses it on every poll and raises this event only when the visible percentage has
            // changed, so the gauge stays live without causing a redraw storm.
            _bridge.Grid.OnGridChanged += () =>
            {
                if (this.ShowsRemaining)
                {
                    this.RefreshCodexContext();
                }
            };

            // The session on the display keys has reported nothing — a tab whose Claude has not run
            // a turn yet, or one started before the status-line bridge was wired. Show a dash rather
            // than the last writer's numbers, which would be a different session's (#49).
            _bridge.OnStateUnavailable += () =>
            {
                if (_hasData)
                {
                    _hasData = false;
                    this.ActionImageChanged();
                }
            };
        }

        private void RefreshCodexContext()
        {
            var tty = _bridge.DisplayTty();
            var sessions = _bridge.Grid.Sessions;
            var percent = CodexPercentFor(tty, sessions);

            if (percent == null)
            {
                if (_hasData)
                {
                    _hasData = false;
                    this.ActionImageChanged();
                }
                return;
            }

            var value = Math.Clamp(percent.Value, 0, 100);
            if (!_hasData || value != _percent || _usedTokens != 0 || _maxTokens != 0)
            {
                _percent = value;
                _usedTokens = 0;
                _maxTokens = 0;
                _hasData = true;
                this.ActionImageChanged();
            }
        }

        /// <summary>
        /// Resolve only the session currently being displayed. Unknown stays unknown: converting a
        /// missing value to zero is what made the gauge claim a fresh 100% on every Codex session.
        /// </summary>
        internal static Int32? CodexPercentFor(
            String tty,
            IReadOnlyDictionary<String, GridSession> sessions) =>
            tty != null && sessions != null && sessions.TryGetValue(tty, out var session)
                ? session.CtxPercent
                : null;

        // The key owns its button events: the service would otherwise run the short action the
        // instant the key goes down, before it can know a long press is coming. Short press = the
        // key's own job (on release); long press = the way off. See LiveStatusGate.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent) =>
            _gate.HandleButton(
                buttonEvent.EventType,
                () => this.RunCommand(actionParameter),
                this.ShowsRemaining ? this.Compact : null);

        protected override void RunCommand(String actionParameter)
        {
            var command = _bridge.Agent.SlashCommand(AgentVerb.Context);
            if (command == null)
            {
                PluginLog.Info($"ContextCommand: {_bridge.Agent.DisplayName} has no context/status command");
                return;
            }
            _bridge.SendPrompt(command);
            PluginLog.Info($"ContextCommand: {command}");
        }

        private void Compact()
        {
            var command = _bridge.Agent.SlashCommand(AgentVerb.Compact);
            if (command == null)
            {
                return;
            }
            _bridge.SendPrompt(command);
            PluginLog.Info($"ContextCommand: long press {command}");
        }

        private String Label()
        {
            var setup = _gate.Label;
            if (setup != null)
            {
                return setup;
            }

            if (!_hasData)
            {
                return "—";
            }

            var displayPercent = this.ShowsRemaining ? Math.Max(0, 100 - _percent) : _percent;
            var suffix = this.ShowsRemaining ? " left" : String.Empty;
            if (_maxTokens <= 0)
            {
                return $"{displayPercent}%{suffix}";
            }

            var used = _usedTokens >= 1000 ? $"{_usedTokens / 1000}k" : _usedTokens.ToString();
            var max = _maxTokens >= 1_000_000 ? $"{_maxTokens / 1_000_000}M"
                    : _maxTokens >= 1000 ? $"{_maxTokens / 1000}k"
                    : _maxTokens.ToString();
            return $"{displayPercent}%{suffix}\n{used}/{max}";
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => this.Label().Replace("\n", Environment.NewLine);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Proactive warning: the gauge turns amber as the window fills, red when nearly full.
            var icon = _percent >= 90 ? "gauge_crit" : _percent >= 75 ? "gauge_warn" : "gauge";
            return KeyImage.Render(imageSize, this.Label(), KeyImage.Slate, icon);
        }
    }
}
