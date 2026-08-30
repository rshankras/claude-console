namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    /// <summary>
    /// A throwaway home directory the engine's settings and runtime paths hang off for the life of
    /// one test (BridgeManager.HomeOverride). Grid code and the bridge both write under the real
    /// home when nothing redirects them — the suite's canary in tests/run-all.sh catches a test that
    /// forgets this, but only after the damage. Dispose restores the previous override.
    /// </summary>
    internal sealed class TempHome : IDisposable
    {
        private readonly String _previous;

        public String Dir { get; }

        public TempHome()
        {
            this.Dir = Path.Combine(Path.GetTempPath(), "cc-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Dir);
            _previous = BridgeManager.HomeOverride;
            BridgeManager.HomeOverride = this.Dir;
        }

        public String ClaudeDir => Path.Combine(this.Dir, ".claude");
        public String Settings => Path.Combine(this.ClaudeDir, "settings.json");
        public String Backup => Path.Combine(this.ClaudeDir, "settings.json.claude-console.bak");
        public String RuntimeHome => Path.Combine(this.ClaudeDir, "claude-console");
        public String Marker => Path.Combine(this.RuntimeHome, "no-autowire");
        public String ChainFile => Path.Combine(this.RuntimeHome, "statusline-chain");

        public void WriteSettings(String json)
        {
            Directory.CreateDirectory(this.ClaudeDir);
            File.WriteAllText(this.Settings, json);
        }

        public String ReadSettings() => File.Exists(this.Settings) ? File.ReadAllText(this.Settings) : null;

        public String[] LeftoverTemps() =>
            Directory.Exists(this.ClaudeDir) ? Directory.GetFiles(this.ClaudeDir, "settings.json.cc.*.tmp") : Array.Empty<String>();

        public void Dispose()
        {
            BridgeManager.HomeOverride = _previous;
            try { Directory.Delete(this.Dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
