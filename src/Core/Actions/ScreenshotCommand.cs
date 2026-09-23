namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Screenshot key — capture a region of the screen (the system's own Shift+Cmd+4 picker) and
    /// hand the image to the CURRENT conversation: the path is typed with a short instruction, no
    /// Return, so the user appends their question. The draft pattern, for the same reason Voice
    /// Draft exists: the interesting part comes after.
    ///
    /// Both agents take a path mid-conversation, by different routes that the sentence serves
    /// equally: Claude Code reads it directly; Codex's model opens it with its image-viewing tool
    /// when told to — the wording is near-verbatim from the July Vizhi plugin, which proved that
    /// route on hardware (VizhiActionRouter.cs:447). It looked impossible from the CLI (`-i`
    /// attaches to the initial prompt only), which is the trap: the CLI is not the only door —
    /// the model's own tools are another. ImageAtLaunch survives as the fallback for a
    /// hypothetical agent whose session genuinely cannot reach a file mid-conversation.
    ///
    /// The capture blocks while the user aims, so the work runs off the action thread — a keypad
    /// that freezes for the length of a screenshot reads as crashed.
    ///
    /// First use may trigger the one-time Screen Recording grant for the plugin service — the
    /// same shape as voice's Microphone grant, and like it, a silent no-file outcome afterwards
    /// means the grant was refused, not that the key is broken.
    /// </summary>
    public class ScreenshotCommand : PluginDynamicCommand
    {
        public ScreenshotCommand()
            : base(displayName: "Screenshot", description: Describe(), groupName: "Core")
        {
        }

        // Written before the SDK asks for anything else, so it must not assume more than the
        // agent declared in the product's constructor.
        private static String Describe()
        {
            var agent = BridgeManager.Instance.Agent;

            if (agent.Capabilities.ImageInConversation)
            {
                return "Capture a region of the screen into the CURRENT conversation — add your question, then Return";
            }

            if (agent.Capabilities.ImageAtLaunch)
            {
                return $"Capture a region of the screen and start a NEW {agent.DisplayName} session with it ({agent.CliCommand} -i)";
            }

            return "Capture a region of the screen (this agent cannot take images)";
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            var caps = bridge.Agent.Capabilities;

            if (!caps.ImageInConversation && !caps.ImageAtLaunch)
            {
                return;
            }

            // The picker blocks until the user drags a region or hits Esc; never on the action thread.
            Task.Run(() =>
            {
                try
                {
                    var path = bridge.CaptureScreenshot();
                    if (path == null)
                    {
                        return;
                    }

                    if (caps.ImageInConversation)
                    {
                        // The instruction, not just the path: it is what makes Codex's model open
                        // the file (Vizhi's proven wording), and it costs Claude Code nothing.
                        // No Return — the user's question comes after.
                        bridge.InjectText(
                            $"A screenshot was captured at {path}. Inspect this image with your available image-viewing tools before continuing. ",
                            pressEnter: false);
                    }
                    else
                    {
                        bridge.LaunchAgentSession("-i", path);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "ScreenshotCommand failed");
                }
            });
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "Shot";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "Shot", KeyImage.Blue, "screenshot");
    }
}
