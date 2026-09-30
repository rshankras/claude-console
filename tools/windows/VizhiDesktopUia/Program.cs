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
// UiaMatching.cs, where the test suite can reach them. Verbs are spread over the partial files
// by concern: this one (target, scan, status, press, focus), ComposerVerbs.cs (drafts, writes,
// send, attachments), ContextVerbs.cs (clipboard, screenshots, Copy Reply) and SearchVerbs.cs.
//
// Verbs (each takes --process <exe name>... --require-process; --window <title> is recon only):
//   inspect [--all]                         candidate windows, or the target's control tree
//   frontmost                               {"frontmost":bool} — process check, no UI walk
//   status  --approve <label>... --deny ... one state snapshot; surface=false means the web
//                                           content is not being served (screen locked, window
//                                           hidden) — report it, never guess
//   press   --label <text>... [--expect-near <card>] [--conversation <marker>] [--expect-mode <mode>]
//           -> {"matched":..,"frontMoved":bool,"frontBeforeHwnd":N}: a press is a click to
//           Chromium and a click activates the window; frontMoved says the app took the
//           foreground from another window, and frontBeforeHwnd is what restore-front needs.
//   press-exact --label <text>...           one enabled button with exactly that label
//   restore-front --hwnd <N>                hand the foreground back after a press moved it
//   voice   --action start|end --voice-start <label>... --voice-end <label>...
//   open-panel --expect-mode <m> --mode-prefix <p> --panel-open <label>... --panel-visible <label>...
//   draft-target --mode-prefix <p> --expect-mode <m> [--conv-marker <m>] [--allow-existing]
//           -> {"target":<token>} naming this window, mode, editor and open conversation
//   append-target (same) -> {"target","fingerprint","hasContent"} of the current draft
//   write   --text <text> [--expect-target <token>] [--accept-existing] [--send-label <label>]
//           -> {"method":"value"|"existing","sent":bool}; into an EMPTY composer only
//   append  --text <text> --expect-target <token> --expect-draft <fingerprint> [--accept-existing]
//   send    --send-label <label> [--expect-target <token>] [--expect-text <text>]
//   attach-image --image <png> / attach-files --files <json>   (both need --expect-target)
//   context-selection|clipboard|screenshot|window|return|paste, copy-reply, search: see the
//           other files
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
internal static partial class Program
{
    private const Int32 MaxDepth = 40;
    private const Int32 MaxNodes = 4000;
    private const Int32 ExitNotRunning = 3;
    private const Int32 ExitNoMatch = 4;
    private const Int32 ExitError = 5;
    private const Int32 ExitChanged = 6;

    private static readonly Int32[] CachedProperties =
    {
        UiaIds.RuntimeId, UiaIds.BoundingRectangle, UiaIds.ProcessId, UiaIds.ControlType, UiaIds.Name, UiaIds.IsEnabled, UiaIds.AutomationId,
        UiaIds.ClassName, UiaIds.HelpText, UiaIds.NativeWindowHandle, UiaIds.IsOffscreen,
        UiaIds.IsExpandCollapseAvailable, UiaIds.IsInvokeAvailable, UiaIds.IsSelectionItemAvailable,
        UiaIds.IsToggleAvailable, UiaIds.IsValueAvailable, UiaIds.ValueValue, UiaIds.ValueIsReadOnly,
        UiaIds.SelectionItemIsSelected, UiaIds.LegacyDescription, UiaIds.AriaRole, UiaIds.AriaProperties,
        UiaIds.FullDescription, UiaIds.HasKeyboardFocus,
    };

    private sealed record Target(IntPtr Hwnd, Int32 Pid, String Title);

    private sealed record Scan(List<UiaNode> Nodes, Boolean Complete)
    {
        public Boolean Surface => UiaMatching.HasSurface(this.Nodes, this.Complete);
    }

    private static IUIAutomation? _uia;
    private static IUIAutomation Uia => _uia ??= (IUIAutomation)new CUIAutomation();
    private static HashSet<Int32> _appPids = new();
    private static Dictionary<String, List<String>> _options = new();

