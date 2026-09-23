namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    internal sealed record DesktopSearchResult(String Title, String Id);

    internal sealed record DesktopSearchSnapshot
    {
        public Boolean Available { get; init; }
        public String Target { get; init; } = "";
        public String Origin { get; init; } = "";
        public String Query { get; init; } = "";
        public String Error { get; init; } = "unsupported";
        public IReadOnlyList<DesktopSearchResult> Results { get; init; } = Array.Empty<DesktopSearchResult>();

        internal static DesktopSearchSnapshot Parse(String json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json ?? "{}");
                var root = doc.RootElement;
                String Read(String key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True || String.IsNullOrEmpty(Read("target")))
                    return new() { Origin = Read("origin"), Error = String.IsNullOrEmpty(Read("error")) ? "unavailable" : Read("error") };
                var results = new List<DesktopSearchResult>();
                if (root.TryGetProperty("results", out var array) && array.ValueKind == JsonValueKind.Array)
                    foreach (var item in array.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                            && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && !String.IsNullOrWhiteSpace(title.GetString()) && !String.IsNullOrEmpty(id.GetString()))
                            results.Add(new(title.GetString(), id.GetString()));
                return new() { Available = true, Error = null, Target = Read("target"), Origin = Read("origin"), Query = Read("query"),
                    Results = results.GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single()).Take(100).ToArray() };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new() { Error = "unavailable" }; }
        }
    }

    /// <summary>One keypad search session. Never shares the composer transcript sink.</summary>
    internal sealed class DesktopSearch
    {
        private readonly IDesktopAutomation _automation;
        private readonly Object _gate = new();
        private volatile String _session;
        private volatile Boolean _ended;
        private Boolean _waitingForField;
        private volatile DesktopSearchSnapshot _current = new();
        private volatile String _feedback;
        public DesktopSearch(IDesktopAutomation automation) => _automation = automation;
        public DesktopSearchSnapshot Current => _ended ? new() : _current;
        public String Feedback => _ended ? null : _feedback;
        public event Action Changed;

        public Boolean Begin()
        {
            lock (_gate)
            {
                OpenLocked();
            }
            Changed?.Invoke();
            return Current.Available;
        }

        public void End()
        {
            // Invalidate immediately, even if a native read still owns the operation lock.
            // Current hides late results until the next Begin acquires that lock and resets it.
            _ended = true; _session = null;
            Changed?.Invoke();
        }

        public void Refresh()
        {
            lock (_gate)
            {
                if (_ended || _session == null || (!_current.Available && !_waitingForField)) return;
                // A probe only observes the original window; it never clicks Search again.
                var next = _current.Available ? _automation.Search("read", _current.Target)
                    : _automation.Search("probe", origin: _current.Origin);
                if (_current.Available || next.Available || !CanAwaitField(next)) _waitingForField = false;
                if (next.Available == _current.Available && next.Target == _current.Target && next.Origin == _current.Origin && next.Query == _current.Query
                    && next.Error == _current.Error && next.Results.SequenceEqual(_current.Results)) return;
                // A lost search surface requires an explicit retry; never attach to a new one.
                _current = next;
            }
            Changed?.Invoke();
        }

        public void TypeQuery()
        {
            lock (_gate)
            {
                if (_ended || _session == null) return;
                _feedback = null;
                if (_current.Available) _current = _automation.Search("focus", _current.Target, _current.Query);
                else OpenLocked();
            }
            Changed?.Invoke();
        }

        public Func<String, String> CaptureSink()
        {
            lock (_gate)
            {
                if (_ended || _session == null || !_current.Available) return null;
                var session = _session;
                var seen = _current;
                _feedback = null;
                return text =>
                {
                    String result;
                    lock (_gate)
                    {
                        if (_ended || _session != session || !_current.Available || _current.Target != seen.Target) return "Cancelled";
                        // Use the query seen at capture START, even when polling sees later typing.
                        var next = _automation.Search("write", seen.Target, seen.Query, text);
                        if (_ended || _session != session) return "Cancelled";
                        _current = next;
                        _feedback = result = next.Available ? null : FeedbackFor(next.Error);
                    }
                    Changed?.Invoke();
                    return result;
                };
            }
        }

        public String ResultParameter(DesktopSearchResult result)
        {
            // Called while rendering: never wait behind a native search operation.
            return "result:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new[] { _ended ? null : _session, Current.Query, result.Id, result.Title })));
        }

        public Boolean Select(String parameter)
        {
            var selected = false;
            lock (_gate)
            {
                if (_ended || _session == null || !_current.Available || parameter?.StartsWith("result:", StringComparison.Ordinal) != true) return false;
                try
                {
                    var values = JsonSerializer.Deserialize<String[]>(Convert.FromBase64String(parameter[7..]));
                    if (values?.Length != 4 || values[0] != _session || values[1] != _current.Query
                        || !_current.Results.Any(r => r.Id == values[2] && r.Title == values[3])) return false;
                    var next = _automation.Search("select", _current.Target, values[1], values[2], values[3]);
                    selected = next.Available;
                    if (selected) { _session = null; _current = new(); }
                    else { _current = next; _feedback = FeedbackFor(next.Error); }
                }
                catch (Exception ex) when (ex is FormatException or JsonException) { return false; }
            }
            Changed?.Invoke();
            return selected;
        }

        public DesktopSearchResult Resolve(String parameter) => Current.Results.FirstOrDefault(r => ResultParameter(r) == parameter);
        public void ShowFeedback(String message) { _feedback = message; Changed?.Invoke(); }
        private void OpenLocked()
        {
            _ended = false;
            _session = Guid.NewGuid().ToString("N"); // revoke old captures even if the app reuses its field
            _feedback = null;
            _current = _automation.Search("open");
            _waitingForField = CanAwaitField(_current);
        }
        internal static Boolean CanAwaitField(DesktopSearchSnapshot state) => !state.Available
            && !String.IsNullOrEmpty(state.Origin)
            && state.Error is "search-field-missing" or "search-container-missing" or "search-field-ambiguous" or "search-button-missing";

        internal static (String Label, String Hint) ProblemFor(String error) => error switch
        {
            "app-not-running" or "app-not-frontmost" => ("Open ChatGPT", "Then retry"),
            "not-trusted" => ("Allow access", "Mac Settings"),
            "mode-changed" => ("Use ChatGPT", "Then retry"),
            "mode-unavailable" => ("Mode unreadable", "Retry"),
            "no-surface" => ("App not ready", "Retry"),
            "search-button-missing" => ("Open app search", "Then wait"),
            "search-field-missing" => ("Search not ready", "Open in app"),
            "search-container-missing" => ("Search layout", "Unsupported"),
            "search-field-ambiguous" => ("Multiple fields", "Check App"),
            "search-target-changed" => ("Search changed", "Retry"),
            "query-changed" => ("Query changed", "Retry"),
            "search-focus-failed" => ("Click search", "In app"),
            "search-field-disabled" or "search-value-unavailable" => ("Search unreadable", "Type in app"),
            "search-write-failed" or "search-write-unconfirmed" => ("Query not typed", "Type in app"),
            "search-open-failed" => ("Search not opened", "Open in app"),
            _ => ("Search unavailable", "Use App"),
        };
        internal static String FeedbackFor(String error) => error switch
        {
            "query-changed" => "Query changed",
            "app-not-frontmost" => "Open App",
            "mode-changed" or "search-target-changed" => "Search changed",
            _ => ProblemFor(error).Hint,
        };
    }
}
