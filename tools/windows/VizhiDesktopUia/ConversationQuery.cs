using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VizhiDesktopUia;

internal static partial class Program
{
    /// <summary>Ask the provider for all exact-title matches, then read only their subtrees.
    /// Include every source used by Read.Text, so a value/help label cannot hide a duplicate.
    /// Query results are never retained or reused for a later action.</summary>
    private static UiaNode? QueryConversation(Target target, String title, String marker)
    {
        var timer = Stopwatch.StartNew();
        _scanPath = "query";
        try
        {
            RequireTarget(target);
            var condition = Uia.CreatePropertyCondition(UiaIds.Name, title);
            foreach (var property in new[] { UiaIds.ValueValue, UiaIds.HelpText, UiaIds.FullDescription })
                condition = Uia.CreateOrCondition(condition, Uia.CreatePropertyCondition(property, title));
            var matches = Roots.Read(target.Hwnd.ToInt64(), target.Pid, () => Uia.ElementFromHandle(target.Hwnd),
                root => root.FindAllBuildCache(UiaIds.TreeScopeDescendants, condition, CacheRequest()));
            _queryDetail = $"roots:{matches?.Length ?? 0}";
            if (matches == null || matches.Length == 0 || matches.Length > 32) return null;
            var branches = new List<IReadOnlyList<UiaNode>>();
            for (var i = 0; i < matches.Length; i++)
            {
                var scan = ReadTree(matches.GetElement(i));
                if (!scan.Complete) { _queryDetail = "incomplete"; return null; }
                branches.Add(scan.Nodes);
            }
            var selected = UiaMatching.UniqueQueriedConversation(branches, title, marker);
            _queryDetail += selected == null ? ";fallback" : ";unique";
            return selected;
        }
        catch (COMException ex)
        {
            _queryDetail = $"uia:0x{ex.HResult:X8}";
            Roots.Invalidate();
            return null; // A read-only optimization failed; full resolution remains authoritative.
        }
        finally { _scanMs += timer.ElapsedMilliseconds; }
    }
}
