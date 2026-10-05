namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>A refused folder activation stays visible until the user retries or goes back.</summary>
    public abstract class DesktopActionFolder : PluginDynamicFolder
    {
        private readonly DesktopBusyFeedback _feedback;
        internal Boolean RetryPending { get; private set; }
        protected DesktopActionFolder()
        {
            _feedback = new DesktopBusyFeedback(() => { if (this.Plugin != null) this.CommandImageChanged("retry"); });
            DesktopServices.Lifetime.OnStop(() => { RetryPending = false; _feedback.Dispose(); });
        }
        protected void ClearRetry() { RetryPending = false; _feedback.Clear("retry"); }
        protected void ShowRetry()
        {
            RetryPending = true;
            _feedback.Show("retry");
            RefreshActions();
        }
        protected void RefreshActions() { if (this.Plugin != null) this.ButtonActionNamesChanged(); }
        protected IEnumerable<String> RetryActions => new[]
        { this.CreateCommandName("retry") };
        public override String GetCommandDisplayName(String parameter, PluginImageSize size) => "\u200B";
        public override void RunCommand(String parameter)
        { if (parameter == "retry" && RetryPending) Activate(); }
        public override BitmapImage GetCommandImage(String parameter, PluginImageSize size) =>
            KeyImage.RenderIntentTile(size, _feedback.Contains("retry") ? "Busy" : "Try Again", "waiting", "TAP TO RETRY");
    }
}
