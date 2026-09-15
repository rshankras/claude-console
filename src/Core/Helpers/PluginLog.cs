namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    // A helper class that enables logging from the plugin code.

    internal static class PluginLog
    {
        private static PluginLogFile _pluginLogFile;
        private static String _productName = "Plugin";

        public static void Init(PluginLogFile pluginLogFile, String productName)
        {
            pluginLogFile.CheckNullArgument(nameof(pluginLogFile));
            productName.CheckNullArgument(nameof(productName));
            PluginLog._pluginLogFile = pluginLogFile;
            PluginLog._productName = productName;
        }

        internal static String Format(String productName, String text) => $"[{productName}] {text}";

        private static String Format(String text) => Format(PluginLog._productName, text);

        public static void Verbose(String text) => PluginLog._pluginLogFile?.Verbose(Format(text));

        public static void Verbose(Exception ex, String text) => PluginLog._pluginLogFile?.Verbose(ex, Format(text));

        public static void Info(String text) => PluginLog._pluginLogFile?.Info(Format(text));

        public static void Info(Exception ex, String text) => PluginLog._pluginLogFile?.Info(ex, Format(text));

        public static void Warning(String text) => PluginLog._pluginLogFile?.Warning(Format(text));

        public static void Warning(Exception ex, String text) => PluginLog._pluginLogFile?.Warning(ex, Format(text));

        public static void Error(String text) => PluginLog._pluginLogFile?.Error(Format(text));

        public static void Error(Exception ex, String text) => PluginLog._pluginLogFile?.Error(ex, Format(text));
    }
}
