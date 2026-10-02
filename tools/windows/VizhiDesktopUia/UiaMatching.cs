// The matching rules, with no UI Automation in them: every function here takes the flat scan and
// returns a decision, so the rules are unit-tested on any platform (tests link this file).
//
// A port of the rules in tools/desktop/VizhiAxBridge.swift, not a reinterpretation. The two
// helpers answer the same questions for the same keys; where a rule changes, change both.
// Role words are UIA's (Button, Text, Edit, Document) where the Swift helper says AXButton,
// AXStaticText, AXTextArea, AXWebArea.

#nullable enable
using System.Text.RegularExpressions;

namespace VizhiDesktopUia;

/// <summary>One element of the scan, in tree order. <see cref="Depth"/> is what lets a flat
/// list recover subtree boundaries.</summary>
internal sealed class UiaNode
{
    public Int32 Index { get; init; }
    public String Role { get; init; } = "";
    public String Text { get; init; } = "";
    public IReadOnlyList<String> Labels { get; init; } = Array.Empty<String>();
    public Boolean Pressable { get; init; }
    public Boolean Enabled { get; init; } = true;
    public Int32 Depth { get; init; }
    public String AriaRole { get; init; } = "";
    public String AriaProperties { get; init; } = "";
    /// <summary>The element's value, where it has one. The composer's draft, never its name.</summary>
    public String Value { get; init; } = "";
    public Boolean HasValue { get; init; }
    public Boolean ReadOnly { get; init; }
    public Boolean Selected { get; init; }
    public Boolean Focused { get; init; }
    /// <summary>Screen rectangle as left, top, width, height; null when the element has none.</summary>
    public Double[]? Bounds { get; init; }
    /// <summary>UIA's runtime id, stable for the element's lifetime: what a draft target is keyed on.</summary>
    public String RuntimeId { get; init; } = "";
    /// <summary>The live element; opaque to everything in this file.</summary>
    public Object? Handle { get; init; }
}

internal static class UiaMatching
{
    public const Int32 CardTextCap = 400;
    public const Int32 MaxConversations = 8;

    private static readonly String[] StatusRoles = { "Text", "Image", "ProgressBar", "StatusBar", "Group" };

