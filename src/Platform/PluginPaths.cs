namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.IO;

    /// <summary>
    /// Where the plugin's own files live on disk.
    ///
    /// AppContext.BaseDirectory and Assembly.Location DO NOT WORK here: the SDK loads the plugin
    /// into its own context, so Assembly.Location comes back empty and BaseDirectory points at the
    /// SERVICE's directory, not the plugin's. Anything shipped inside the package — the voice
    /// payload, the Windows helper executables — must be found relative to the path the SDK hands
    /// us in Plugin.AssemblyFilePath.
    ///
    /// Learned the hard way twice: EnsureVoiceRuntimeInstalled already threaded
    /// PluginAssemblyFilePath through for the same reason, and the Windows helpers then repeated
    /// the mistake with AppContext.BaseDirectory — which logged
    /// "claude-console-inject.exe not found in the plugin package" while the exe sat right beside
    /// the DLL. This type exists so there is ONE answer to "where am I installed".
    /// </summary>
    internal static class PluginPaths
    {
        /// <summary>
        /// Set once from ClaudeConsolePlugin.Load via the SDK's Plugin.AssemblyFilePath.
        /// Null until then, and null in unit tests unless a test sets it.
        /// </summary>
        internal static String PluginAssemblyFilePath { get; set; }

        /// <summary>
        /// The directory holding the plugin DLL — and therefore the helper executables and the
        /// voice payload shipped beside it. Null when the SDK path hasn't been provided.
        /// </summary>
        internal static String PluginDirectory
        {
            get
            {
                var asm = PluginAssemblyFilePath;
                if (String.IsNullOrEmpty(asm))
                {
                    return null;
                }

                try
                {
                    return Path.GetDirectoryName(asm);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Full path to a file shipped beside the plugin DLL, or null if it isn't there.
        /// Resolved on every call rather than cached: the SDK path arrives after construction.
        /// </summary>
        internal static String PackagedFile(String fileName)
        {
            var dir = PluginDirectory;
            if (String.IsNullOrEmpty(dir))
            {
                return null;
            }

            try
            {
                var candidate = Path.Combine(dir, fileName);
                return File.Exists(candidate) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
