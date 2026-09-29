// vizhi-desktop-uia — one-shot Windows UI Automation verbs for desktop agent apps.
//
// The JSON/argument contract mirrors tools/desktop/VizhiAxBridge.swift so the plugin consumes
// identical snapshots on macOS and Windows. `inspect` is the Windows W0 reconnaissance tool:
// run it without --window to list candidate top-level windows, then with the captured title to
// dump the bounded UIA tree. The action verbs refuse ambiguous app/composer matches.
//
// Verbs:
//   inspect [--window title]                         candidate windows or target UIA tree
//   status  --window title... --approve label...    one state snapshot
//   press   --window title... --label text...       invoke without foreground activation
//   write   --window title... --text text            set the sole writable composer
//   focus   --window title...                        deliberate foreground activation

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Automation;

internal static class Program
{
    private const Int32 MaxDepth = 40;
    private const Int32 MaxNodes = 2000;
    private const Int32 CardTextCap = 400;

    private sealed record Node(AutomationElement Element, String Role, String Text, Boolean Pressable, Int32 Depth);

    [SupportedOSPlatform("windows")]
    private static Int32 Main(String[] args)
    {
        try
        {
            var verb = args.FirstOrDefault() ?? "help";
            var options = ParseOptions(args.Skip(1).ToArray());

            if (verb is "help" or "--help")
            {
                Console.WriteLine("vizhi-desktop-uia <inspect|status|press|write|focus> [options]");
                return 0;
            }

            if (verb == "inspect" && Values(options, "--window").Count == 0
                && Values(options, "--process").Count == 0)
            {
                return InspectWindows();
            }

            var target = FindTarget(
                Values(options, "--process"), Values(options, "--window"), options.ContainsKey("--require-process"));
            if (target.Error != null)
            {
                return Fail(target.Error, target.Error == "ambiguous-app" ? 6 : 3);
            }

            var root = target.Element!;
            return verb switch
            {
                "inspect" => InspectTarget(root),
                "status" => Status(root, options),
                "press" => Press(root, options),
                "press-exact" => Press(root, options, exact: true),
                "write" => Write(root, options),
                "focus" => Focus(root),
                _ => Fail($"unknown-verb {verb}", 4),
            };
        }
        catch (ElementNotAvailableException)
        {
            return Fail("surface-unavailable", 5);
        }
        catch (Exception ex)
        {
            return Fail($"uia-error: {ex.GetType().Name}: {ex.Message}", 5);
        }
    }

