namespace ClaudeConsoleFocus;

/// <summary>Only a unique title or verified identity may select a tab.</summary>
internal static class TabSelection
{
    internal static bool TrySelect(int matches, Func<bool> selectByIdentity, Action selectUnique)
    {
        if (matches > 1) { return selectByIdentity(); }
        if (matches != 1) { return false; }
        selectUnique();
        return true;
    }
}
