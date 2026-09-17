namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Xunit;

    public sealed class WindowsGitBashFactAttribute : FactAttribute
    {
        internal static String BashPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        public WindowsGitBashFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !System.IO.File.Exists(BashPath))
            { this.Skip = "requires Windows with Git Bash to exercise MSYS argument conversion"; }
        }
    }

    /// <summary>
    /// A fact only Windows can run — it launches a win-x64 executable. Elsewhere it is REPORTED
    /// as skipped, never passed silently: the suite's rule is that a platform difference shows in
    /// the run summary ("Skipped: N"), not in an early return that counts as green.
    /// </summary>
    public sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                this.Skip = "runs the Windows hook exe; the macOS half of this contract is tests/scripts/test-bridge-scripts.sh";
            }
        }
    }
}
