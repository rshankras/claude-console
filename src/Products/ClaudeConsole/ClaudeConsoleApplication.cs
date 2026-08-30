namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// The plugin's ClientApplication — deliberately EMPTY, because this is a universal plugin.
    ///
    /// It has to exist. On 2026-08-28 the universal change deleted it along with the Terminal
    /// binding it carried, and the Logi Plugin Service then refused the assembly outright: "Cannot
    /// load plugin from …ClaudeConsolePlugin.dll", then "added to disabled plugins list" — no crash
    /// marker, no reason in any log, and the same DLL loaded fine in a plain .NET host. Rebuilding
    /// the previous commit into the same dev link loaded; probing Spotify — the universal plugin
    /// Logitech's QA cited as the model — showed a SpotifyApplication : ClientApplication that
    /// overrides nothing. The service requires the class; the yaml capability (HasNoApplication)
    /// decides whether it binds anything. Both products lost an afternoon to this once; do not
    /// delete it again.
    ///
    /// Nothing is overridden on purpose. GetProcessName/GetBundleName return "" from the base and
    /// that is correct under HasNoApplication. It was lethal only under HasApplication — the 1.5-era
    /// crash was this same empty shape declared as an APPLICATION plugin, which asked the service
    /// to associate a profile with an application that had no identity.
    /// </summary>
    public class ClaudeConsoleApplication : ClientApplication
    {
        public ClaudeConsoleApplication()
        {
        }
    }
}
