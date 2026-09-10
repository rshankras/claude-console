// claude-console-focus — brings one specific Claude Code session's Windows Terminal tab to the
// front. The missing half of the Phase 3 gap: wt.exe can raise a window but cannot select a tab,
// and there is no supported API mapping a process to its tab. What DOES hold: the tab's label IS
// the session's console title (Windows Terminal renders the ConPTY title on the tab), and we can
// read that title by attaching to the session's console — the same attach the inject helper
// already performs. So: verify the session, read its title, find the TabItem with that name via
// UI Automation, select it, and bring the window forward.
//
// A separate short-lived process for the same two reasons as claude-console-inject: AttachConsole
// mutates global state the plugin host must never touch, and this exe (alone of the three) needs
// the Windows Desktop Runtime for System.Windows.Automation, bundled inside this executable.
//
// Exit codes (the contract with WindowsPlatformBridge.FocusSession):
//   0 tab selected and window raised
//   2 session missing/not verifiable
//   4 window raised but the tab could not be identified (title matched no tab, or no console)
//   5 session elevated (attach denied across integrity levels)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Automation;

internal static class Program
{
    private const Int32 ExitOk = 0;
    private const Int32 ExitSessionMissing = 2;
    private const Int32 ExitRaisedOnly = 4;
    private const Int32 ExitSessionElevated = 5;

    private const Int32 ErrorAccessDenied = 5;

