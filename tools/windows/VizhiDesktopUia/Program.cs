// vizhi-desktop-uia — one-shot Windows UI Automation verbs for desktop agent apps.
//
// The Windows half of the desktop seam: what tools/desktop/VizhiAxBridge.swift is on macOS. Every
// invocation does ONE thing and exits with ONE line of JSON on stdout — deliberately not a daemon:
// the plugin spawns it through BoundedProcess (timeout + kill-tree), so a hung or malformed
// Chromium tree can be killed without wedging LogiPluginService.
//
// App-agnostic BY CONTRACT: this binary knows no control labels and no app names. Which app to
// drive (--process) and which labels mean approve/deny/stop/attention arrive as arguments from
// the product's IDesktopAppAdapter. Keep it that way — a second desktop app must cost an
// adapter, not a helper fork.
//
// The JSON contract mirrors the macOS helper field for field, so the monitor and every key
// consume the same snapshot on both operating systems. The matching rules live in
// UiaMatching.cs, where the test suite can reach them.
//
// Verbs (each takes --process <exe name>... --require-process; --window <title> is recon only):
//   inspect [--all]                         candidate windows, or the target's control tree
//   frontmost                               {"frontmost":bool} — process check, no UI walk
//   status  --approve <label>... --deny ... one state snapshot; surface=false means the web
//                                           content is not being served (screen locked, window
//                                           hidden) — report it, never guess
//   press   --label <text>... [--expect-near <card>] [--conversation <marker>] [--expect-mode <mode>]
//   press-exact --label <text>...           one enabled button with exactly that label
//   write   --text <text> [--send-label <label>]
//   focus                                   the ONE deliberate foreground activation
//
// Exit codes: 0 ok · 3 app not running · 4 no match / not found · 5 UIA error · 6 ambiguous or changed.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace VizhiDesktopUia;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const Int32 MaxDepth = 40;
    private const Int32 MaxNodes = 4000;
    private const Int32 ExitNotRunning = 3;
    private const Int32 ExitNoMatch = 4;
    private const Int32 ExitError = 5;
    private const Int32 ExitChanged = 6;

    private static readonly Int32[] CachedProperties =
    {
        UiaIds.ProcessId, UiaIds.ControlType, UiaIds.Name, UiaIds.IsEnabled, UiaIds.AutomationId,
        UiaIds.ClassName, UiaIds.HelpText, UiaIds.NativeWindowHandle, UiaIds.IsOffscreen,
        UiaIds.IsExpandCollapseAvailable, UiaIds.IsInvokeAvailable, UiaIds.IsSelectionItemAvailable,
        UiaIds.IsToggleAvailable, UiaIds.IsValueAvailable, UiaIds.ValueValue, UiaIds.ValueIsReadOnly,
        UiaIds.SelectionItemIsSelected, UiaIds.LegacyDescription, UiaIds.AriaRole, UiaIds.AriaProperties,
        UiaIds.FullDescription,
    };

    private sealed record Target(IntPtr Hwnd, Int32 Pid, String Title);

    private sealed record Scan(List<UiaNode> Nodes, Boolean Complete)
    {
        public Boolean Surface => UiaMatching.HasSurface(this.Nodes, this.Complete);
    }

    private static IUIAutomation? _uia;
    private static IUIAutomation Uia => _uia ??= (IUIAutomation)new CUIAutomation();

    private static Int32 Main(String[] args)
    {
        try
        {
            var verb = args.FirstOrDefault() ?? "help";
            var options = ParseOptions(args.Skip(1).ToArray());

            if (verb is "help" or "--help")
            {
                Console.WriteLine("vizhi-desktop-uia <inspect|frontmost|status|press|press-exact|write|focus> [options]");
                return 0;
            }

            var pids = ProcessIds(Values(options, "--process"));
            if (verb == "frontmost")
            {
                // Passive polling backs off while another app is in front without asking
                // Chromium to build or traverse its accessibility tree.
                return Emit(new Dictionary<String, Object?> { ["frontmost"] = pids.Contains(ForegroundPid()) });
            }

            if (verb == "inspect" && Values(options, "--window").Count == 0 && Values(options, "--process").Count == 0)
            {
                return InspectWindows();
            }

            var (target, error) = FindTarget(pids, Values(options, "--process").Count > 0,
                Values(options, "--window"), options.ContainsKey("--require-process"));
            if (target == null)
            {
                return Fail(error!, error == "ambiguous-app" ? ExitChanged : ExitNotRunning);
            }

            return verb switch
            {
                "inspect" => InspectTarget(target, options.ContainsKey("--all")),
                "status" => Status(target, options),
                "press" => Press(target, options, exact: false),
                "press-exact" => Press(target, options, exact: true),
                "write" => Write(target, options),
                "focus" => Focus(target),
                _ => Fail($"unknown-verb {verb}", ExitNoMatch),
            };
        }
        catch (COMException ex)
        {
            return Fail($"uia-error: 0x{ex.HResult:X8}", ExitError);
        }
        catch (Exception ex)
        {
            return Fail($"uia-error: {ex.GetType().Name}: {ex.Message}", ExitError);
        }
    }

    // ---- arguments ---------------------------------------------------------

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

    // ---- target ------------------------------------------------------------

    private static HashSet<Int32> ProcessIds(IReadOnlyList<String> processNames)
    {
        var pids = new HashSet<Int32>();
        foreach (var raw in processNames)
        {
            var name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    pids.Add(process.Id);
                    process.Dispose();
                }
            }
            catch
            {
                // A process that exits mid-enumeration is not an error.
            }
        }
        return pids;
    }

    /// <summary>
    /// The app's top-level windows a user could switch to, front to back: visible, unowned,
    /// uncloaked, titled, and not a tool window. The app can have more than one open (one per
    /// mode); every read and press is scoped to the one in front, else the one it activated
    /// last, so a control in a background window is never pressed.
    /// </summary>
    private static List<Target> AppWindows(HashSet<Int32> pids)
    {
        var windows = new List<Target>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((Int32)pid) || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero
                || (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0 || IsCloaked(hwnd))
            {
                return true;
            }
            var title = WindowTitle(hwnd);
            if (title.Length > 0)
            {
                windows.Add(new Target(hwnd, (Int32)pid, title));
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static (Target? Target, String? Error) FindTarget(HashSet<Int32> pids, Boolean byProcess,
        IReadOnlyList<String> titles, Boolean requireProcess)
    {
        if (byProcess)
        {
            if (pids.Count == 0)
            {
                return (null, "app-not-running");
            }
            var windows = AppWindows(pids);
            if (windows.Count == 0)
            {
                return (null, "app-not-running");
            }
            var foreground = GetForegroundWindow();
            return (windows.FirstOrDefault(w => w.Hwnd == foreground) ?? windows[0], null);
        }

        if (requireProcess)
        {
            return (null, "identity-unconfirmed");
        }
        if (titles.Count == 0)
        {
            return (null, "app-not-running");
        }

        // Recon only: a browser tab titled like the app would match too, which is why plugin
        // actions must name the process.
        var matches = new List<Target>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }
            var title = WindowTitle(hwnd);
            if (title.Length > 0 && titles.Any(t => String.Equals(title, t, StringComparison.OrdinalIgnoreCase)
                    || title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                matches.Add(new Target(hwnd, (Int32)pid, title));
            }
            return true;
        }, IntPtr.Zero);

        var processes = matches.Select(m => m.Pid).Distinct().ToList();
        if (processes.Count == 0)
        {
            return (null, "app-not-running");
        }
        if (processes.Count > 1)
        {
            return (null, "ambiguous-app");
        }
        return (matches[0], null);
    }

    // ---- scan --------------------------------------------------------------

    /// <summary>
    /// One cross-process call fetches the whole subtree with every property this helper reads
    /// (a cache request scoped to the subtree); the walk below is then in-process. Per-node
    /// property reads, the WPF wrapper's way, cost ~2 s for a 365-node tree on the live app.
    /// </summary>
    private static Scan ScanTarget(Target target)
    {
        var request = Uia.CreateCacheRequest();
        foreach (var id in CachedProperties)
        {
            request.AddProperty(id);
        }
        request.TreeScope = UiaIds.TreeScopeSubtree;
        request.TreeFilter = Uia.RawViewCondition;
        request.AutomationElementMode = UiaIds.ElementModeFull;

        var root = Uia.ElementFromHandleBuildCache(target.Hwnd, request);
        var nodes = new List<UiaNode>();
        var complete = true;

        void Visit(IUIAutomationElement element, Int32 depth)
        {
            if (depth > MaxDepth || nodes.Count >= MaxNodes)
            {
                complete = false;
                return;
            }

            nodes.Add(Read(element, nodes.Count, depth));

            IUIAutomationElementArray? children = null;
            try { children = element.GetCachedChildren(); } catch (COMException) { }
            if (children == null)
            {
                return;
            }
            var count = children.Length;
            for (var i = 0; i < count; i++)
            {
                Visit(children.GetElement(i), depth + 1);
            }
        }

        Visit(root, 0);
        return new Scan(nodes, complete);
    }

    private static UiaNode Read(IUIAutomationElement element, Int32 index, Int32 depth)
    {
        String Str(Int32 id) => element.GetCachedPropertyValue(id) as String ?? "";
        Boolean Flag(Int32 id) => element.GetCachedPropertyValue(id) is Boolean b && b;

        var name = Str(UiaIds.Name);
        var help = Str(UiaIds.HelpText);
        var full = Str(UiaIds.FullDescription);
        var legacy = Str(UiaIds.LegacyDescription);
        var hasValue = Flag(UiaIds.IsValueAvailable);
        var value = hasValue ? Str(UiaIds.ValueValue) : "";

        // Display text, in the macOS helper's order: title, value, description, help.
        var text = name.Length > 0 ? name : value.Length > 0 ? value : help.Length > 0 ? help : full;

        // An icon button can put its only meaningful label in its description or help. Keep
        // every name together so a first non-empty value cannot hide the action's name.
        var labels = new[] { name, help, full, legacy }.Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

        return new UiaNode
        {
            Index = index,
            Role = UiaIds.Role(element.GetCachedPropertyValue(UiaIds.ControlType) is Int32 type ? type : 0),
            Text = text,
            Labels = labels,
            Pressable = Flag(UiaIds.IsInvokeAvailable) || Flag(UiaIds.IsExpandCollapseAvailable)
                || Flag(UiaIds.IsToggleAvailable) || Flag(UiaIds.IsSelectionItemAvailable),
            Enabled = Flag(UiaIds.IsEnabled),
            Depth = depth,
            AriaRole = Str(UiaIds.AriaRole),
            AriaProperties = Str(UiaIds.AriaProperties),
            Value = value,
            HasValue = hasValue,
            ReadOnly = Flag(UiaIds.ValueIsReadOnly),
            Selected = Flag(UiaIds.SelectionItemIsSelected),
            Handle = element,
        };
    }

    // Bounded wait for the web content — used by press/write (which must not act on a half
    // tree), NOT by status (a status poll reports surface=false immediately and cheaply).
    private static Scan? WaitForSurface(Target target, Int32 milliseconds)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            var scan = ScanTarget(target);
            if (scan.Surface)
            {
                return scan;
            }
            Thread.Sleep(200);
        }
        while (deadline.ElapsedMilliseconds < milliseconds);
        return null;
    }

    // ---- verbs -------------------------------------------------------------

    private static Int32 InspectWindows()
    {
        var windows = new List<Dictionary<String, Object?>>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }
            var title = WindowTitle(hwnd);
            if (title.Length == 0)
            {
                return true;
            }
            GetWindowThreadProcessId(hwnd, out var pid);
            windows.Add(new Dictionary<String, Object?>
            {
                ["title"] = title,
                ["pid"] = (Int32)pid,
                ["process"] = ProcessName((Int32)pid),
                ["class"] = WindowClass(hwnd),
                ["owned"] = GetWindow(hwnd, GW_OWNER) != IntPtr.Zero,
                ["toolWindow"] = (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0,
                ["cloaked"] = IsCloaked(hwnd),
            });
            return true;
        }, IntPtr.Zero);
        return Emit(new Dictionary<String, Object?> { ["windows"] = windows });
    }

    // Recon: report controls and activity-shaped nodes, not message text. --all dumps every
    // node for a developer sitting at the machine; the plugin never passes it.
    private static Int32 InspectTarget(Target target, Boolean all)
    {
        var timer = Stopwatch.StartNew();
        var scan = ScanTarget(target);
        var elapsed = timer.ElapsedMilliseconds;
        var interesting = scan.Nodes.Where(n => all || n.Pressable
                || n.Role is "Image" or "ProgressBar" or "Edit" or "Document" or "StatusBar" or "Window" or "Menu")
            .Select(n => new Dictionary<String, Object?>
            {
                ["depth"] = n.Depth,
                ["role"] = n.Role,
                ["text"] = n.Text,
                ["labels"] = n.Labels.ToList(),
                ["pressable"] = n.Pressable,
                ["enabled"] = n.Enabled,
                ["ariaRole"] = n.AriaRole,
                ["ariaProperties"] = n.AriaProperties,
                ["hasValue"] = n.HasValue,
                ["readOnly"] = n.ReadOnly,
                ["selected"] = n.Selected,
            }).ToList();
        return Emit(new Dictionary<String, Object?>
        {
            ["process"] = ProcessName(target.Pid),
            ["title"] = target.Title,
            ["surface"] = scan.Surface,
            ["complete"] = scan.Complete,
            ["nodeCount"] = scan.Nodes.Count,
            ["scanMs"] = (Int32)elapsed,
            ["nodes"] = interesting,
        });
    }

    private static Int32 Status(Target target, Dictionary<String, List<String>> options)
    {
        var scan = ScanTarget(target);
        if (!scan.Surface)
        {
            // Screen locked / window hidden: the tree evaporates. Say so — the monitor must
            // treat this as SURFACE UNAVAILABLE, never as "everything resolved".
            return Emit(new Dictionary<String, Object?> { ["surface"] = false });
        }

        var nodes = scan.Nodes;
        var approve = UiaMatching.FirstPressable(nodes, Values(options, "--approve"));
        var deny = UiaMatching.FirstPressable(nodes, Values(options, "--deny"));
        var stop = UiaMatching.ExactButtons(nodes, Values(options, "--stop")).FirstOrDefault();
        var modePrefix = Value(options, "--mode-prefix") ?? "";
        var mode = UiaMatching.Mode(nodes, modePrefix);

        Boolean Present(String argument) => UiaMatching.FirstPressable(nodes, Values(options, argument)) != null;

        Int32? baseline = null;
        foreach (var entry in Values(options, "--idle-images"))
        {
            var parts = entry.Split('=', 2);
            if (parts.Length == 2 && parts[0] == mode && Int32.TryParse(parts[1], out var count))
            {
                baseline = count;
                break;
            }
        }

        var panelMode = Value(options, "--panel-mode") ?? "";
        return Emit(new Dictionary<String, Object?>
        {
            ["surface"] = true,
            ["attention"] = UiaMatching.Attention(nodes, Value(options, "--attention") ?? ""),
            ["approvalPresent"] = approve != null,
            ["denyPresent"] = deny != null,
            ["stopPresent"] = stop != null,
            ["voiceChat"] = UiaMatching.VoiceState(nodes, Values(options, "--voice-start"), Values(options, "--voice-end")),
            ["canSend"] = UiaMatching.SendTarget(nodes, Value(options, "--send-label") ?? "",
                Values(options, "--stop"), Values(options, "--approve")) != null,
            // Copy Reply is not ported yet: the key must read unavailable, never guess.
            ["canCopyAnswer"] = false,
            ["copyAnswerError"] = "unsupported",
            ["searchPresent"] = Present("--search"),
            ["changesPresent"] = mode.Length > 0 && mode == panelMode && UiaMatching.PanelRouteAvailable(nodes,
                modePrefix + mode, Values(options, "--changes"), Values(options, "--panel-visible")),
            ["projectsPresent"] = Present("--projects"),
            ["pluginsPresent"] = Present("--plugins"),
            ["attachFilesPresent"] = Present("--attach-files"),
            ["permissionsPresent"] = Present("--permissions"),
            ["scheduledPresent"] = Present("--scheduled"),
            ["pullRequestsPresent"] = Present("--pull-requests"),
            ["explorePresent"] = Present("--explore"),
            ["quickChatPresent"] = Present("--quick-chat"),
            ["cardText"] = approve == null ? "" : UiaMatching.CardText(approve, nodes),
            ["mode"] = mode,
            ["conversations"] = UiaMatching.Conversations(nodes, Value(options, "--conv-marker") ?? "",
                Values(options, "--state-awaiting"), Values(options, "--state-unread"),
                Values(options, "--state-running"), baseline),
        });
    }

    private static Int32 Press(Target target, Dictionary<String, List<String>> options, Boolean exact)
    {
        var labels = Values(options, "--label");
        if (labels.Count == 0)
        {
            return Fail("no --label given", ExitNoMatch);
        }

        var scan = WaitForSurface(target, 2000);
        if (scan == null)
        {
            return Fail("surface-unavailable", ExitError);
        }
        var nodes = scan.Nodes;

        var expectedMode = Value(options, "--expect-mode");
        if (expectedMode != null)
        {
            var prefix = Value(options, "--mode-prefix") ?? "";
            var modes = prefix.Length == 0 ? new List<UiaNode>()
                : nodes.Where(n => n.Text.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (modes.Count != 1 || modes[0].Text != prefix + expectedMode)
            {
                return Fail("mode-changed", ExitChanged);
            }
        }

        UiaNode? candidate;
        var marker = Value(options, "--conversation");
        if (marker != null)
        {
            var matches = UiaMatching.ConversationMatches(nodes, labels[0], marker);
            if (matches.Count > 1)
            {
                return Fail("ambiguous-conversation", ExitNoMatch);
            }
            candidate = matches.FirstOrDefault();
        }
        else if (exact)
        {
            candidate = UiaMatching.UniqueEnabledButton(nodes, labels);
        }
        else
        {
            candidate = UiaMatching.FirstPressable(nodes, labels);
        }
        if (candidate == null)
        {
            return Fail("no-match", ExitNoMatch);
        }

        // The expected-card guard: the caller names the card text it SAW; if what is beside
        // the button now is a different card (the old one resolved, a new one appeared between
        // the keypad's render and the thumb), refuse — "card-changed" makes the user look.
        var expected = UiaMatching.Collapse(Value(options, "--expect-near"));
        if (expected.Length > 0)
        {
            var seen = UiaMatching.CardText(candidate, nodes);
            if (seen.Length == 0 || !(seen.Contains(expected, StringComparison.Ordinal)
                || expected.Contains(seen, StringComparison.Ordinal)))
            {
                return Fail("card-changed", ExitChanged);
            }
        }

        if (options.ContainsKey("--dry"))
        {
            return Emit(new Dictionary<String, Object?> { ["matched"] = candidate.Text, ["dry"] = true });
        }

        var before = ProcessName(ForegroundPid());
        if (!Invoke(candidate))
        {
            return Fail("press-failed", ExitError);
        }
        return Emit(new Dictionary<String, Object?>
        {
            ["matched"] = candidate.Text,
            ["frontBefore"] = before,
            ["frontAfter"] = ProcessName(ForegroundPid()),
        });
    }

    /// <summary>
    /// Press WITHOUT focusing the app. Chromium exposes a plain button through Invoke, a
    /// popup button (aria-haspopup) through ExpandCollapse, a pressed-state button through
    /// Toggle and a sidebar row through SelectionItem; a press means the same thing to the
    /// user on all four.
    /// </summary>
    private static Boolean Invoke(UiaNode node)
    {
        if (node.Handle is not IUIAutomationElement element)
        {
            return false;
        }
        if (element.GetCurrentPattern(UiaIds.InvokePattern) is IUIAutomationInvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }
        if (element.GetCurrentPattern(UiaIds.TogglePattern) is IUIAutomationTogglePattern toggle)
        {
            toggle.Toggle();
            return true;
        }
        if (element.GetCurrentPattern(UiaIds.ExpandCollapsePattern) is IUIAutomationExpandCollapsePattern expand)
        {
            if (expand.CurrentExpandCollapseState == UiaIds.Expanded)
            {
                expand.Collapse();
            }
            else
            {
                expand.Expand();
            }
            return true;
        }
        if (element.GetCurrentPattern(UiaIds.SelectionItemPattern) is IUIAutomationSelectionItemPattern select)
        {
            select.Select();
            return true;
        }
        return false;
    }

    private static Int32 Write(Target target, Dictionary<String, List<String>> options)
    {
        var text = Value(options, "--text") ?? "";
        if (text.Length == 0)
        {
            return Fail("no --text given", ExitNoMatch);
        }

        var scan = WaitForSurface(target, 2000);
        if (scan == null)
        {
            return Fail("surface-unavailable", ExitError);
        }
        var composers = UiaMatching.Composers(scan.Nodes);
        if (composers.Count == 0)
        {
            return Fail("no-composer", ExitNoMatch);
        }
        if (composers.Count > 1)
        {
            return Fail("ambiguous-composer", ExitChanged);
        }

        if (composers[0].Handle is not IUIAutomationElement composer
            || composer.GetCurrentPattern(UiaIds.ValuePattern) is not IUIAutomationValuePattern value)
        {
            return Fail("no-composer", ExitNoMatch);
        }
        value.SetValue(text);
        Thread.Sleep(200);
        if (!(value.CurrentValue ?? "").Contains(text, StringComparison.Ordinal))
        {
            return Fail("write-not-applied", ExitError);
        }

        var sent = false;
        var sendLabel = Value(options, "--send-label");
        if (!String.IsNullOrEmpty(sendLabel))
        {
            var latest = ScanTarget(target);
            var send = UiaMatching.SendTarget(latest.Nodes, sendLabel, Values(options, "--stop"), Values(options, "--approve"));
            if (send == null || !Invoke(send))
            {
                return Fail("send-not-found", ExitNoMatch);
            }
            sent = true;
        }

        return Emit(new Dictionary<String, Object?> { ["method"] = "value", ["sent"] = sent });
    }

    private static Int32 Focus(Target target)
    {
        var hwnd = target.Hwnd;
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // Windows refuses SetForegroundWindow for a background helper under the foreground
        // lock. This command is always the direct result of a physical key press, so use the
        // same explicit-user-intent fallback as ClaudeConsoleFocus.
        if (!SetForegroundWindow(hwnd))
        {
            SwitchToThisWindow(hwnd, true);
        }

        // The app may activate a different owned top-level window, so compare process identity
        // instead of demanding the exact handle be foreground. Activation is asynchronous.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(100);
            if (ForegroundPid() == target.Pid)
            {
                return Emit(new Dictionary<String, Object?>());
            }
        }
        return Fail("focus-failed", ExitError);
    }

    // ---- output ------------------------------------------------------------

    private static Int32 Emit(Dictionary<String, Object?> payload, Int32 code = 0)
    {
        payload["ok"] = code == 0;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteJson(writer, payload);
        }
        Console.Out.Write(Encoding.UTF8.GetString(stream.ToArray()));
        Console.Out.WriteLine();
        Console.Out.Flush();
        return code;
    }

    // A hand-rolled writer: the trimmed publish cannot rely on reflection-based serialization
    // of Dictionary<String, Object?>.
    private static void WriteJson(Utf8JsonWriter writer, Object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case String s: writer.WriteStringValue(s); break;
            case Boolean b: writer.WriteBooleanValue(b); break;
            case Int32 i: writer.WriteNumberValue(i); break;
            case Int64 l: writer.WriteNumberValue(l); break;
            case Dictionary<String, Object?> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map)
                {
                    writer.WritePropertyName(key);
                    WriteJson(writer, item);
                }
                writer.WriteEndObject();
                break;
            case Dictionary<String, String> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map)
                {
                    writer.WriteString(key, item);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }

    private static Int32 Fail(String error, Int32 code) =>
        Emit(new Dictionary<String, Object?> { ["error"] = error }, code);

    // ---- Win32 -------------------------------------------------------------

    private const Int32 SW_RESTORE = 9;
    private const UInt32 GW_OWNER = 4;
    private const Int32 GWL_EXSTYLE = -20;
    private const Int64 WS_EX_TOOLWINDOW = 0x80;
    private const Int32 DWMWA_CLOAKED = 14;

    private delegate Boolean EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private static String ProcessName(Int32 pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch { return ""; }
    }

    private static Int32 ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return (Int32)pid;
    }

    private static String WindowTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        var length = GetWindowTextW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    private static String WindowClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    private static Boolean IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(Int32)) == 0 && cloaked != 0;

    [DllImport("user32.dll")]
    private static extern Boolean EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern UInt32 GetWindowThreadProcessId(IntPtr hwnd, out UInt32 processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, UInt32 command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, Int32 index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern Int32 GetWindowTextW(IntPtr hwnd, StringBuilder text, Int32 max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern Int32 GetClassNameW(IntPtr hwnd, StringBuilder text, Int32 max);

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

    [DllImport("dwmapi.dll")]
    private static extern Int32 DwmGetWindowAttribute(IntPtr hwnd, Int32 attribute, out Int32 value, Int32 size);
}
