// Find Chat: the app's own search dialog, driven one step per invocation — open, probe, read,
// focus, write, select — with a token that names the field and its window, so a step never
// acts on a search surface other than the one the keypad is showing.

using System.Diagnostics;
using System.Runtime.Versioning;

namespace VizhiDesktopUia;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    private const Int32 MaxQueryLength = 500;

    private sealed class SearchFailure : Exception
    {
        public SearchFailure(String error, Int32 code = ExitNoMatch) : base(error) { this.Code = code; }
        public Int32 Code { get; }
    }

    private sealed record SearchSurface(UiaNode Field, List<UiaNode> Nodes, String Query, String Token);

    // Per invocation only. A successful open checks mode before a modal may hide its selector.
    private static String? _verifiedSearchOrigin;

    private static String? SearchOrigin(Target target, Dictionary<String, List<String>> options)
    {
        if (Win32.WindowTitle(target.Hwnd) != target.Title || target.Title.Length == 0) return null;
        if (AppWindows(_appPids).Count(w => w.Title == target.Title) != 1) return null;
        var identity = String.Join("\n", target.Pid.ToString(), target.Title, target.Hwnd.ToInt64().ToString(),
            Value(options, "--mode-prefix") ?? "", Value(options, "--expect-mode") ?? "");
        return UiaMatching.Fingerprint(identity);
    }

    private static (SearchSurface? Surface, String Error) InspectSearch(Target target, IReadOnlyList<UiaNode> nodes,
        Dictionary<String, List<String>> options)
    {
        var names = Values(options, "--search-field");
        var fields = Enumerable.Range(0, nodes.Count).Where(i => UiaMatching.IsSearchField(nodes[i], names)).ToList();
        if (fields.Count == 0) return (null, "search-field-missing");
        if (fields.Count > 1) return (null, "search-field-ambiguous");
        var index = fields[0];
        var field = nodes[index];
        if (!field.Enabled) return (null, "search-field-disabled");
        if (!field.HasValue) return (null, "search-value-unavailable");
        var start = UiaMatching.SearchContainer(index, nodes);
        if (start == null) return (null, "search-container-missing");
        var origin = SearchOrigin(target, options);
        if (origin == null) return (null, "search-target-changed");
        var end = UiaMatching.SubtreeEnd(nodes, start.Value);
        var token = UiaMatching.Fingerprint(origin + ":" + field.RuntimeId);
        return (new SearchSurface(field, nodes.Skip(start.Value + 1).Take(end - start.Value - 1).ToList(), field.Value, token), "");
    }

    private static List<(UiaNode Node, String Id, String Title)> Results(SearchSurface surface, Dictionary<String, List<String>> options) =>
        UiaMatching.SearchResults(surface.Nodes, surface.Query, Values(options, "--result-host").Select(h => h.ToLowerInvariant()).ToList(),
            Values(options, "--result-path"));

    private static void CheckSearchMode(Target target, IReadOnlyList<UiaNode> nodes, Boolean pinned, Dictionary<String, List<String>> options)
    {
        var error = UiaMatching.ModeError(UiaMatching.ReportedModes(nodes, Value(options, "--mode-prefix") ?? ""),
            Value(options, "--expect-mode") ?? "", pinned);
        if (error != null) throw new SearchFailure(error);
        _verifiedSearchOrigin = SearchOrigin(target, options);
    }

    private static SearchSurface CheckedSearch(Target target, Dictionary<String, List<String>> options, String? queryOverride = null)
    {
        if (!AppIsFrontmost(target)) throw new SearchFailure("app-not-frontmost");
        var origin = SearchOrigin(target, options);
        if (origin == null) throw new SearchFailure("search-target-changed");
        var expectedOrigin = Value(options, "--origin");
        if (expectedOrigin != null && origin != expectedOrigin) throw new SearchFailure("search-target-changed");
        var scan = ScanTarget(target);
        if (!scan.Surface) throw new SearchFailure("no-surface");
        var (surface, error) = InspectSearch(target, scan.Nodes, options);
        // Tokens are issued only after a mode check and name mode + process + window + field.
        // A modal can hide the mode selector without invalidating that same target.
        var action = Value(options, "--action");
        var pinned = _verifiedSearchOrigin == origin || (action == "probe" && expectedOrigin == origin)
            || (surface != null && Value(options, "--target") == surface.Token);
        CheckSearchMode(target, scan.Nodes, pinned, options);
        if (surface == null) throw new SearchFailure(error);
        var expectedTarget = Value(options, "--target");
        if (expectedTarget != null && surface.Token != expectedTarget) throw new SearchFailure("search-target-changed");
        var query = queryOverride ?? Value(options, "--query");
        if (query != null && surface.Query != query) throw new SearchFailure("query-changed");
        return surface;
    }

    private static Int32 EmitSearch(Target target, SearchSurface surface, Dictionary<String, List<String>> options, Boolean withResults = true) =>
        Emit(new Dictionary<String, Object?>
        {
            ["target"] = surface.Token,
            ["origin"] = SearchOrigin(target, options) ?? "",
            ["query"] = surface.Query,
            ["results"] = withResults
                ? Results(surface, options).Select(r => new Dictionary<String, String> { ["id"] = r.Id, ["title"] = r.Title }).ToList()
                : new List<Dictionary<String, String>>(),
        });

    private static Int32 Search(Target target, Dictionary<String, List<String>> options)
    {
        try
        {
            return SearchSteps(target, options);
        }
        catch (SearchFailure failure)
        {
            // Issue a recovery origin only AFTER mode was verified. An unreadable or wrong mode
            // must never create the evidence a later probe would use to accept a hidden selector.
            var invalidated = failure.Message is "mode-unavailable" or "mode-changed" or "search-target-changed" or "app-not-frontmost" or "no-surface";
            var origin = !invalidated && _verifiedSearchOrigin != null && _verifiedSearchOrigin == SearchOrigin(target, options) ? _verifiedSearchOrigin : "";
            return Emit(new Dictionary<String, Object?> { ["error"] = failure.Message, ["origin"] = origin }, failure.Code);
        }
    }

    private static Int32 SearchSteps(Target target, Dictionary<String, List<String>> options)
    {
        var action = Value(options, "--action") ?? "";
        if (action is not ("open" or "probe" or "read" or "focus" or "write" or "select")) throw new SearchFailure("invalid-search-action");

        // Find Chat is an explicit navigation request: bring the app forward in this same step.
        if (action == "open" && !AppIsFrontmost(target))
        {
            if (AppWindows(_appPids).Count != 1 || !Raise(target.Hwnd)) throw new SearchFailure("app-not-frontmost");
            if (Win32.WindowTitle(target.Hwnd) != target.Title) throw new SearchFailure("search-target-changed");
        }
        if (!AppIsFrontmost(target)) throw new SearchFailure("app-not-frontmost");

        if (action == "open")
        {
            var scan = ScanTarget(target);
            if (!scan.Surface) throw new SearchFailure("no-surface");
            CheckSearchMode(target, scan.Nodes, pinned: false, options);
            var (surface, error) = InspectSearch(target, scan.Nodes, options);
            if (surface == null)
            {
                // An existing field in an unsupported container must not make Retry toggle an
                // already-open search panel. Only press the opener when no field is present.
                if (error != "search-field-missing") throw new SearchFailure(error);
                var button = UiaMatching.UniqueEnabledButton(scan.Nodes, Values(options, "--search"));
                if (button == null) throw new SearchFailure("search-button-missing");
                if (!Invoke(button)) throw new SearchFailure("search-open-failed", ExitError);
                var deadline = Stopwatch.StartNew();
                while (deadline.ElapsedMilliseconds < 1000)
                {
                    Thread.Sleep(100);
                    if (InspectSearch(target, ScanTarget(target).Nodes, options).Surface != null) break;
                }
            }
        }
        else if (action == "probe")
        {
            if (String.IsNullOrEmpty(Value(options, "--origin"))) throw new SearchFailure("missing-search-origin");
        }
        else if (String.IsNullOrEmpty(Value(options, "--target")))
        {
            throw new SearchFailure("missing-search-target");
        }
        if (action is "focus" or "write" or "select" && Value(options, "--query") == null) throw new SearchFailure("missing-search-query");

        var current = CheckedSearch(target, options);
        if (action is "open" or "focus")
        {
            if (!current.Field.Focused && current.Field.Handle is IUIAutomationElement field)
            {
                try { field.SetFocus(); } catch (System.Runtime.InteropServices.COMException) { throw new SearchFailure("search-focus-failed", ExitError); }
            }
            return EmitSearch(target, CheckedSearch(target, options), options);
        }
        if (action == "write")
        {
            var text = Value(options, "--value") ?? "";
            if (String.IsNullOrWhiteSpace(text) || text.Length > MaxQueryLength) throw new SearchFailure("invalid-query");
            if (current.Field.Handle is not IUIAutomationElement field
                || field.GetCurrentPattern(UiaIds.ValuePattern) is not IUIAutomationValuePattern value)
            {
                throw new SearchFailure("search-write-failed", ExitError);
            }
            try { value.SetValue(text); } catch (System.Runtime.InteropServices.COMException) { throw new SearchFailure("search-write-failed", ExitError); }
            // The field reports the new value a few hundred milliseconds later (measured live).
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 1500 && (value.CurrentValue ?? "") != text) Thread.Sleep(50);
            if ((value.CurrentValue ?? "") != text) throw new SearchFailure("search-write-unconfirmed", ExitError);
            var updated = CheckedSearch(target, options, queryOverride: text);
            if (updated.Token != current.Token) throw new SearchFailure("search-write-unconfirmed", ExitError);
            return EmitSearch(target, updated, options);
        }
        if (action == "select")
        {
            var id = Value(options, "--value");
            var title = Value(options, "--title");
            var matches = Results(current, options).Where(r => r.Id == id && r.Title == title).ToList();
            if (matches.Count != 1) throw new SearchFailure("search-result-changed");
            var latest = CheckedSearch(target, options);
            var match = Results(latest, options).FirstOrDefault(r => r.Id == matches[0].Id && r.Title == matches[0].Title);
            if (match.Node == null || !SameElement(match.Node, matches[0].Node)) throw new SearchFailure("search-result-changed");
            if (!Invoke(match.Node)) throw new SearchFailure("search-select-failed", ExitError);
            return EmitSearch(target, current, options, withResults: false);
        }
        return EmitSearch(target, current, options);
    }
}