    [STAThread]
    private static Int32 Main(String[] args)
    {
        try
        {
            var verb = args.FirstOrDefault() ?? "help";
            var options = ParseOptions(args.Skip(1).ToArray());
            _options = options;

            if (verb is "help" or "--help")
            {
                Console.WriteLine("vizhi-desktop-uia <verb> [options] — see the source header for the verbs");
                return 0;
            }

            var pids = ProcessIds(Values(options, "--process"));
            _appPids = pids;
            if (verb == "frontmost")
            {
                // Passive polling backs off while another app is in front without asking
                // Chromium to build or traverse its accessibility tree.
                return Emit(new Dictionary<String, Object?> { ["frontmost"] = pids.Contains(Win32.ForegroundPid()) });
            }

            if (verb == "restore-front")
            {
                return RestoreFront(Value(options, "--hwnd"), pids);
            }

            if (verb.StartsWith("context-", StringComparison.Ordinal))
            {
                return Context(verb["context-".Length..], options);
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
                "voice" => Voice(target, options),
                "open-panel" => OpenPanel(target, options),
                "draft-target" => DraftTarget(target, options),
                "append-target" => AppendTarget(target, options),
                "write" => Write(target, options, appending: false),
                "append" => Write(target, options, appending: true),
                "send" => Send(target, options),
                "attach-image" => Attach(target, options, image: true),
                "attach-files" => Attach(target, options, image: false),
                "copy-reply" => CopyReply(target, options),
                "search" => Search(target, options),
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
    /// The app's top-level windows a user could switch to, front to back. The app can have
    /// more than one open (one per mode); every read and press is scoped to the one in front,
    /// else the one it activated last, so a control in a background window is never pressed.
    /// </summary>
    private static List<Target> AppWindows(HashSet<Int32> pids) =>
        Win32.TopLevelWindows()
            .Where(hwnd => pids.Contains(Win32.PidOf(hwnd)) && Win32.IsAppWindow(hwnd))
            .Select(hwnd => new Target(hwnd, Win32.PidOf(hwnd), Win32.WindowTitle(hwnd)))
            .ToList();

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
            var foreground = Win32.GetForegroundWindow();
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
        var matches = Win32.TopLevelWindows()
            .Where(Win32.IsWindowVisible)
            .Select(hwnd => new Target(hwnd, Win32.PidOf(hwnd), Win32.WindowTitle(hwnd)))
            .Where(w => w.Title.Length > 0 && titles.Any(t => String.Equals(w.Title, t, StringComparison.OrdinalIgnoreCase)
                || w.Title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
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

    private static Boolean AppIsFrontmost(Target target) => Win32.ForegroundPid() == target.Pid;

    // ---- scan --------------------------------------------------------------

    private static IUIAutomationCacheRequest CacheRequest()
    {
        var request = Uia.CreateCacheRequest();
        foreach (var id in CachedProperties)
        {
            request.AddProperty(id);
        }
        request.TreeScope = UiaIds.TreeScopeSubtree;
        request.TreeFilter = Uia.RawViewCondition;
        request.AutomationElementMode = UiaIds.ElementModeFull;
        return request;
    }

    /// <summary>
    /// One cross-process call fetches the whole subtree with every property this helper reads
    /// (a cache request scoped to the subtree); the walk below is then in-process. Per-node
    /// property reads, the WPF wrapper's way, cost ~2 s for a 365-node tree on the live app.
    /// </summary>
    private static Scan ScanTarget(Target target) => ScanWindow(target.Hwnd);

    private static Scan ScanWindow(IntPtr hwnd)
    {
        var root = Uia.ElementFromHandleBuildCache(hwnd, CacheRequest());
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
            Focused = Flag(UiaIds.HasKeyboardFocus),
            RuntimeId = element.GetCachedPropertyValue(UiaIds.RuntimeId) is Int32[] id ? String.Join(".", id) : "",
            Bounds = element.GetCachedPropertyValue(UiaIds.BoundingRectangle) is Double[] { Length: 4 } rect ? rect : null,
            Handle = element,
        };
    }

    // Bounded wait for the web content — used by the acting verbs (which must not act on a
    // half tree), NOT by status (a status poll reports surface=false immediately and cheaply).
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

    private static Boolean SameElement(UiaNode a, UiaNode b) => a.RuntimeId.Length > 0 && a.RuntimeId == b.RuntimeId;

    // ---- verbs -------------------------------------------------------------

    private static Int32 InspectWindows()
    {
        var windows = Win32.TopLevelWindows().Where(Win32.IsWindowVisible)
            .Select(hwnd => new { Hwnd = hwnd, Title = Win32.WindowTitle(hwnd) })
            .Where(w => w.Title.Length > 0)
            .Select(w => new Dictionary<String, Object?>
            {
                ["title"] = w.Title,
                ["pid"] = Win32.PidOf(w.Hwnd),
                ["process"] = ProcessName(Win32.PidOf(w.Hwnd)),
                ["class"] = Win32.WindowClass(w.Hwnd),
                ["appWindow"] = Win32.IsAppWindow(w.Hwnd),
            }).ToList();
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
                || n.Role is "Image" or "ProgressBar" or "Edit" or "Document" or "StatusBar" or "Window" or "Menu" or "Hyperlink" or "ComboBox")
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
                ["focused"] = n.Focused,
                ["value"] = all && n.Role is "Hyperlink" or "Edit" or "ComboBox" ? n.Value : "",
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
        var reply = UiaMatching.ReplyTarget(nodes, ReplyRules(options));
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
            ["canCopyAnswer"] = reply.Node != null,
            ["copyAnswerError"] = reply.Error,
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

        var beforeWindow = Win32.GetForegroundWindow();
        var before = ProcessName(Win32.ForegroundPid());
        if (!Invoke(candidate))
        {
            return Fail("press-failed", ExitError);
        }

        // Chromium performs a UIA press as a click, and a click activates its window — unlike
        // the macOS AXPress, which leaves the front app alone. Report the move so the caller
        // can hand focus back with restore-front; this process cannot (see there).
        var afterPid = Win32.ForegroundPid();
        var beforePid = Win32.PidOf(beforeWindow);
        return Emit(new Dictionary<String, Object?>
        {
            ["matched"] = candidate.Text,
            ["frontBefore"] = before,
            ["frontAfter"] = ProcessName(afterPid),
            ["frontBeforeHwnd"] = beforeWindow.ToInt64(),
            ["frontMoved"] = beforeWindow != IntPtr.Zero && beforePid != target.Pid && afterPid == target.Pid,
        });
    }

    /// <summary>
    /// Give the foreground back to the window named by --hwnd, as reported by a press's
    /// frontBeforeHwnd. A separate invocation on purpose: measured live, the process that made
    /// the UIA call is refused SetForegroundWindow afterwards and a fresh one is accepted at
    /// once. Touches no UIA and synthesises no input.
    /// </summary>
    private static Int32 RestoreFront(String? handle, HashSet<Int32> appPids)
    {
        if (!Int64.TryParse(handle, out var raw) || raw == 0)
        {
            return Fail("no --hwnd given", ExitNoMatch);
        }
        var previous = new IntPtr(raw);
        if (!Win32.IsWindowVisible(previous))
        {
            return Fail("window-gone", ExitNoMatch);
        }
        if (Win32.GetForegroundWindow() == previous)
        {
            return Emit(new Dictionary<String, Object?> { ["restored"] = false, ["already"] = true });
        }
        // Only hand focus back from the app this helper drives: never move it off whatever
        // else the user has since switched to.
        if (!appPids.Contains(Win32.ForegroundPid()))
        {
            return Emit(new Dictionary<String, Object?> { ["restored"] = false, ["frontElsewhere"] = true });
        }
        return Raise(previous) ? Emit(new Dictionary<String, Object?> { ["restored"] = true }) : Fail("restore-refused", ExitError);
    }

    /// <summary>
    /// Bring a window to the foreground and wait for it to arrive. Every call here is the
    /// direct result of a physical key press, so the user's intent is not in doubt — but
    /// Windows' foreground lock does not know about keypads: while the user is giving input to
    /// the front window, a background process is refused (measured live 2026-09-30: refused on
    /// a key press, accepted from the same helper while the user was idle). A zero-distance
    /// mouse move from this process satisfies the lock; the Alt-tab switch is the last resort.
    /// </summary>
    private static Boolean Raise(IntPtr hwnd)
    {
        if (Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }
        var pid = Win32.PidOf(hwnd);
        Boolean Arrived()
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                Thread.Sleep(100);
                if (Win32.ForegroundPid() == pid)
                {
                    return true;
                }
            }
            return false;
        }

        if (Win32.SetForegroundWindow(hwnd) && Arrived())
        {
            return true;
        }
        Win32.NoOpInput();
        if (Win32.SetForegroundWindow(hwnd) && Arrived())
        {
            return true;
        }
        Win32.SwitchToThisWindow(hwnd, true);
        return Arrived();
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

    private static Int32 Voice(Target target, Dictionary<String, List<String>> options)
    {
        var action = Value(options, "--action") ?? "";
        if (action is not ("start" or "end"))
        {
            return Fail("invalid-voice-action", ExitNoMatch);
        }
        var starts = Values(options, "--voice-start");
        var ends = Values(options, "--voice-end");
        var initial = WaitForSurface(target, 2000);
        if (initial == null)
        {
            return Fail("surface-unavailable", ExitError);
        }
        var candidate = UiaMatching.VoiceTarget(action, initial.Nodes, starts, ends);
        if (candidate == null)
        {
            return Fail("voice-state-changed", ExitChanged);
        }
        // A second look: the same window, the same button.
        var latest = ScanTarget(target);
        var confirmed = latest.Surface && Win32.WindowTitle(target.Hwnd) == target.Title
            ? UiaMatching.VoiceTarget(action, latest.Nodes, starts, ends) : null;
        if (confirmed == null || !SameElement(candidate, confirmed))
        {
            return Fail("voice-state-changed", ExitChanged);
        }
        if (!Invoke(confirmed))
        {
            return Fail("voice-press-failed", ExitError);
        }
        // Request accepted is all we know. Setup dialogs, connection failure, or user
        // cancellation can follow; only later status observations may render an active session.
        return Emit(new Dictionary<String, Object?> { ["requested"] = action });
    }

    private static Int32 OpenPanel(Target target, Dictionary<String, List<String>> options)
    {
        var expectedMode = Value(options, "--expect-mode") ?? "";
        var prefix = Value(options, "--mode-prefix") ?? "";
        var openers = Values(options, "--panel-open");
        var visibleLabels = Values(options, "--panel-visible");
        if (expectedMode.Length == 0 || prefix.Length == 0 || openers.Count == 0 || visibleLabels.Count == 0)
        {
            return Fail("panel-arguments", ExitNoMatch);
        }
        if (!AppIsFrontmost(target) || WaitForSurface(target, 1000) == null)
        {
            return Fail("panel-unavailable", ExitNoMatch);
        }
        var marker = Value(options, "--conv-marker") ?? "";
        var conversation = UiaMatching.SelectedConversation(ScanTarget(target).Nodes, marker);

        (List<UiaNode>? Content, String? Error, Int32 Code) Checked()
        {
            if (!AppIsFrontmost(target)) return (null, "panel-foreground-changed", ExitChanged);
            if (Win32.WindowTitle(target.Hwnd) != target.Title) return (null, "panel-window-changed", ExitChanged);
            var scan = ScanTarget(target);
            if (!scan.Surface) return (null, "panel-surface-missing", ExitChanged);
            if (UiaMatching.SelectedConversation(scan.Nodes, marker) != conversation) return (null, "panel-conversation-changed", ExitChanged);
            var modes = scan.Nodes.Where(n => n.Text.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (modes.Count != 1 || modes[0].Text != prefix + expectedMode) return (null, "mode-changed", ExitChanged);
            if (UiaMatching.PanelObstructed(scan.Nodes)) return (null, "panel-obstructed", ExitNoMatch);
            var owners = UiaMatching.Documents(scan.Nodes).Where(area => area.Any(n => n.Text == prefix + expectedMode)).ToList();
            return owners.Count == 1 ? (owners[0], null, 0) : (null, "panel-not-available", ExitNoMatch);
        }

        Int32 Visible(List<UiaNode> content) => UiaMatching.ExactButtons(content, visibleLabels).Count;

        var (nodes, error, code) = Checked();
        if (nodes == null) return Fail(error!, code);
        if (Visible(nodes) > 1) return Fail("panel-ambiguous", ExitNoMatch);
        if (Visible(nodes) == 1) return Emit(new Dictionary<String, Object?> { ["opened"] = true, ["alreadyOpen"] = true });

        var candidates = UiaMatching.PanelOpeners(nodes, openers);
        if (candidates.Count > 1) return Fail("panel-opener-multiple", ExitNoMatch);
        // A conversation may have no review capability. Absence is not a clean working
        // tree, and must not trigger a guessed shortcut, toggle, or text entry.
        if (candidates.Count != 1) return Fail("panel-not-available", ExitNoMatch);

        var (fresh, freshError, freshCode) = Checked();
        if (fresh == null) return Fail(freshError!, freshCode);
        if (Visible(fresh) == 1) return Emit(new Dictionary<String, Object?> { ["opened"] = true, ["alreadyOpen"] = true });
        var confirmed = UiaMatching.PanelOpeners(fresh, openers);
        if (confirmed.Count != 1 || !SameElement(candidates[0], confirmed[0])) return Fail("panel-target-changed", ExitChanged);
        if (!Invoke(confirmed[0])) return Fail("panel-press-failed", ExitError);

        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 1200)
        {
            Thread.Sleep(80);
            var (again, againError, againCode) = Checked();
            if (again == null) return Fail(againError!, againCode);
            if (Visible(again) == 1)
            {
                return Emit(new Dictionary<String, Object?> { ["opened"] = true, ["alreadyOpen"] = false, ["method"] = "button" });
            }
        }
        return Fail("panel-unconfirmed", ExitError);
    }

    private static Int32 Focus(Target target) =>
        Raise(target.Hwnd) ? Emit(new Dictionary<String, Object?>()) : Fail("focus-failed", ExitError);

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

    private static String ProcessName(Int32 pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch { return ""; }
    }
}
