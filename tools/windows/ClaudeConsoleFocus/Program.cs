// claude-console-focus — brings one specific Claude Code session's Windows Terminal tab to the
// front. The missing half of the Phase 3 gap: wt.exe can raise a window but cannot select a tab,
// and there is no supported API mapping a process to its tab. What DOES hold: the tab's label IS
// the session's console title (Windows Terminal renders the ConPTY title on the tab), and we can
// read that title by attaching to the session's console — the same attach the inject helper
// already performs. So: verify the session, read its title, find the TabItem with that name via
// UI Automation, select it, and bring the window forward.
//
// A separate short-lived process for the same two reasons as claude-console-inject: AttachConsole
// mutates global state the plugin host must never touch, and a UI Automation walk is a job for a
// process that exits. UIA is driven through its COM interface (UIAutomationCore.dll ships with
// Windows), not the WPF wrapper System.Windows.Automation: that wrapper lives in the Desktop
// Runtime, which made this exe either framework-dependent and broken on clean machines (#83) or
// self-contained and 68 MB. As a plain console helper it trims to the size of the others.
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

[SupportedOSPlatform("windows")]
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
    private static Int32 FocusTab(Int32 pid, String? firstTitle)
    {
        const Int32 attempts = 4;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var title = attempt == 0 ? firstTitle : ConsoleTitleOf(pid).title;

            var windows = TerminalWindows();
            if (windows.Length == 0)
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
                if (ClaudeConsoleFocus.TabSelection.TrySelect(matches.Count,
                    () => SelectByNonce(pid), () => Select(matches[0].Window, matches[0].Tab)))
                {
                    return ExitOk;
                }
                if (matches.Count > 1)
                {
                    Console.Error.WriteLine("multiple tabs match; target identity could not be verified");
                    Raise(matches[0].Window);
                    return ExitRaisedOnly;
                }
            }

            if (attempt < attempts - 1)
            {
                Thread.Sleep(120);
            }
            else
            {
                // A renamed or stale tab label can match nothing. Try the same identity
                // challenge used for duplicate labels before declaring the target unresolved.
                if (SelectByNonce(pid)) { return ExitOk; }
                // Out of retries — not Windows Terminal, or the tab really isn't there.
                Raise(windows.GetElement(0));
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
    private static List<(IUIAutomationElement Window, IUIAutomationElement Tab)> MatchingTabs(
        IUIAutomationElementArray windows, String title)
    {
        var exact = new List<(IUIAutomationElement, IUIAutomationElement)>();
        var fuzzy = new List<(IUIAutomationElement, IUIAutomationElement)>();
        var core = TitleCore(title);

        for (var w = 0; w < windows.Length; w++)
        {
            var window = windows.GetElement(w);
            var tabs = TabsOf(window);

            for (var t = 0; t < tabs.Length; t++)
            {
                var tab = tabs.GetElement(t);
                var label = tab.CurrentName ?? String.Empty;
                if (String.Equals(label, title, StringComparison.Ordinal))
                {
                    exact.Add((window, tab));
                    continue;
                }

                var name = TitleCore(label);
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

    private static void Select(IUIAutomationElement window, IUIAutomationElement tab)
    {
        if (tab.GetCurrentPattern(UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern pattern)
        {
            pattern.Select();
        }
        Raise(window);
    }

    /// <summary>
    /// Selection by identity for duplicate labels: retitle the TARGET's console to a nonce,
    /// select the one tab that repaints to it, restore the original title. ConPTY forwards a
    /// SetConsoleTitle to the terminal as an OSC title sequence, so the tab label follows within
    /// a repaint. The restore is in a finally — a helper that leaves a nonce on a user's tab has
    /// turned a cosmetic miss into vandalism. False means the nonce never appeared (a terminal
    /// that debounces titles, or an app that repaints its own immediately) — the caller reports
    /// unresolved focus and must not select an arbitrary matching tab.
    /// </summary>
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

                var windows = TerminalWindows();
                for (var w = 0; w < windows.Length; w++)
                {
                    var window = windows.GetElement(w);
                    var tabs = TabsOf(window);
                    for (var t = 0; t < tabs.Length; t++)
                    {
                        var tab = tabs.GetElement(t);
                        if (String.Equals(tab.CurrentName, nonce, StringComparison.Ordinal))
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

    private static void Raise(IUIAutomationElement window)
    {
        var hwnd = window.CurrentNativeWindowHandle;
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

    // ---- UI Automation, through COM ---------------------------------------
    //
    // Only the vtable slots this helper calls are declared; a `_VtblGapN_M` method reserves the
    // M slots in between by count — the same device tlbimp uses, honoured by the runtime's
    // built-in COM interop. The order is UIAutomationClient.h's and must never be "tidied".

    private const Int32 TreeScopeChildren = 2;
    private const Int32 TreeScopeDescendants = 4;
    private const Int32 UIA_ControlTypePropertyId = 30003;
    private const Int32 UIA_ClassNamePropertyId = 30012;
    private const Int32 UIA_TabItemControlTypeId = 50019;
    private const Int32 UIA_SelectionItemPatternId = 10010;

    private static IUIAutomation? _uia;

    private static IUIAutomation Uia => _uia ??= (IUIAutomation)new CUIAutomation();

    /// <summary>Every top-level Windows Terminal window, by its stable window class.</summary>
    private static IUIAutomationElementArray TerminalWindows() =>
        Uia.GetRootElement().FindAll(
            TreeScopeChildren,
            Uia.CreatePropertyCondition(UIA_ClassNamePropertyId, TerminalWindowClass));

    /// <summary>Every TabItem anywhere under a terminal window.</summary>
    private static IUIAutomationElementArray TabsOf(IUIAutomationElement window) =>
        window.FindAll(
            TreeScopeDescendants,
            Uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_TabItemControlTypeId));

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation
    {
    }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void _VtblGap1_2();                                             // CompareElements, CompareRuntimeIds
        IUIAutomationElement GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        void _VtblGap2_16();                                            // ElementFromPoint … CreateFalseCondition
        IUIAutomationCondition CreatePropertyCondition(Int32 propertyId, [MarshalAs(UnmanagedType.Struct)] Object value);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void _VtblGap1_3();                                             // SetFocus, GetRuntimeId, FindFirst
        IUIAutomationElementArray FindAll(Int32 scope, IUIAutomationCondition condition);
        void _VtblGap2_9();                                             // FindFirstBuildCache … GetCachedPatternAs
        [return: MarshalAs(UnmanagedType.IUnknown)]
        Object? GetCurrentPattern(Int32 patternId);
        void _VtblGap3_6();                                             // GetCachedPattern … CurrentLocalizedControlType
        String? CurrentName { [return: MarshalAs(UnmanagedType.BStr)] get; }
        void _VtblGap4_12();                                            // CurrentAcceleratorKey … CurrentIsPassword
        IntPtr CurrentNativeWindowHandle { get; }
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        Int32 Length { get; }
        IUIAutomationElement GetElement(Int32 index);
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition
    {
    }

    [ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationSelectionItemPattern
    {
        void Select();
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
