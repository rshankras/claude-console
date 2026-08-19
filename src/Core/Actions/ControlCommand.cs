namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// Session control keys (group "Core"): Esc, Mode, Tab, Compact, Clear, Exit. One auto-discovered
    /// command, one SDK action per control via AddParameter.
    ///   Esc     → Escape keystroke — interrupts/stops Claude, exits a mode, dismisses a menu.
    ///             Sent as a real key (code 53), NOT typed text and NOT followed by Enter.
    ///   Mode    → Shift+Tab keystroke — cycles Claude Code's input modes
    ///             (normal → auto-accept edits → plan). Action id stays "plan".
    ///   Tab     → Tab then Return in one press — accepts the highlighted autocomplete AND submits
    ///             (e.g. complete a slash command and run it). Distinct from Mode's Shift+Tab.
    ///   Compact → the agent's compact command
    ///   Clear   → the agent's clear command — resets the conversation ("/clear", "/new", …)
    ///   Exit    → the agent's exit command
    ///
    /// THE WORDS COME FROM THE AGENT, and a key the agent has no word for is never added. Typing
    /// "/context" at an agent that has no such command produces an error on screen and a key that
    /// looks broken; an absent key is honest. Same for Mode, which needs an input-mode cycle to
    /// drive. Claude Code supports all of them, so its profile is unchanged.
    /// (Context lives on its own gauge key — see ContextCommand.)
    /// </summary>
    public class ControlCommand : PluginDynamicCommand
    {
        private const String Esc = "esc";
        private const String Mode = "plan"; // action id kept as "plan" so existing key bindings survive the relabel
        private const String Tab = "tab";
        private const String Review = "review";
        private const String Compact = "compact";
        private const String Clear = "clear";
        private const String Exit = "exit";

        public ControlCommand()
            : base()
        {
            var agent = BridgeManager.Instance.Agent;

            // Escape is universal: every terminal agent treats it as interrupt/dismiss.
            this.AddParameter(Esc, "Esc", "Core")
                .SetDescription($"Interrupt {agent.DisplayName}, exit a mode, or dismiss a menu (Escape)");

            // Tab is NOT universal. It only earns a key where the agent has a completion to accept;
            // elsewhere the press does nothing and the key reads as broken.
            if (agent.Capabilities.TabCompletion)
            {
                this.AddParameter(Tab, "Tab", "Core")
                    .SetDescription("Accept the highlighted autocomplete and submit it (Tab, then Return)");
            }

            if (agent.Capabilities.InputModes)
            {
                this.AddParameter(Mode, "Mode", "Core")
                    .SetDescription("Cycle input mode: normal → auto-accept edits → plan (Shift+Tab)");
            }

            this.AddVerbParameter(agent, AgentVerb.Review, Review, "Review", "run the agent's native code review");
            this.AddVerbParameter(agent, AgentVerb.Compact, Compact, "Compact", "shrink the context window");
            this.AddVerbParameter(agent, AgentVerb.Clear, Clear, "Clear", "reset the conversation");
            this.AddVerbParameter(agent, AgentVerb.Exit, Exit, "Exit", $"quit the {agent.DisplayName} session");
        }

        // One key per verb the agent actually has a word for. The description names the real
        // command so the Options+ action list tells the truth about what a press will type.
        private void AddVerbParameter(IAgentAdapter agent, AgentVerb verb, String id, String label, String what)
        {
            var command = agent.SlashCommand(verb);
            if (command == null)
            {
                return;
            }

            this.AddParameter(id, label, "Core").SetDescription($"Run {command} to {what}");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Esc:
                    bridge.InjectKey(KeyStroke.Escape);
                    break;
                case Mode:
                    bridge.InjectKey(KeyStroke.ShiftTab); // cycle input modes
                    break;
                case Tab:
                    bridge.InjectTabThenEnter(); // Tab (accept autocomplete) + Return (submit), one press
                    break;
                case Review:
                    SendVerb(bridge, AgentVerb.Review);
                    break;
                case Compact:
                    SendVerb(bridge, AgentVerb.Compact);
                    break;
                case Clear:
                    SendVerb(bridge, AgentVerb.Clear);
                    break;
                case Exit:
                    SendVerb(bridge, AgentVerb.Exit);
                    break;
            }

            PluginLog.Info($"ControlCommand: {actionParameter}");
        }

        // Two Core keys carry icons that CANNOT be the parameter id: review.png is the Prompts
        // family's blue (and still bound on the prompts page), and clear.png is Git-amber by
        // designer-pack inheritance. Purple _core variants keep page 2 one family — and give the
        // native review a different glyph from the review prompt, so the two stop wearing one face.
        private static String IconFor(String actionParameter) =>
            actionParameter switch
            {
                Review => "review_core",
                Clear => "clear_core",
                _ => actionParameter,
            };

        // A key can only exist when the agent has a word for its verb, so a null here means the
        // binding outlived a change of agent — type nothing rather than something it will reject.
        private static void SendVerb(BridgeManager bridge, AgentVerb verb)
        {
            var command = bridge.Agent.SlashCommand(verb);
            if (command == null)
            {
                PluginLog.Info($"ControlCommand: {bridge.Agent.DisplayName} has no command for {verb} — ignoring");
                return;
            }

            bridge.SendPrompt(command);
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            switch (actionParameter)
            {
                case Esc: return "Esc";
                case Review: return "Review";
                case Mode: return "Mode";
                case Tab: return "Tab";
                case Compact: return "Compact";
                case Clear: return "Clear";
                case Exit: return "Exit";
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            BitmapColor color;
            switch (actionParameter)
            {
                case Esc: color = KeyImage.Red; break;
                case Exit: color = KeyImage.Red; break;
                case Mode: color = KeyImage.Purple; break;
                case Review: color = KeyImage.Purple; break;   // the Core family's colour
                case Clear: color = KeyImage.Purple; break;
                default: color = KeyImage.Slate; break; // Compact, Tab
            }
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), color, IconFor(actionParameter));
        }
    }
}