    private static Dictionary<String, List<String>> ParseOptions(String[] args)
    {
        var result = new Dictionary<String, List<String>>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            if (!result.TryGetValue(key, out var values))
            {
                values = new List<String>();
                result[key] = values;
            }
            values.Add(value);
        }
        return result;
    }

    private static List<String> Values(Dictionary<String, List<String>> options, String name) =>
        options.TryGetValue(name, out var values) ? values : new List<String>();

    private static String? Value(Dictionary<String, List<String>> options, String name) =>
        Values(options, name).FirstOrDefault();

    [SupportedOSPlatform("windows")]
    private static (AutomationElement? Element, String? Error) FindTarget(
        IReadOnlyList<String> processNames, IReadOnlyList<String> titles, Boolean requireProcess)
    {
        var processMatches = new List<Process>();
        foreach (var raw in processNames)
        {
            var name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            try { processMatches.AddRange(Process.GetProcessesByName(name)); } catch { }
        }
        var processWindows = processMatches
            .Where(p => Safe(() => p.MainWindowHandle) != IntPtr.Zero)
            .GroupBy(p => p.Id).Select(g => g.First()).ToList();
        if (processWindows.Count == 1)
        {
            return (AutomationElement.FromHandle(processWindows[0].MainWindowHandle), null);
        }
        if (processWindows.Count > 1)
        {
            return (null, "ambiguous-app");
        }

        if (processNames.Count > 0 || requireProcess)
        {
            return (null, processNames.Count == 0 ? "identity-unconfirmed" : "app-not-running");
        }

        if (titles.Count == 0)
        {
            return (null, "app-not-running");
        }

        var matches = new List<AutomationElement>();
        foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            var name = Safe(() => candidate.Current.Name) ?? "";
            if (name.Length == 0 || !titles.Any(t =>
                    String.Equals(name, t, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            matches.Add(candidate);
        }

        var processIds = matches.Select(x => Safe(() => x.Current.ProcessId)).Where(x => x > 0).Distinct().ToList();
        if (processIds.Count == 0)
        {
            return (null, "app-not-running");
        }
        if (processIds.Count > 1)
        {
            return (null, "ambiguous-app");
        }

        return (matches.First(x => Safe(() => x.Current.ProcessId) == processIds[0]), null);
    }

    [SupportedOSPlatform("windows")]
    private static List<Node> Scan(AutomationElement root)
    {
        var nodes = new List<Node>();
        var walker = TreeWalker.RawViewWalker;

        void Visit(AutomationElement element, Int32 depth)
        {
            if (depth > MaxDepth || nodes.Count >= MaxNodes)
            {
                return;
            }

            var type = Safe(() => element.Current.ControlType) ?? ControlType.Custom;
            nodes.Add(new Node(element, type.ProgrammaticName, DisplayText(element), CanPress(element), depth));

            AutomationElement? child = null;
            try { child = walker.GetFirstChild(element); } catch (ElementNotAvailableException) { }
            while (child != null && nodes.Count < MaxNodes)
            {
                Visit(child, depth + 1);
                try { child = walker.GetNextSibling(child); } catch (ElementNotAvailableException) { child = null; }
            }
        }

        Visit(root, 0);
        return nodes;
    }

    [SupportedOSPlatform("windows")]
    private static String DisplayText(AutomationElement element)
    {
        var name = Safe(() => element.Current.Name) ?? "";
        if (name.Length > 0)
        {
            return name;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw))
        {
            return ((ValuePattern)raw).Current.Value ?? "";
        }
        return Safe(() => element.Current.HelpText) ?? "";
    }

    [SupportedOSPlatform("windows")]
    private static Boolean CanPress(AutomationElement element) =>
        element.TryGetCurrentPattern(InvokePattern.Pattern, out _)
        || element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _);

    private static Node? FirstPressable(IEnumerable<Node> nodes, IReadOnlyList<String> labels)
    {
        var needles = labels.Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
        return nodes.FirstOrDefault(n => n.Pressable && n.Text.Length > 0
            && needles.Any(label => n.Text.Contains(label, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<Node> ExactButtons(IReadOnlyList<Node> nodes, IReadOnlyList<String> labels) =>
        nodes.Where((n, i) => n.Role == ControlType.Button.ProgrammaticName && n.Text.Length > 0
            && labels.Contains(n.Text, StringComparer.Ordinal)
            && !nodes.Skip(i + 1).TakeWhile(child => child.Depth > n.Depth).Any(child => child.Pressable)).ToList();

    private static String Collapse(String text) =>
        String.Join(" ", text.Split((Char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static String CardText(Node anchor, IReadOnlyList<Node> nodes)
    {
        var index = -1;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i].Element, anchor.Element))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return "";
        }

        var texts = nodes.Skip(Math.Max(0, index - 20)).Take(Math.Min(20, index))
            .Where(n => n.Role == ControlType.Text.ProgrammaticName && n.Text.Length > 0)
            .Select(n => n.Text).TakeLast(3);
        var text = Collapse(String.Join(" ", texts));
        return text.Length > CardTextCap ? text[..CardTextCap] : text;
    }

    [SupportedOSPlatform("windows")]
    private static Int32 Status(AutomationElement root, Dictionary<String, List<String>> options)
    {
        var nodes = Scan(root);
        if (nodes.Count <= 1)
        {
            return Emit(new Dictionary<String, Object?> { ["surface"] = false });
        }

        var approve = FirstPressable(nodes, Values(options, "--approve"));
        var deny = FirstPressable(nodes, Values(options, "--deny"));
        var stop = ExactButtons(nodes, Values(options, "--stop")).FirstOrDefault();
        var attentionNeedle = Value(options, "--attention") ?? "";
        var modePrefix = Value(options, "--mode-prefix") ?? "";
        var mode = modePrefix.Length == 0 ? "" : nodes.Select(n => n.Text)
            .FirstOrDefault(t => t.StartsWith(modePrefix, StringComparison.OrdinalIgnoreCase))?[modePrefix.Length..] ?? "";

        return Emit(new Dictionary<String, Object?>
        {
            ["surface"] = true,
            ["attention"] = attentionNeedle.Length > 0 && nodes.Any(n => n.Text.Contains(attentionNeedle, StringComparison.OrdinalIgnoreCase)),
            ["approvalPresent"] = approve != null,
            ["denyPresent"] = deny != null,
            ["stopPresent"] = stop != null,
            ["cardText"] = approve == null ? "" : CardText(approve, nodes),
            ["mode"] = mode,
            ["conversations"] = Conversations(nodes, options),
        });
    }

    [SupportedOSPlatform("windows")]
    private static List<Dictionary<String, String>> Conversations(
        IReadOnlyList<Node> nodes, Dictionary<String, List<String>> options)
    {
        var marker = Value(options, "--conv-marker") ?? "";
        var awaiting = Value(options, "--state-awaiting") ?? "";
        var unread = Value(options, "--state-unread") ?? "";
        var result = new List<Dictionary<String, String>>();
        if (marker.Length == 0)
        {
            return result;
        }

        for (var i = 0; i < nodes.Count && result.Count < 8; i++)
        {
            var node = nodes[i];
            if (!node.Pressable || node.Text.Length == 0)
            {
                continue;
            }

            var j = i + 1;
            var hasMarker = false;
            var state = "idle";
            while (j < nodes.Count && nodes[j].Depth > node.Depth)
            {
                var child = nodes[j];
                hasMarker |= child.Pressable && child.Text.Contains(marker, StringComparison.OrdinalIgnoreCase);
                if (awaiting.Length > 0 && child.Text.Contains(awaiting, StringComparison.OrdinalIgnoreCase)) state = "awaiting";
                else if (state == "idle" && unread.Length > 0 && child.Text.Contains(unread, StringComparison.OrdinalIgnoreCase)) state = "unread";
                j++;
            }

            if (!hasMarker)
            {
                continue;
            }

            var selected = node.Element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                && ((SelectionItemPattern)pattern).Current.IsSelected;
            result.Add(new Dictionary<String, String>
            {
                ["title"] = node.Text,
                ["state"] = state,
                ["selected"] = selected ? "true" : "false",
            });
            i = j - 1;
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static Int32 Press(AutomationElement root, Dictionary<String, List<String>> options, Boolean exact = false)
    {
        var labels = Values(options, "--label");
        if (labels.Count == 0)
        {
            return Fail("no --label given", 4);
        }

        var nodes = Scan(root);
        var marker = Value(options, "--conversation");
        var target = FirstPressable(nodes, labels);
        if (exact)
        {
            var matches = ExactButtons(nodes, labels);
            target = matches.Count == 1 && matches[0].Pressable && matches[0].Element.Current.IsEnabled
                ? matches[0] : null;
        }
        if (marker != null)
        {
            var matches = nodes.Where((node, i) => node.Pressable
                && String.Equals(node.Text, labels[0], StringComparison.Ordinal)
                && nodes.Skip(i + 1).TakeWhile(child => child.Depth > node.Depth)
                    .Any(child => child.Pressable && child.Text == marker)).ToList();
            if (matches.Count > 1) return Fail("ambiguous-conversation", 4);
            target = matches.SingleOrDefault();
        }
        if (target == null)
        {
            return Fail("no-match", 4);
        }

        var expected = Collapse(Value(options, "--expect-near") ?? "");
        if (expected.Length > 0)
        {
            var seen = CardText(target, nodes);
            if (seen.Length == 0 || !(seen.Contains(expected, StringComparison.Ordinal)
                || expected.Contains(seen, StringComparison.Ordinal)))
            {
                return Fail("card-changed", 6);
            }
        }

        var before = ForegroundProcessName();
        if (!Invoke(target.Element))
        {
            return Fail("press-failed", 5);
        }
        return Emit(new Dictionary<String, Object?>
        {
            ["matched"] = target.Text,
            ["frontBefore"] = before,
            ["frontAfter"] = ForegroundProcessName(),
        });
    }

    [SupportedOSPlatform("windows")]
    private static Boolean Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return true;
        }
        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection))
        {
            ((SelectionItemPattern)selection).Select();
            return true;
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    private static Int32 Write(AutomationElement root, Dictionary<String, List<String>> options)
    {
        var text = Value(options, "--text") ?? "";
        if (text.Length == 0)
        {
            return Fail("no --text given", 4);
        }

        var writable = Scan(root).Where(n => n.Role == ControlType.Edit.ProgrammaticName
            && n.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
            && !((ValuePattern)value).Current.IsReadOnly).ToList();
        if (writable.Count == 0)
        {
            return Fail("no-composer", 4);
        }
        if (writable.Count > 1)
        {
            return Fail("ambiguous-composer", 6);
        }

        var composer = writable[0].Element;
        var valuePattern = (ValuePattern)composer.GetCurrentPattern(ValuePattern.Pattern);
        valuePattern.SetValue(text);
        Thread.Sleep(200);
        if (!valuePattern.Current.Value.Contains(text, StringComparison.Ordinal))
        {
            return Fail("write-not-applied", 5);
        }

        var sent = false;
        var sendLabel = Value(options, "--send-label");
        if (!String.IsNullOrEmpty(sendLabel))
        {
            var send = FirstPressable(Scan(root), new[] { sendLabel });
            if (send == null || !Invoke(send.Element))
            {
                return Fail("send-not-found", 4);
            }
            sent = true;
        }

        return Emit(new Dictionary<String, Object?> { ["method"] = "value", ["sent"] = sent });
    }

    [SupportedOSPlatform("windows")]
    private static Int32 Focus(AutomationElement root)
    {
        var hwnd = new IntPtr(Safe(() => root.Current.NativeWindowHandle));
        if (hwnd == IntPtr.Zero)
        {
            return Fail("no-window-handle", 5);
        }
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // Windows may reject SetForegroundWindow for a background helper because of the
        // foreground-lock rule. This command is always the direct result of a physical key press,
        // so use the same explicit-user-intent fallback as ClaudeConsoleFocus.
        if (!SetForegroundWindow(hwnd))
        {
            SwitchToThisWindow(hwnd, true);
        }

        // The app may activate a different owned top-level window, so compare process identity
        // instead of demanding the exact UIA root handle remain foreground.
        Thread.Sleep(100);
        var targetPid = Safe(() => root.Current.ProcessId);
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
        return targetPid > 0 && foregroundPid == (UInt32)targetPid
            ? Emit(new Dictionary<String, Object?>())
            : Fail("focus-failed", 5);
    }

    [SupportedOSPlatform("windows")]
    private static Int32 InspectWindows()
    {
        var windows = new List<Dictionary<String, Object?>>();
        foreach (AutomationElement element in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            var pid = Safe(() => element.Current.ProcessId);
            var title = Safe(() => element.Current.Name) ?? "";
            if (pid <= 0 || title.Length == 0)
            {
                continue;
            }

            windows.Add(new Dictionary<String, Object?>
            {
                ["title"] = title,
                ["pid"] = pid,
                ["process"] = ProcessName(pid),
                ["class"] = Safe(() => element.Current.ClassName) ?? "",
            });
        }
        return Emit(new Dictionary<String, Object?> { ["windows"] = windows });
    }

    [SupportedOSPlatform("windows")]
    private static Int32 InspectTarget(AutomationElement root)
    {
        var nodes = Scan(root).Select(n => new Dictionary<String, Object?>
        {
            ["depth"] = n.Depth,
            ["role"] = n.Role,
            ["text"] = n.Text,
            ["pressable"] = n.Pressable,
            ["class"] = Safe(() => n.Element.Current.ClassName) ?? "",
            ["automationId"] = Safe(() => n.Element.Current.AutomationId) ?? "",
            ["offscreen"] = Safe(() => n.Element.Current.IsOffscreen),
        }).ToList();
        return Emit(new Dictionary<String, Object?>
        {
            ["process"] = ProcessName(Safe(() => root.Current.ProcessId)),
            ["title"] = Safe(() => root.Current.Name) ?? "",
            ["nodes"] = nodes,
        });
    }

    private static T Safe<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default!; }
    }

    private static String ProcessName(Int32 pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch { return ""; }
    }

    private static String ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessName((Int32)pid);
    }

    private static Int32 Emit(Dictionary<String, Object?> payload, Int32 code = 0)
    {
        payload["ok"] = code == 0;
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return code;
    }

    private static Int32 Fail(String error, Int32 code) =>
        Emit(new Dictionary<String, Object?> { ["error"] = error }, code);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern UInt32 GetWindowThreadProcessId(IntPtr hwnd, out UInt32 processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean ShowWindow(IntPtr hwnd, Int32 command);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hwnd, Boolean altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean IsIconic(IntPtr hwnd);

    private const Int32 SW_RESTORE = 9;
}
