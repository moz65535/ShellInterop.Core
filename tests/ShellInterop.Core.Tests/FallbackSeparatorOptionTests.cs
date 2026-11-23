using ShellInterop.Core.JumpLists;

namespace ShellInterop.Core.Tests;

/// <summary>SHELL-JL-FALLBACK-SEPARATOR-01: AutoSeparateTasksFallback. Fake Shell only (no real Shell evidence).</summary>
public class FallbackSeparatorOptionTests
{
    private static JumpDestinationDefinition Dest(string id) => new() { Key = "k" + id, Link = Samples.Task("D" + id) };

    private static JumpListEntry Fb(string id) => JumpListEntry.ForKeyedTask("k" + id, Samples.Task("F" + id));

    private static JumpCategoryDefinition Cat(string name, string[] ids, params JumpListEntry?[] fallback)
        => new() { Name = name, Destinations = ids.Select(Dest).ToArray(), TasksFallback = fallback };

    private static JumpRemovedDestinationTracking Track() => new();

    private static JumpListDefinition Def(bool? auto = null, params JumpListEntry?[] entries)
    {
        var d = JumpListDefinition.FromEntriesWithCategories(Samples.AppId,
            entries.Length == 0 ? [JumpListEntry.ForTask(Samples.Task("T"))] : entries,
            [Cat("A", ["1"], Fb("1")), Cat("B", ["2"], Fb("2"))], Track());
        return auto is null ? d : d.WithAutoSeparateTasksFallback(auto.Value);
    }

    private static string[] Shown(FakeShell shell) => shell.LastEntries.Select(e => e.IsSeparator ? "-" : e.Task!.Title).ToArray();

    [Fact]
    public void Default_IsTrue_AndWithCopiesWithoutMutatingTheOriginal()
    {
        var original = Def();
        var off = original.WithAutoSeparateTasksFallback(false);
        Assert.True(original.AutoSeparateTasksFallback);
        Assert.False(off.AutoSeparateTasksFallback);
        Assert.Equal(original.Entries.Count, off.Entries.Count);
        Assert.Equal(original.Categories.Select(c => c!.Name), off.Categories.Select(c => c!.Name));
        Assert.Equal(original.RemovedDestinationTracking, off.RemovedDestinationTracking);
        Assert.True(off.WithAutoSeparateTasksFallback(true).AutoSeparateTasksFallback);
    }

    [Fact]
    public async Task Unspecified_KeepsAutomaticSeparators_BeforeFirstBlockAndBetweenBlocks()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[1] = Hresults.AccessDenied;
        using var publisher = Samples.Publisher(shell);
        var r = await publisher.ApplyAsync(Def());
        Assert.True(r.Succeeded);
        Assert.Equal(["Task T", "-", "Task F1", "-", "Task F2"], Shown(shell));
    }

    [Fact]
    public async Task Disabled_InsertsNoAutomaticSeparators_OnFirstCategoryDenied_AndKeepsOrder()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[1] = Hresults.AccessDenied;
        using var publisher = Samples.Publisher(shell);
        var r = await publisher.ApplyAsync(Def(false));
        Assert.True(r.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksFallback, r.EffectiveDisplayMode);
        Assert.Equal(["Task T", "Task F1", "Task F2"], Shown(shell));
        Assert.Equal(1, shell.Calls.Count(c => c == "BeginList"));   // Initial plan only: no retry session
        Assert.DoesNotContain("AbortList", shell.Calls);
        Assert.Equal(1, shell.Calls.Count(c => c == "CommitList"));
    }

    [Fact]
    public async Task Disabled_KeepsCallerSeparators_ButAddsNone()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[1] = Hresults.AccessDenied;
        var entries = new[] { JumpListEntry.ForTask(Samples.Task("T")), JumpListEntry.Separator(), JumpListEntry.ForTask(Samples.Task("U")) };
        var def = JumpListDefinition.FromEntriesWithCategories(Samples.AppId, entries,
            [Cat("A", ["1"], Fb("1"), JumpListEntry.Separator(), JumpListEntry.ForTask(Samples.Task("F9"))), Cat("B", ["2"], Fb("2"))], Track())
            .WithAutoSeparateTasksFallback(false);
        using var publisher = Samples.Publisher(shell);
        var r = await publisher.ApplyAsync(def);
        Assert.True(r.Succeeded);
        Assert.Equal(["Task T", "-", "Task U", "Task F1", "-", "Task F9", "Task F2"], Shown(shell));
        Assert.Equal(["Task T", "Task U", "Task F1", "Task F9", "Task F2"], shell.Store.Select(t => t.Title));
    }

    private static JumpListDefinition Plan(bool auto, params string[] ids)
    {
        var cats = ids.Select(i => Cat("C" + i, [i], Fb(i))).ToArray();
        return JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))], cats, Track())
            .WithAutoSeparateTasksFallback(auto);
    }

    [Theory]
    [InlineData(false, false, new[] { "Task T", "Task F1", "Task F2" })]
    [InlineData(true, false, new[] { "Task T", "Task F1", "Task F2" })]
    [InlineData(false, true, new[] { "Task T", "-", "Task F1", "-", "Task F2" })]
    [InlineData(true, true, new[] { "Task T", "-", "Task F1", "-", "Task F2" })]
    public async Task PrivacyFallbackRetry_UsesTheRetryPlansOwnSetting_NotTheInitialOne(bool initialAuto, bool retryAuto, string[] expected)
    {
        var shell = new FakeShell();
        shell.BeginSlotsByCall[1] = 8;
        shell.BeginSlotsByCall[2] = 3;
        shell.AppendCategoryHrByCall[2] = Hresults.AccessDenied;   // 2nd category of the Initial plan is denied after the 1st succeeded
        using var publisher = Samples.Publisher(shell);
        var phases = new List<JumpListPlanPhase>();
        var r = await publisher.ApplyPlannedAsync(Plan(true, "1", "2"), ctx =>
        {
            phases.Add(ctx.Phase);
            return ctx.Phase == JumpListPlanPhase.Initial ? Plan(initialAuto, "1", "2") : Plan(retryAuto, "1", "2");
        });
        Assert.True(r.Succeeded);
        Assert.Equal([JumpListPlanPhase.Initial, JumpListPlanPhase.PrivacyFallbackRetry], phases);
        Assert.Equal(JumpListDisplayMode.TasksFallback, r.EffectiveDisplayMode);
        Assert.Equal(expected, Shown(shell));
        // Contract unchanged by the option: abort, second BeginList with its own slots, one commit, no category published.
        Assert.Equal(2, shell.Calls.Count(c => c == "BeginList"));
        Assert.Equal(1, shell.Calls.Count(c => c == "AbortList"));
        Assert.Equal(1, shell.Calls.Count(c => c == "CommitList"));
        Assert.Equal(3u, r.BeginListSlots);
    }

    [Fact]
    public async Task SamePublisher_ConsecutiveApplies_DoNotLeakTheSetting()
    {
        var shell = new FakeShell();
        foreach (int call in new[] { 1, 2, 3 })
        {
            shell.AppendCategoryHrByCall[call] = Hresults.AccessDenied;
        }

        using var publisher = Samples.Publisher(shell);
        await publisher.ApplyAsync(Def(false));
        var first = Shown(shell);
        await publisher.ApplyAsync(Def());
        var second = Shown(shell);
        await publisher.ApplyAsync(Def(false));
        var third = Shown(shell);
        Assert.Equal(["Task T", "Task F1", "Task F2"], first);
        Assert.Equal(["Task T", "-", "Task F1", "-", "Task F2"], second);
        Assert.Equal(first, third);
    }
}
