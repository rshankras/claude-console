#nullable enable
namespace ClaudeConsoleFocus;

/// <summary>Labels suggest candidates; console identity authorizes selection.</summary>
internal static class TabSelection
{
    internal static bool TrySelect(int matches, Func<bool> selectByIdentity)
    {
        // A manually renamed tab can impersonate even a unique matching title.
        return matches > 0 && selectByIdentity();
    }

    /// <summary>Probe tabs without committing selection until the target proves its identity.</summary>
    internal static bool TryProbe<T>(IReadOnlyList<T> tabs, Func<T, bool> isSelected,
        Action<T> select, Func<T, bool> verify, Func<bool>? canContinue = null)
    {
        var originals = tabs.Where(isSelected).ToArray();
        if (originals.Length != 1) return false; // no reliable rollback target
        var committed = false;
        try
        {
            foreach (var tab in tabs)
            {
                if (canContinue != null && !canContinue()) return false;
                select(tab);
                if (isSelected(tab) && verify(tab))
                {
                    committed = true;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            if (!committed) select(originals[0]);
        }
    }
}