    public static String NormalizeLabel(String? label) =>
        String.Join(" ", (label ?? "").Split((Char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    // The card is laid out for a window; consumers get one clean line. Also what makes the
    // card-text comparison stable across reads.
    public static String Collapse(String? text) =>
        String.Join(" ", (text ?? "").Split((Char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>End (exclusive) of the subtree rooted at <paramref name="index"/>.</summary>
    public static Int32 SubtreeEnd(IReadOnlyList<UiaNode> nodes, Int32 index)
    {
        var end = index + 1;
        while (end < nodes.Count && nodes[end].Depth > nodes[index].Depth)
        {
            end++;
        }
        return end;
    }

    /// <summary>
    /// The web content is there, and all of it. Chromium keeps the document element while it
    /// serves no page (screen locked, window covered or minimised), so a document alone is not
    /// a surface: it must have content under it. A truncated scan cannot establish uniqueness
    /// or rule out an approval, so it is not a surface either.
    /// </summary>
    public static Boolean HasSurface(IReadOnlyList<UiaNode> nodes, Boolean complete)
    {
        if (!complete)
        {
            return false;
        }
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Role == "Document" && SubtreeEnd(nodes, i) > i + 1)
            {
                return true;
            }
        }
        return false;
    }

    public static UiaNode? FirstPressable(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels)
    {
        var needles = labels.Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.ToLowerInvariant()).ToList();
        if (needles.Count == 0)
        {
            return null;
        }
        return nodes.FirstOrDefault(n => n.Pressable && n.Text.Length > 0
            && needles.Any(needle => n.Text.ToLowerInvariant().Contains(needle, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Exact button identity: full semantic labels only. Case and whitespace are presentation
    /// differences; substrings and text from descendants are not alternative selectors. A
    /// sidebar row can itself be a button with an arbitrary user title — its nested controls
    /// are what distinguish it from a leaf action button.
    /// </summary>
    public static List<UiaNode> ExactButtons(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels,
        IReadOnlyList<String>? roles = null)
    {
        roles ??= new[] { "Button" };
        var names = labels.Select(NormalizeLabel).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var result = new List<UiaNode>();
        if (names.Count == 0)
        {
            return result;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (!roles.Contains(node.Role) || !node.Labels.Any(label => names.Contains(NormalizeLabel(label))))
            {
                continue;
            }

            var nested = false;
            for (var j = i + 1; j < nodes.Count && nodes[j].Depth > node.Depth; j++)
            {
                if (nodes[j].Pressable)
                {
                    nested = true;
                    break;
                }
            }
            if (!nested)
            {
                result.Add(node);
            }
        }
        return result;
    }

    public static UiaNode? UniqueEnabledButton(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels)
    {
        var matches = ExactButtons(nodes, labels);
        return matches.Count == 1 && matches[0].Pressable && matches[0].Enabled ? matches[0] : null;
    }

    /// <summary>
    /// The card's description: the text nodes directly BEFORE an anchor button in tree order.
    /// One definition, used by status (to report) and press (to verify) — the expected-card
    /// guard is only sound if both sides compute the same string.
    /// </summary>
    public static String CardText(UiaNode anchor, IReadOnlyList<UiaNode> nodes)
    {
        var index = IndexOf(nodes, anchor);
        if (index < 0)
        {
            return "";
        }

        var start = Math.Max(0, index - 20);
        var texts = new List<String>();
        for (var i = start; i < index; i++)
        {
            if (nodes[i].Role == "Text" && nodes[i].Text.Length > 0)
            {
                texts.Add(nodes[i].Text);
            }
        }
        var text = Collapse(String.Join(" ", texts.Skip(Math.Max(0, texts.Count - 3))));
        return text.Length > CardTextCap ? text[..CardTextCap] : text;
    }

    public static Int32 IndexOf(IReadOnlyList<UiaNode> nodes, UiaNode node)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                return i;
            }
        }
        return -1;
    }

    public static String Mode(IReadOnlyList<UiaNode> nodes, String prefix)
    {
        if (prefix.Length == 0)
        {
            return "";
        }
        var match = nodes.FirstOrDefault(n => n.Text.StartsWith(prefix, StringComparison.Ordinal));
        return match == null ? "" : match.Text[prefix.Length..];
    }

    public static Boolean Attention(IReadOnlyList<UiaNode> nodes, String marker)
    {
        var needle = marker.ToLowerInvariant();
        return needle.Length > 0 && nodes.Any(n => n.Text.ToLowerInvariant().Contains(needle, StringComparison.Ordinal));
    }

    // The baseline comes from verified app/mode controls, never from possibly-running peers.
    public static String ConversationState(String state, Int32 images, Int32? baseline) =>
        state == "idle" && baseline is Int32 known && known >= 0 && images > known ? "running" : state;

    /// <summary>
    /// Read only a verified sidebar row's descendants, never message text or the row's own
    /// title. Exact normalized matches avoid treating a chat titled "Thinking about travel"
    /// as busy.
    /// </summary>
    public static String ConversationRowState(String title, IEnumerable<UiaNode> descendants,
        IReadOnlyList<String> awaiting, IReadOnlyList<String> unread, IReadOnlyList<String> running, Int32? baseline)
    {
        var rows = descendants.ToList();
        var titleLabel = NormalizeLabel(title);
        var labels = rows.Where(n => StatusRoles.Contains(n.Role))
            .SelectMany(n => new[] { n.Text }.Concat(n.Labels))
            .Select(NormalizeLabel)
            .Where(x => x.Length > 0 && x != titleLabel)
            .ToHashSet(StringComparer.Ordinal);

        Boolean Matches(IReadOnlyList<String> candidates) =>
            candidates.Any(c => NormalizeLabel(c).Length > 0 && labels.Contains(NormalizeLabel(c)));

        if (Matches(awaiting)) return "awaiting";
        if (Matches(unread)) return "unread";
        if (Matches(running)) return "running";

        // The verified legacy unnamed-spinner fallback, scoped to this row and mode.
        var images = rows.Count(n => n.Role == "Image" && n.Text.Length == 0);
        return ConversationState("idle", images, baseline);
    }

    /// <summary>
    /// aria-current="page" marks the open conversation. An explicit non-current value must not
    /// borrow selection from a parent; the selection pattern is only the fallback for an app
    /// that reports no aria-current at all.
    /// </summary>
    public static Boolean IsCurrent(UiaNode node)
    {
        foreach (var pair in node.AriaProperties.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() == "current")
            {
                return NormalizeLabel(parts[1]) is "page" or "true";
            }
        }
        return node.Selected;
    }

    /// <summary>
    /// Rows that carry the adapter's sidebar marker in their own subtree. A chat that belongs
    /// to a project is listed twice — under its project, where its row sits inside the
    /// project's own list item, and again in Recents — and both rows are marked current. The
    /// copy under the project is dropped whenever Recents lists the same title, so one chat is
    /// one row; a project chat Recents no longer shows keeps its only row.
    /// </summary>
    public static IEnumerable<(UiaNode Row, Int32 Index, Int32 End)> ConversationRows(
        IReadOnlyList<UiaNode> nodes, String marker)
    {
        if (marker.Length == 0)
        {
            return Enumerable.Empty<(UiaNode, Int32, Int32)>();
        }
        var rows = new List<(UiaNode Row, Int32 Index, Int32 End, Boolean InProject)>();
        var i = 0;
        while (i < nodes.Count)
        {
            var node = nodes[i];
            if (!node.Pressable || node.Text.Length == 0)
            {
                i++;
                continue;
            }

            var end = SubtreeEnd(nodes, i);
            var hasMarker = false;
            for (var j = i + 1; j < end; j++)
            {
                if (nodes[j].Pressable && nodes[j].Text == marker)
                {
                    hasMarker = true;
                    break;
                }
            }

            if (hasMarker)
            {
                rows.Add((node, i, end, ListItemAncestors(nodes, i) > 1));
                i = end;        // skip the subtree so row controls never read as items
            }
            else
            {
                i++;
            }
        }
        var listed = rows.Where(r => !r.InProject).Select(r => r.Row.Text).ToHashSet(StringComparer.Ordinal);
        return rows.Where(r => !r.InProject || !listed.Contains(r.Row.Text)).Select(r => (r.Row, r.Index, r.End));
    }

    private static Int32 ListItemAncestors(IReadOnlyList<UiaNode> nodes, Int32 index)
    {
        var count = 0;
        var depth = nodes[index].Depth;
        for (var i = index - 1; i >= 0 && depth > 0; i--)
        {
            if (nodes[i].Depth >= depth) continue;
            depth = nodes[i].Depth;
            if (nodes[i].Role == "ListItem") count++;
        }
        return count;
    }

    public static List<Dictionary<String, String>> Conversations(IReadOnlyList<UiaNode> nodes, String marker,
        IReadOnlyList<String> awaiting, IReadOnlyList<String> unread, IReadOnlyList<String> running, Int32? baseline)
    {
        var result = new List<Dictionary<String, String>>();
        foreach (var (row, index, end) in ConversationRows(nodes, marker))
        {
            if (result.Count >= MaxConversations)
            {
                break;
            }
            var descendants = nodes.Skip(index + 1).Take(end - index - 1);
            result.Add(new Dictionary<String, String>
            {
                ["title"] = row.Text,
                ["state"] = ConversationRowState(row.Text, descendants, awaiting, unread, running, baseline),
                ["selected"] = IsCurrent(row) ? "true" : "false",
            });
        }
        return result;
    }

    // Exact titles only, and only rows with the adapter's sidebar marker in their subtree.
    public static List<UiaNode> ConversationMatches(IReadOnlyList<UiaNode> nodes, String title, String marker)
    {
        if (title.Length == 0 || marker.Length == 0)
        {
            return new List<UiaNode>();
        }
        return ConversationRows(nodes, marker).Where(x => x.Row.Text == title).Select(x => x.Row).ToList();
    }

    /// <summary>
    /// The one button that starts a voice chat. The Windows app shows two at once — the
    /// composer's "Start voice chat" and the sidebar's "Start new voice chat" — so the
    /// adapter's labels are tried in order and the first label naming exactly one enabled
    /// button wins; a label naming two is still ambiguous. (The macOS rule takes all labels
    /// together, where the app shows one.)
    /// </summary>
    public static UiaNode? VoiceStartButton(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> start)
    {
        foreach (var label in start)
        {
            var matches = ExactButtons(nodes, new[] { label });
            if (matches.Count == 1)
            {
                return matches[0].Pressable && matches[0].Enabled ? matches[0] : null;
            }
            if (matches.Count > 1)
            {
                return null;
            }
        }
        return null;
    }

    public static String VoiceState(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> start, IReadOnlyList<String> end)
    {
        if (start.Count == 0 || end.Count == 0)
        {
            return "unavailable";
        }
        // Even a disabled End button is evidence of a session. It must block starting another.
        var endings = ExactButtons(nodes, end);
        if (endings.Count > 0)
        {
            return endings.Count == 1 ? "active" : "unavailable";
        }
        return VoiceStartButton(nodes, start) != null ? "ready" : "unavailable";
    }

    public static UiaNode? VoiceTarget(String action, IReadOnlyList<UiaNode> nodes,
        IReadOnlyList<String> start, IReadOnlyList<String> end)
    {
        if (action is not ("start" or "end")
            || VoiceState(nodes, start, end) != (action == "start" ? "ready" : "active"))
        {
            return null;
        }
        return action == "start" ? VoiceStartButton(nodes, start) : UniqueEnabledButton(nodes, end);
    }

    public static List<UiaNode> Composers(IReadOnlyList<UiaNode> nodes) =>
        nodes.Where(n => n.Role == "Edit" && n.HasValue && !n.ReadOnly).ToList();

    /// <summary>
    /// Send is never a substring search over the entire window. Require exactly one composer,
    /// a non-empty draft, and an enabled exact Send button in a shared local container. The
    /// window and the document are too broad to establish that relationship.
    /// </summary>
    public static UiaNode? SendTarget(IReadOnlyList<UiaNode> nodes, String sendLabel,
        IReadOnlyList<String> stop, IReadOnlyList<String> approve)
    {
        var composers = Composers(nodes);
        if (sendLabel.Length == 0 || composers.Count != 1 || String.IsNullOrWhiteSpace(composers[0].Value)
            || ExactButtons(nodes, stop).Count > 0 || FirstPressable(nodes, approve) != null)
        {
            return null;
        }

        var index = IndexOf(nodes, composers[0]);
        var depth = nodes[index].Depth;
        for (var i = index - 1; i >= 0; i--)
        {
            if (nodes[i].Depth >= depth)
            {
                continue;
            }

            var ancestor = nodes[i];
            if (ancestor.Role is "Window" or "Document")
            {
                return null;
            }
            depth = ancestor.Depth;
            var matches = new List<UiaNode>();
            for (var j = i + 1; j < SubtreeEnd(nodes, i); j++)
            {
                if (nodes[j].Pressable && nodes[j].Text == sendLabel)
                {
                    matches.Add(nodes[j]);
                }
            }
            if (matches.Count > 0)
            {
                return matches.Count == 1 && matches[0].Enabled ? matches[0] : null;
            }
        }
        return null;
    }

    // Rich editors can expose an empty or trailing paragraph through their value. Ignore only
    // outer whitespace for eligibility and readback; never strip visible content.
    public static String ComparableDraft(String? value) => (value ?? "").Trim();

    public static String Fingerprint(String? value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ComparableDraft(value)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// After an append: the value ends with the addition, and what precedes it is the original
    /// (by fingerprint) followed by a real line break. The editor may render the paragraph gap
    /// as one newline or two; the count is not the test, the original content is.
    /// </summary>
    public static Boolean AppendedDraftMatches(String value, String before, String addition)
    {
        value = ComparableDraft(value);
        addition = ComparableDraft(addition);
        if (addition.Length == 0 || !value.EndsWith(addition, StringComparison.Ordinal))
        {
            return false;
        }
        var prefix = value[..^addition.Length];
        if (before != Fingerprint("") && !(prefix.EndsWith('\n') || prefix.EndsWith('\r')))
        {
            return false;
        }
        return Fingerprint(prefix) == before;
    }

    /// <summary>
    /// A value that spells one of the adapter's composer hints, with no enabled Send beside
    /// it, is the empty composer's hint rendered into its value, not a draft. A literal draft
    /// spelling the same words has Send enabled and survives.
    /// </summary>
    public static Boolean IsPlaceholderDraft(String value, IReadOnlyList<String> placeholders, Boolean sendEnabled) =>
        !sendEnabled && placeholders.Contains(ComparableDraft(value), StringComparer.Ordinal);

    /// <summary>
    /// The mode labels the window reports, from controls only. Duplicate reports of one mode
    /// are harmless; conflicting modes in one window are not.
    /// </summary>
    public static HashSet<String> ReportedModes(IReadOnlyList<UiaNode> nodes, String prefix)
    {
        var values = new HashSet<String>(StringComparer.Ordinal);
        if (prefix.Length == 0)
        {
            return values;
        }
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var control = node.Role is "Button" or "SplitButton" or "MenuItem" or "ComboBox";
            if (!control && !(node.Role == "Group" && node.Pressable))
            {
                continue;
            }
            var labels = new List<String> { node.Text };
            labels.AddRange(node.Labels);
            if (control)
            {
                for (var j = i + 1; j < nodes.Count && nodes[j].Depth > node.Depth; j++)
                {
                    if (nodes[j].Role == "Text")
                    {
                        labels.Add(nodes[j].Text);
                    }
                }
            }
            foreach (var label in labels)
            {
                if (label.StartsWith(prefix, StringComparison.Ordinal))
                {
                    values.Add(label[prefix.Length..]);
                }
            }
        }
        return values;
    }

    /// <summary>Null when the expected mode is the one (and only) mode the window reports.</summary>
    public static String? ModeError(HashSet<String> modes, String expected, Boolean pinned)
    {
        if (expected.Length == 0)
        {
            return "mode-unavailable";
        }
        if (modes.Count == 0)
        {
            return pinned ? null : "mode-unavailable";
        }
        return modes.Count == 1 && modes.Contains(expected) ? null : "mode-changed";
    }

    /// <summary>
    /// The open conversation's identity, so a reused editor cannot silently receive a brief
    /// after sidebar navigation. Empty when the app exposes none; null when it exposes more
    /// than one, which no caller may act on.
    /// </summary>
    public static String? SelectedConversation(IReadOnlyList<UiaNode> nodes, String marker)
    {
        var selected = ConversationRows(nodes, marker).Where(r => IsCurrent(r.Row))
            .Select(r => r.Row.Text + ":" + r.Row.RuntimeId).ToList();
        return selected.Count > 1 ? null : selected.FirstOrDefault() ?? "";
    }

    /// <summary>A modal, or a task that is running or waiting, means the composer is not ready.</summary>
    public static Boolean ComposerBlocked(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> blockingLabels) =>
        nodes.Any(IsBlockingDialog) || ExactButtons(nodes, blockingLabels).Any(n => n.Enabled);

    // ---- Copy Reply ------------------------------------------------------------
    //
    // Speaker headings and the response-only Copy control are adapter-owned semantics. Never
    // use selected text, the last generic Copy button, or scrape message text.

    public sealed record ReplyRules(
        IReadOnlyList<String> AssistantHeadings, IReadOnlyList<String> UserHeadings,
        IReadOnlyList<String> CopyResponse, IReadOnlyList<String> CopyButton, IReadOnlyList<String> CopyCompleted,
        IReadOnlyList<String> ResponseActions, IReadOnlyList<String> Stop, IReadOnlyList<String> VoiceEnd,
        IReadOnlyList<String> Approve, String ConversationMarker, IReadOnlyList<String> Awaiting, IReadOnlyList<String> Running);

    private static readonly String[] ActionRoles = { "Button", "SplitButton" };

    public static Boolean IsHeading(UiaNode node) => node.AriaRole == "heading";

    public static String SpeakerLabel(Int32 index, IReadOnlyList<UiaNode> nodes)
    {
        var heading = nodes[index];
        if (!IsHeading(heading))
        {
            return "";
        }
        var text = heading.Text;
        if (text.Length == 0)
        {
            var parts = new List<String>();
            for (var i = index + 1; i < SubtreeEnd(nodes, index); i++)
            {
                if (nodes[i].Role == "Text") parts.Add(nodes[i].Text);
            }
            text = String.Join(" ", parts);
        }
        return NormalizeLabel(text);
    }

    /// <summary>
    /// The run of sibling controls a Copy sits in. Web accessibility may omit ordinary div
    /// wrappers, so a contiguous run of sibling controls counts as well as a named group.
    /// Message text, headings and the composer terminate the run: a code Copy cannot borrow
    /// a response action across that content boundary.
    /// </summary>
    public static List<UiaNode> ReplyActionRun(Int32 index, Int32 heading, IReadOnlyList<UiaNode> nodes)
    {
        if (index <= heading) return new List<UiaNode>();
        var parent = -1;
        for (var i = index - 1; i >= 0; i--)
        {
            if (nodes[i].Depth < nodes[index].Depth) { parent = i; break; }
        }
        if (parent < 0 || nodes[parent].Role is not ("Group" or "Document")) return new List<UiaNode>();
        var end = SubtreeEnd(nodes, parent);
        var peers = Enumerable.Range(parent + 1, end - parent - 1).Where(i => nodes[i].Depth == nodes[index].Depth).ToList();
        var position = peers.IndexOf(index);
        if (position < 0) return new List<UiaNode>();
        Boolean Control(Int32 i) => peers[i] > heading && ActionRoles.Contains(nodes[peers[i]].Role);
        var first = position;
        var last = position;
        while (first > 0 && Control(first - 1)) first--;
        while (last + 1 < peers.Count && Control(last + 1)) last++;
        var stop = last + 1 < peers.Count ? peers[last + 1] : end;
        return nodes.Skip(peers[first]).Take(stop - peers[first]).ToList();
    }

    /// <summary>
    /// A fallback for independently wrapped footer controls (each button in its own group, as
    /// the live Windows app lays them out): THREE distinct icon buttons — Copy plus two
    /// different response actions — aligned in one compact horizontal row. Coordinates
    /// establish their relationship only; activation still goes through the button.
    /// </summary>
    public static Boolean SameReplyControlRow(IReadOnlyList<Double[]> frames)
    {
        if (frames.Count != 3 || frames.Any(f => f.Length != 4 || f.Any(v => !Double.IsFinite(v))
            || f[2] < 8 || f[2] > 96 || f[3] < 8 || f[3] > 96))
        {
            return false;
        }
        var height = frames.Min(f => f[3]);
        if (frames.Max(f => f[3]) > height * 1.5) return false;
        var midY = frames.Select(f => f[1] + f[3] / 2).ToList();
        if (midY.Max() - midY.Min() > height * 0.25) return false;
        var ordered = frames.OrderBy(f => f[0]).ToList();
        if (ordered[0][0] + ordered[0][2] > ordered[1][0] || ordered[1][0] + ordered[1][2] > ordered[2][0]) return false;
        return ordered[2][0] + ordered[2][2] - ordered[0][0] <= height * 8;
    }

    public static Boolean PositionedReplyRow(UiaNode candidate, IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> responseActions)
    {
        if (candidate.Bounds == null) return false;
        var actions = ExactButtons(nodes, responseActions, ActionRoles).Where(n => !ReferenceEquals(n, candidate) && n.Bounds != null).ToList();
        if (actions.Count < 2) return false;
        var known = responseActions.Select(NormalizeLabel).ToHashSet(StringComparer.Ordinal);
        HashSet<String> Semantics(UiaNode node) => node.Labels.Select(NormalizeLabel).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        for (var first = 0; first < actions.Count - 1; first++)
        {
            for (var second = first + 1; second < actions.Count; second++)
            {
                if (Semantics(actions[first]).Overlaps(Semantics(actions[second]))) continue;
                if (SameReplyControlRow(new[] { candidate.Bounds, actions[first].Bounds!, actions[second].Bounds! })) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A diff or preview can expose another document in the same window. Identify the
    /// conversation by its speaker headings, not by which document happens to have Copy.
    /// </summary>
    public static List<UiaNode>? ReplyConversationNodes(IReadOnlyList<UiaNode> nodes, ISet<String> speakerLabels)
    {
        var areas = Documents(nodes);
        if (areas.Count == 1) return areas[0];
        var conversations = areas.Where(area => Enumerable.Range(0, area.Count).Any(i => speakerLabels.Contains(SpeakerLabel(i, area)))).ToList();
        return conversations.Count == 1 ? conversations[0] : null;
    }

    public static (UiaNode? Node, String Error) ReplyTarget(IReadOnlyList<UiaNode> windowNodes, ReplyRules rules, UiaNode? copiedTarget = null)
    {
        var assistants = rules.AssistantHeadings.Select(NormalizeLabel).ToList();
        var users = rules.UserHeadings.Select(NormalizeLabel).ToList();
        if (assistants.Count == 0 || users.Count == 0 || rules.CopyResponse.Count == 0) return (null, "unsupported");
        if (!windowNodes.Any(n => n.Role == "Document")) return (null, "reply-web-area-missing");
        if (windowNodes.Any(IsBlockingDialog)) return (null, "reply-dialog-open");
        if (ExactButtons(windowNodes, rules.Stop.Concat(rules.VoiceEnd).ToList()).Count > 0
            || FirstPressable(windowNodes, rules.Approve) != null) return (null, "answer-not-ready");
        if (rules.ConversationMarker.Length > 0)
        {
            var selected = 0;
            foreach (var (row, index, end) in ConversationRows(windowNodes, rules.ConversationMarker))
            {
                if (!IsCurrent(row)) continue;
                selected++;
                var state = ConversationRowState(row.Text, windowNodes.Skip(index + 1).Take(end - index - 1),
                    rules.Awaiting, Array.Empty<String>(), rules.Running, null);
                if (state is "running" or "awaiting") return (null, "answer-not-ready");
            }
            if (selected > 1) return (null, "reply-selection-multiple");
        }
        var speakers = new HashSet<String>(assistants.Concat(users), StringComparer.Ordinal);
        var nodes = ReplyConversationNodes(windowNodes, speakers);
        if (nodes == null) return (null, "reply-web-area-multiple");

        (Int32 Index, Boolean Assistant)? lastSpeaker = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!IsHeading(nodes[i])) continue;
            var label = SpeakerLabel(i, nodes);
            if (assistants.Contains(label)) lastSpeaker = (i, true);
            else if (users.Contains(label)) lastSpeaker = (i, false);
        }
        if (lastSpeaker == null) return (null, "reply-unrecognized");
        var latest = lastSpeaker.Value;
        if (!latest.Assistant) return (null, "no-answer");

        // No fallback to an earlier answer when the newest turn has no completed copy action.
        var tail = nodes.Skip(latest.Index + 1).ToList();
        var matches = ExactButtons(tail, rules.CopyResponse);
        Boolean Known(UiaNode candidate) => matches.Any(m => ReferenceEquals(m, candidate));
        // Some app versions expose the response-specific text only as a tooltip. A generic
        // Copy is accepted only in the same small action row as an adapter-owned sibling action.
        // A code-block Copy would have to climb past the message heading, and is rejected.
        foreach (var candidate in ExactButtons(tail, rules.CopyButton))
        {
            if (Known(candidate)) continue;
            var index = IndexOf(nodes, candidate);
            if (index < 0) continue;
            var run = ReplyActionRun(index, latest.Index, nodes);
            if (ExactButtons(run, rules.CopyButton).Count == 1 && ExactButtons(run, rules.ResponseActions, ActionRoles).Count > 0)
            {
                matches.Add(candidate);
                continue;
            }
            var controlLabels = rules.CopyButton.Concat(rules.CopyCompleted).Concat(rules.ResponseActions)
                .Select(NormalizeLabel).ToHashSet(StringComparer.Ordinal);
            for (var parent = index - 1; parent >= 0; parent--)
            {
                if (nodes[parent].Depth >= candidate.Depth) continue;
                var end = SubtreeEnd(nodes, parent);
                if (end <= index) continue;
                var scope = nodes.Skip(parent).Take(end - parent).ToList();
                if (scope.Any(n => IsHeading(n) || n.Role is "Edit" or "Document")) break;
                // The heading can be outside the Markdown container. Do not let a code Copy
                // climb through response text to borrow More actions from a different footer.
                if (scope.Any(n => n.Role == "Text" && n.Text.Length > 0 && !controlLabels.Contains(NormalizeLabel(n.Text)))) break;
                if (nodes[parent].Role == "Group" && ExactButtons(scope, rules.CopyButton).Count == 1
                    && ExactButtons(scope, rules.ResponseActions, ActionRoles).Count > 0)
                {
                    matches.Add(candidate);
                }
                break;
            }
            if (!Known(candidate) && PositionedReplyRow(candidate, tail, rules.ResponseActions))
            {
                matches.Add(candidate);
            }
        }
        // The same pressed button changes its accessible label to Copied for two seconds.
        // This is acknowledgement only, never an alternative target for a fresh request.
        if (copiedTarget != null)
        {
            var acknowledged = ExactButtons(tail, rules.CopyCompleted).FirstOrDefault(n => n.RuntimeId == copiedTarget.RuntimeId);
            if (acknowledged != null && !matches.Any(m => m.RuntimeId == copiedTarget.RuntimeId)) matches.Add(acknowledged);
        }
        if (matches.Count > 1) return (null, "reply-copy-multiple");
        if (matches.Count == 0)
        {
            // Report which selector condition failed, without emitting labels, message text
            // or element identities.
            var names = rules.CopyResponse.Concat(rules.CopyButton).Select(NormalizeLabel).ToHashSet(StringComparer.Ordinal);
            Boolean NamedCopy(UiaNode node) => (node.Pressable || ActionRoles.Contains(node.Role))
                && node.Labels.Any(l => names.Contains(NormalizeLabel(l)));
            var named = tail.Where(NamedCopy).ToList();
            if (named.Count == 0) return (null, nodes.Any(NamedCopy) ? "reply-copy-outside-latest" : "reply-copy-not-found");
            if (!named.Any(n => n.Role == "Button")) return (null, "reply-copy-wrong-role");
            if (ExactButtons(tail, rules.CopyResponse.Concat(rules.CopyButton).ToList()).Count == 0) return (null, "reply-copy-nested-control");
            if (ExactButtons(tail, rules.ResponseActions, ActionRoles).Count == 0) return (null, "reply-action-not-found");
            return (null, "reply-action-row-unrecognized");
        }
        var target = matches[0];
        return target.Pressable && target.Enabled ? (target, "") : (null, "answer-not-ready");
    }

    // ---- Find Chat ------------------------------------------------------------
    //
    // Search never uses the message composer, arbitrary pressable rows, or Return. Unknown
    // layouts are deliberately unsupported. All discovery stays inside one dialog.

    public static String NormalizeSearchLabel(String label) => NormalizeLabel(label).Trim('.', '…', ' ');

    public static Boolean IsSearchField(UiaNode node, IReadOnlyList<String> names)
    {
        if (node.Role is not ("Edit" or "ComboBox")) return false;
        if (node.AriaRole == "searchbox") return true;
        var expected = names.Select(NormalizeSearchLabel).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        return node.Labels.Concat(new[] { node.Text }).Any(l => NormalizeSearchLabel(l).Length > 0 && expected.Contains(NormalizeSearchLabel(l)));
    }

    /// <summary>The dialog or search landmark the field sits in; the window is the hard boundary.</summary>
    public static Int32? SearchContainer(Int32 index, IReadOnlyList<UiaNode> nodes)
    {
        var depth = nodes[index].Depth;
        for (var i = index - 1; i >= 0; i--)
        {
            if (nodes[i].Depth >= depth) continue;
            depth = nodes[i].Depth;
            if (IsDialog(nodes[i]) || nodes[i].AriaRole == "search") return i;
            if (nodes[i].Role == "Window") break;
        }
        return null;
    }

    /// <summary>
    /// A result's id: a conversation link's canonical URL, or — the Windows app lists results
    /// as pressable list items with no URL — the item's title, fingerprinted. Duplicate titles
    /// are dropped by the caller, so an id always names one item.
    /// </summary>
    public static String? SearchResultId(UiaNode node, IReadOnlyList<String> hosts, IReadOnlyList<String> paths)
    {
        if (!node.Pressable || String.IsNullOrWhiteSpace(node.Text)) return null;
        if (node.Role == "ListItem")
        {
            return "item:" + Fingerprint(node.Text)[..16];
        }
        if (node.Role != "Hyperlink" || node.Value.Length == 0) return null;
        if (!Uri.TryCreate(node.Value, UriKind.RelativeOrAbsolute, out var url)) return null;
        String path;
        if (url.IsAbsoluteUri)
        {
            if (url.Scheme != "https" || !hosts.Contains(url.Host.ToLowerInvariant()) || url.Query.Length > 0 || url.Fragment.Length > 0) return null;
            path = url.AbsolutePath;
        }
        else
        {
            if (!node.Value.StartsWith('/') || node.Value.Contains('?') || node.Value.Contains('#')) return null;
            path = node.Value;
        }
        var matches = paths.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)
            && path.Length > prefix.Length && !path[prefix.Length..].Contains('/'));
        return matches ? node.Value : null;
    }

    /// <summary>
    /// A result's title for the key: a list item's accessible name runs the chat title, its
    /// project, a shortcut and a snippet together, so the item's first text — the title the
    /// sidebar shows — is what the key displays. The app wraps the title in a group of its own
    /// and splits it around the words the query matched ("Add third line to ", "notes", ".txt"),
    /// so the texts beside the first one in that group are the rest of the title. A link keeps
    /// its own text.
    /// </summary>
    public static String SearchResultTitle(UiaNode node, IReadOnlyList<UiaNode> nodes)
    {
        if (node.Role != "ListItem") return node.Text;
        var index = IndexOf(nodes, node);
        if (index < 0) return node.Text;
        var end = SubtreeEnd(nodes, index);
        for (var i = index + 1; i < end; i++)
        {
            var first = nodes[i];
            if (first.Role != "Text" || String.IsNullOrWhiteSpace(first.Text)) continue;
            // Directly under the item there is no wrapper to say where the title stops.
            if (first.Depth <= node.Depth + 1) return first.Text;
            return String.Concat(nodes.Skip(i).Take(end - i)
                .TakeWhile(n => n.Role == "Text" && n.Depth == first.Depth).Select(n => n.Text)).Trim();
        }
        return node.Text;
    }

    /// <summary>The heading a list item sits under inside its list; empty when it has none.</summary>
    public static String ListGroup(IReadOnlyList<UiaNode> nodes, Int32 index)
    {
        var depth = nodes[index].Depth;
        for (var i = index - 1; i >= 0; i--)
        {
            if (nodes[i].Depth >= depth) continue;
            depth = nodes[i].Depth;
            if (nodes[i].Role == "Group" && nodes[i].Text.Length > 0) return nodes[i].Text;
            if (nodes[i].Role == "List") break;
        }
        return "";
    }

    // The command menu lists app commands (New chat, Open in new window) under their own
    // headings beside the past chats. Named groups keep the list items to chats: a command
    // must never be offered on a key as a chat to open.
    public static List<(UiaNode Node, String Id, String Title)> SearchResults(IReadOnlyList<UiaNode> surfaceNodes, String query,
        IReadOnlyList<String> hosts, IReadOnlyList<String> paths, IReadOnlyList<String>? groups = null)
    {
        if (String.IsNullOrWhiteSpace(query)) return new List<(UiaNode, String, String)>();
        var chats = (groups ?? Array.Empty<String>()).Select(NormalizeLabel).Where(g => g.Length > 0).ToHashSet(StringComparer.Ordinal);
        var matches = surfaceNodes.Select((n, i) => (Node: n, Index: i, Id: SearchResultId(n, hosts, paths))).Where(x => x.Id != null)
            .Where(x => chats.Count == 0 || x.Node.Role != "ListItem" || chats.Contains(NormalizeLabel(ListGroup(surfaceNodes, x.Index))))
            .Select(x => (x.Node, x.Id!, SearchResultTitle(x.Node, surfaceNodes))).ToList();
        var counts = matches.GroupBy(m => m.Item2, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return matches.Where(m => counts[m.Item2] == 1).Take(100).ToList();
    }

    public static Boolean IsDialog(UiaNode node) =>
        node.AriaRole is "dialog" or "alertdialog" || (node.Role == "Window" && node.Depth > 0);

    /// <summary>
    /// A dialog blocks the composer, the reply and the panel routes only when it is modal. The
    /// composer's "Text formatting" toolbar is a non-modal dialog (#152); a dialog that does not
    /// say still blocks, as before. The search-container rule keeps using <see cref="IsDialog"/>.
    /// </summary>
    public static Boolean IsBlockingDialog(UiaNode node) => IsDialog(node) && AriaProperty(node, "modal") != "false";

    private static String? AriaProperty(UiaNode node, String key)
    {
        foreach (var pair in node.AriaProperties.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() == key) return NormalizeLabel(parts[1]);
        }
        return null;
    }

    public static Boolean PanelObstructed(IReadOnlyList<UiaNode> nodes) =>
        nodes.Any(n => IsBlockingDialog(n) || n.Role == "Menu");

    /// <summary>
    /// A preview can expose another document with arbitrary website controls. Split the scan
    /// by document: a nested document owns its own controls and stays in its parent only as a
    /// boundary.
    /// </summary>
    public static List<List<UiaNode>> Documents(IReadOnlyList<UiaNode> nodes)
    {
        var areas = new List<List<UiaNode>>();
        var stack = new Stack<(Int32 Area, Int32 Depth)>();
        foreach (var node in nodes)
        {
            while (stack.Count > 0 && stack.Peek().Depth >= node.Depth)
            {
                stack.Pop();
            }
            if (node.Role == "Document")
            {
                if (stack.Count > 0)
                {
                    areas[stack.Peek().Area].Add(node);
                }
                areas.Add(new List<UiaNode> { node });
                stack.Push((areas.Count - 1, node.Depth));
            }
            else if (stack.Count > 0)
            {
                areas[stack.Peek().Area].Add(node);
            }
        }
        return areas;
    }

    // Only the summary row opens the review panel. Counts may follow its exact name; an
    // arbitrary prefix match (Changes settings, a file disclosure) is not the destination.
    // A turn label names an opener that every edited reply carries. Seen live (app 26.928):
    // each one opens the same tab on the last turn, so several are not an ambiguity — with no
    // summary row, the latest reply's is the opener. Two summary rows still are.
    public static List<UiaNode> PanelOpeners(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels,
        IReadOnlyList<String>? turnLabels = null)
    {
        turnLabels ??= Array.Empty<String>();
        var summary = Openers(nodes, labels.Except(turnLabels, StringComparer.Ordinal).ToList());
        return summary.Count > 0 ? summary : Openers(nodes, turnLabels).TakeLast(1).ToList();
    }

    private static List<UiaNode> Openers(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels)
    {
        const String integer = "[0-9]+(?:[,.٬ ][0-9]{2,3})*";
        var names = nodes.SelectMany(n => n.Labels)
            .Where(name => labels.Any(label => label.Length > 0 && Regex.IsMatch(Collapse(name),
                "^" + Regex.Escape(label) + "(?: *[+−-] *" + integer + "){0,2}$", RegexOptions.CultureInvariant)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ExactButtons(nodes, names).Where(n => n.Pressable && n.Enabled).ToList();
    }

    public static Boolean PanelRouteAvailable(IReadOnlyList<UiaNode> nodes, String modeLabel,
        IReadOnlyList<String> openLabels, IReadOnlyList<String> visibleLabels, IReadOnlyList<String>? turnLabels = null)
    {
        if (modeLabel.Length == 0 || PanelObstructed(nodes))
        {
            return false;
        }
        var owners = Documents(nodes).Where(area => area.Any(n => n.Text == modeLabel)).ToList();
        if (owners.Count != 1)
        {
            return false;
        }
        var panels = ExactButtons(owners[0], visibleLabels);
        return panels.Count != 0 ? panels.Count == 1 : PanelOpeners(owners[0], openLabels, turnLabels).Count == 1;
    }
}
