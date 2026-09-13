namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    public class AgentBridgeStatusTests
    {
        [Fact]
        public void Every_non_ready_state_has_distinct_actionable_words()
        {
            Assert.Null(AgentBridgeNotice.FaceLabel(AgentBridgeStatus.Ready));

            Assert.Equal("Run /hooks", AgentBridgeNotice.FaceLabel(AgentBridgeStatus.AwaitingTrust));
            Assert.Contains("/hooks", AgentBridgeNotice.Message(AgentBridgeStatus.AwaitingTrust));
            Assert.Contains("Never use --dangerously-bypass-hook-trust", AgentBridgeNotice.Message(AgentBridgeStatus.AwaitingTrust));

            Assert.Equal("Setup failed", AgentBridgeNotice.FaceLabel(AgentBridgeStatus.InstallFailed));
            Assert.Contains("could not install", AgentBridgeNotice.Message(AgentBridgeStatus.InstallFailed));

            Assert.Equal("Merge hooks", AgentBridgeNotice.FaceLabel(AgentBridgeStatus.ForeignConfiguration));
            Assert.Contains("left it unchanged", AgentBridgeNotice.Message(AgentBridgeStatus.ForeignConfiguration));
            Assert.Equal("https://www.rshankar.com/keypad-profiles/#codex-hook-trust", AgentBridgeNotice.PublicHelpUrl);
        }

        [Fact]
        public void Bridge_state_notifies_only_when_it_changes()
        {
            var bridge = BridgeManager.Instance;
            bridge.SetAgentBridgeStatus(AgentBridgeStatus.Ready);
            var changes = 0;
            bridge.OnAgentBridgeStatusChanged += _ => changes++;

            bridge.SetAgentBridgeStatus(AgentBridgeStatus.AwaitingTrust);
            bridge.SetAgentBridgeStatus(AgentBridgeStatus.AwaitingTrust);
            bridge.SetAgentBridgeStatus(AgentBridgeStatus.Ready);

            Assert.Equal(2, changes);
        }

        [Fact]
        public void Vizhi_surfaces_each_hook_state_and_clears_it_on_the_first_active_event()
        {
            var product = File.ReadAllText(RepoFile("src", "Products", "VizhiCodex", "VizhiCodexPlugin.cs"));

            Assert.Contains("BridgeManager.Instance.Notify =", product);
            Assert.Contains("CodexBridgeStatus.AwaitingTrust", product);
            Assert.Contains("AgentBridgeStatus.AwaitingTrust", product);
            Assert.Contains("CodexBridgeStatus.ForeignHooksFile", product);
            Assert.Contains("AgentBridgeStatus.ForeignConfiguration", product);
            Assert.Contains("AgentBridgeStatus.InstallFailed", product);
            Assert.Contains("Grid.OnGridChanged += this.OnGridChanged", product);
            Assert.Contains("SetAgentBridgeStatus(AgentBridgeStatus.Ready)", product);
            Assert.Contains("PluginStatus.Normal, null, null, null", product);

            var wire = product.IndexOf("this.WireStateBridge();", StringComparison.Ordinal);
            var poll = product.IndexOf("BridgeManager.Instance.StartPolling();", StringComparison.Ordinal);
            Assert.True(wire >= 0 && poll > wire, "polling must start after the initial hook status is established");
        }

        [Fact]
        public void Both_products_initialize_logs_with_their_public_identity()
        {
            var claude = File.ReadAllText(RepoFile("src", "Products", "ClaudeConsole", "ClaudeConsolePlugin.cs"));
            var vizhi = File.ReadAllText(RepoFile("src", "Products", "VizhiCodex", "VizhiCodexPlugin.cs"));

            Assert.Contains("PluginLog.Init(this.Log, \"Claude Console\")", claude);
            Assert.Contains("PluginLog.Init(this.Log, \"Vizhi for Codex\")", vizhi);
            Assert.Equal("[Vizhi for Codex] bridge ready", PluginLog.Format("Vizhi for Codex", "bridge ready"));
            Assert.Equal("[Claude Console] bridge ready", PluginLog.Format("Claude Console", "bridge ready"));
        }

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, Path.Combine(relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }
    }
}
