namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// The plugin's ClientApplication — deliberately EMPTY, because this is a universal plugin.
    /// The service refuses to load an assembly without one; the yaml's HasNoApplication is what
    /// makes it bind nothing. See ClaudeConsoleApplication for the afternoon that established this.
    /// </summary>
    public class VizhiCodexApplication : ClientApplication
    {
        public VizhiCodexApplication()
        {
        }
    }
}