    // Windows Terminal's top-level window class — stable across releases; the same class name
    // the terminal's own docs suggest for window discovery.
    private const String TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    [SupportedOSPlatform("windows")]
    private static Int32 Main(String[] args)
    {
        try
        {
            var opts = ParseOptions(args);

            if (!Int32.TryParse(opts.GetValueOrDefault("--pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                Console.Error.WriteLine("usage: claude-console-focus tab --pid N --start-ticks T");
                return ExitSessionMissing;
            }

            // The same PID-recycling guard as the inject helper: never act on a process that
            // isn't the session the key was minted for.
            if (Int64.TryParse(opts.GetValueOrDefault("--start-ticks"), NumberStyles.None, CultureInfo.InvariantCulture, out var expectedTicks)
                && expectedTicks > 0)
            {
                if (!VerifyStartTime(pid, expectedTicks, out var why))
                {
                    Console.Error.WriteLine($"target not verified: {why}");
                    return ExitSessionMissing;
                }
            }

            // Verify we can attach at all before the retry loop, so "elevated" is reported as
            // itself rather than as a focus miss.
            var (probe, attachError) = ConsoleTitleOf(pid);
            if (probe == null && attachError == ErrorAccessDenied)
            {
                return ExitSessionElevated;
            }

            return FocusTab(pid, probe);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"claude-console-focus: {ex.Message}");
            return ExitRaisedOnly;
        }
    }

    private static Dictionary<String, String> ParseOptions(String[] args)
    {
        var opts = new Dictionary<String, String>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                opts[args[i]] = args[i + 1];
                i++;
            }
        }
        return opts;
    }

    [SupportedOSPlatform("windows")]
    private static Boolean VerifyStartTime(Int32 pid, Int64 expectedTicks, out String why)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var actual = proc.StartTime.ToUniversalTime().Ticks;
            if (actual != expectedTicks)
            {
                why = $"pid {pid} started at {actual}, expected {expectedTicks} (PID recycled — session is gone)";
                return false;
            }
            why = "";
            return true;
        }
        catch (ArgumentException)
        {
            why = $"pid {pid} is not running";
            return false;
        }
        catch (Exception ex)
        {
            why = $"could not verify pid {pid}: {ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// The session's console title — which is what Windows Terminal shows on its tab. Null when
    /// the console can't be attached (the error code says why).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (String? title, Int32 error) ConsoleTitleOf(Int32 pid)
    {
        FreeConsole();
        if (!AttachConsole((UInt32)pid))
        {
            return (null, Marshal.GetLastWin32Error());
        }

        try
        {
            var sb = new StringBuilder(1024);
            var len = GetConsoleTitleW(sb, (UInt32)sb.Capacity);
            return (len > 0 ? sb.ToString(0, (Int32)len) : null, 0);
        }
        finally
        {
            FreeConsole();
        }
    }

    /// <summary>
    /// Select the Windows Terminal tab whose label matches the session's console title and bring
    /// its window forward. Retries with a FRESH title read each attempt: a busy Claude animates
    /// the title's leading glyph (✳ · ✢ …), so a single read can disagree with the tab's name by
    /// the time the UIA walk runs — seen on real hardware 2026-08-07, where the idle session
    /// matched and the busy one didn't. With no match after the retries the first terminal
    /// window is raised anyway — the right window with the wrong tab beats doing nothing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Int32 FocusTab(Int32 pid, String? firstTitle)
    {
        const Int32 attempts = 4;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var title = attempt == 0 ? firstTitle : ConsoleTitleOf(pid).title;

            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, TerminalWindowClass));

            if (windows.Count == 0)
            {
                Console.Error.WriteLine("no Windows Terminal window found");
                return ExitRaisedOnly;
            }

            if (title != null)
            {
                var matches = MatchingTabs(windows, title);

                // Two tabs with the same label are indistinguishable BY LABEL — two sessions
                // started in the same directory both title their console after it. But the label
                // IS the target's console title, and we hold that console: briefly retitle it to
                // a nonce, select the one tab that repaints to the nonce, restore. That is
                // selection by identity, not by name — seen needed on hardware 2026-08-20, where
                // the first "sahan" tab won and the session lived in the second.
                if (matches.Count > 1 && SelectByNonce(pid))
                {
                    return ExitOk;
                }

                if (matches.Count > 0)
                {
                    Select(matches[0].Window, matches[0].Tab);
                    return ExitOk;
                }
            }

            if (attempt < attempts - 1)
            {
                Thread.Sleep(120);
            }
            else
            {
                // Out of retries — not Windows Terminal, or the tab really isn't there.
                Raise((AutomationElement)windows[0]);
            }
        }

        return ExitRaisedOnly;
    }

    /// <summary>
    /// Every tab matching the title, across every terminal window — exact matches when any
    /// exist, else the fuzzy tiers. Exact first; then glyph-stripped, because a busy Claude
    /// animates the leading status glyph and the read and the walk can straddle a repaint; then
    /// prefix, because the terminal ellipsizes long titles and Claude's conversation summaries
    /// are long. Returning ALL matches is what lets the caller see a duplicate and switch to
    /// selection by identity instead of by name.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<(AutomationElement Window, AutomationElement Tab)> MatchingTabs(
        AutomationElementCollection windows, String title)
    {
        var exact = new List<(AutomationElement, AutomationElement)>();
        var fuzzy = new List<(AutomationElement, AutomationElement)>();
        var core = TitleCore(title);

        foreach (AutomationElement window in windows)
        {
            var tabs = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

            foreach (AutomationElement tab in tabs)
            {
                if (String.Equals(tab.Current.Name, title, StringComparison.Ordinal))
                {
                    exact.Add((window, tab));
                    continue;
                }

                var name = TitleCore(tab.Current.Name);
                if (core.Length > 0 && name.Length > 0 &&
                    (String.Equals(name, core, StringComparison.Ordinal)
                     || core.StartsWith(name.TrimEnd('…'), StringComparison.Ordinal)
                     || name.StartsWith(core, StringComparison.Ordinal)))
                {
                    fuzzy.Add((window, tab));
                }
            }
        }

        return exact.Count > 0 ? exact : fuzzy;
    }

    [SupportedOSPlatform("windows")]
    private static void Select(AutomationElement window, AutomationElement tab)
    {
        if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
        {
            ((SelectionItemPattern)pattern).Select();
        }
        Raise(window);
    }

    /// <summary>
    /// Selection by identity for duplicate labels: retitle the TARGET's console to a nonce,
    /// select the one tab that repaints to it, restore the original title. ConPTY forwards a
    /// SetConsoleTitle to the terminal as an OSC title sequence, so the tab label follows within
    /// a repaint. The restore is in a finally — a helper that leaves a nonce on a user's tab has
    /// turned a cosmetic miss into vandalism. False means the nonce never appeared (a terminal
    /// that debounces titles, or an app that repaints its own immediately) — the caller falls
    /// back to first-match, which was the old behavior.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Boolean SelectByNonce(Int32 pid)
    {
        FreeConsole();
        if (!AttachConsole((UInt32)pid))
        {
            return false;
        }

        String? original = null;
        try
        {
            var sb = new StringBuilder(1024);
            var len = GetConsoleTitleW(sb, (UInt32)sb.Capacity);
            original = len > 0 ? sb.ToString(0, (Int32)len) : null;

            var nonce = "cc-" + Guid.NewGuid().ToString("N")[..12];
            if (!SetConsoleTitleW(nonce))
            {
                return false;
            }

            for (var i = 0; i < 8; i++)
            {
                Thread.Sleep(80);

                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, TerminalWindowClass));

                foreach (AutomationElement window in windows)
                {
                    var tabs = window.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

                    foreach (AutomationElement tab in tabs)
                    {
                        if (String.Equals(tab.Current.Name, nonce, StringComparison.Ordinal))
                        {
                            Select(window, tab);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        finally
        {
            if (original != null)
            {
                try { SetConsoleTitleW(original); } catch { /* the app repaints its own soon */ }
            }
            FreeConsole();
        }
    }

    /// <summary>
    /// A title minus its animated status prefix: everything up to the first letter or digit is
    /// glyph-and-space decoration Claude repaints while busy, and must not break the match.
    /// </summary>
    internal static String TitleCore(String? title)
    {
        if (String.IsNullOrEmpty(title))
        {
            return String.Empty;
        }

        var i = 0;
        while (i < title.Length && !Char.IsLetterOrDigit(title[i]))
        {
            i++;
        }
        return title[i..];
    }

    [SupportedOSPlatform("windows")]
    private static void Raise(AutomationElement window)
    {
        var hwnd = new IntPtr(window.Current.NativeWindowHandle);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // SetForegroundWindow is refused for background processes under the foreground lock;
        // SwitchToThisWindow is the documented-adjacent fallback that honors the user's intent
        // here (they pressed a physical key asking for this window).
        if (!SetForegroundWindow(hwnd))
        {
            SwitchToThisWindow(hwnd, true);
        }
    }

    // ---- Win32 -------------------------------------------------------------

    private const Int32 SW_RESTORE = 9;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean AttachConsole(UInt32 pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern UInt32 GetConsoleTitleW(StringBuilder title, UInt32 size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Boolean SetConsoleTitleW(String title);

    [DllImport("user32.dll")]
    private static extern Boolean SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hwnd, Boolean altTab);

    [DllImport("user32.dll")]
    private static extern Boolean IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern Boolean ShowWindow(IntPtr hwnd, Int32 cmd);
}
