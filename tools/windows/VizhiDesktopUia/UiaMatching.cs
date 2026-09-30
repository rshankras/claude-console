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

    /// <summary>Rows that carry the adapter's sidebar marker in their own subtree.</summary>
    public static IEnumerable<(UiaNode Row, Int32 Index, Int32 End)> ConversationRows(
        IReadOnlyList<UiaNode> nodes, String marker)
    {
        if (marker.Length == 0)
        {
            yield break;
        }

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
                yield return (node, i, end);
                i = end;        // skip the subtree so row controls never read as items
            }
            else
            {
                i++;
            }
        }
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
        return UniqueEnabledButton(nodes, start) != null ? "ready" : "unavailable";
    }

    public static UiaNode? VoiceTarget(String action, IReadOnlyList<UiaNode> nodes,
        IReadOnlyList<String> start, IReadOnlyList<String> end)
    {
        if (action is not ("start" or "end")
            || VoiceState(nodes, start, end) != (action == "start" ? "ready" : "active"))
        {
            return null;
        }
        return UniqueEnabledButton(nodes, action == "start" ? start : end);
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
        nodes.Any(IsDialog) || ExactButtons(nodes, blockingLabels).Any(n => n.Enabled);

    public static Boolean IsDialog(UiaNode node) =>
        node.AriaRole is "dialog" or "alertdialog" || (node.Role == "Window" && node.Depth > 0);

    public static Boolean PanelObstructed(IReadOnlyList<UiaNode> nodes) =>
        nodes.Any(n => IsDialog(n) || n.Role == "Menu");

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
    public static List<UiaNode> PanelOpeners(IReadOnlyList<UiaNode> nodes, IReadOnlyList<String> labels)
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
        IReadOnlyList<String> openLabels, IReadOnlyList<String> visibleLabels)
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
        return panels.Count != 0 ? panels.Count == 1 : PanelOpeners(owners[0], openLabels).Count == 1;
    }
}
