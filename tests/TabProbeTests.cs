using System;
using System.Collections.Generic;
using ClaudeConsoleFocus;
using Xunit;

namespace Loupedeck.ClaudeConsolePlugin.Tests;

public sealed class TabProbeTests
{
    [Fact] public void Commits_only_the_verified_candidate()
    {
        var selected = 2;
        var probes = new List<int>();
        Assert.True(TabSelection.TryProbe(new[] { 0, 1, 2 }, t => selected == t,
            t => selected = t, t => { probes.Add(t); return t == 1; }));
        Assert.Equal(1, selected);
        Assert.Equal(new[] { 0, 1 }, probes);
    }

    [Fact] public void Missing_identity_restores_original_tab()
    {
        var selected = 1;
        Assert.False(TabSelection.TryProbe(new[] { 0, 1, 2 }, t => selected == t,
            t => selected = t, _ => false));
        Assert.Equal(1, selected);
    }

    [Fact] public void Failed_selection_cannot_verify_the_previous_tab()
    {
        var selected = 0;
        var probes = new List<int>();
        Assert.False(TabSelection.TryProbe(new[] { 0, 1 }, t => selected == t,
            _ => { }, t => { probes.Add(t); return t == 1; }));
        Assert.Equal(new[] { 0 }, probes);
    }

    [Fact] public void Probe_exception_restores_original_tab()
    {
        var selected = 1;
        Assert.Throws<InvalidOperationException>(() => TabSelection.TryProbe(new[] { 0, 1 },
            t => selected == t, t => selected = t, _ => throw new InvalidOperationException()));
        Assert.Equal(1, selected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Missing_or_ambiguous_rollback_identity_changes_nothing(int selectedCount)
    {
        var changed = false;
        Assert.False(TabSelection.TryProbe(new[] { 0, 1 }, _ => selectedCount == 2,
            _ => changed = true, _ => true));
        Assert.False(changed);
    }

    [Fact] public void Expired_budget_restores_original_and_stops_probing()
    {
        var selected = 2;
        var attempts = 0;
        Assert.False(TabSelection.TryProbe(new[] { 0, 1, 2 }, t => selected == t,
            t => selected = t, _ => { attempts++; return false; }, () => attempts < 1));
        Assert.Equal(1, attempts);
        Assert.Equal(2, selected);
    }
}
