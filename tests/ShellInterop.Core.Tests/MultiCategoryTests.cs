using ShellInterop.Core.JumpLists;
using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.Tests;

/// <summary>SHELL-JL-06: several custom categories. Fake Shell only (no real Shell evidence).</summary>
public class MultiCategoryTests
{
    private static JumpDestinationDefinition Dest(string id)
        => new() { Key = "k" + id, Link = Samples.Task("D" + id) };

    private static JumpCategoryDefinition Cat(string name, string[] ids, params JumpListEntry?[] fallback)
        => new() { Name = name, Destinations = ids.Select(Dest).ToArray(), TasksFallback = fallback.Length == 0 ? null : fallback };

    private static JumpListEntry Fb(string id) => JumpListEntry.ForKeyedTask("k" + id, Samples.Task("F" + id));

    private static JumpListDefinition Def(JumpRemovedDestinationTracking? tracking, params JumpCategoryDefinition?[] cats)
        => JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))], cats, tracking);

    private static JumpRemovedDestinationTracking Track(IEnumerable<string>? excluded = null, params JumpDestinationMatch[] matchOnly)
        => new() { ExcludedDestinationKeys = (excluded ?? []).ToArray(), MatchOnly = matchOnly };

    private static RawRemovedDestination Raw(string id) => new(RawRemovedKind.Link, Samples.Exe, Samples.Task("D" + id).Arguments);

    private static async Task<JumpListApplyResult> Apply(FakeShell shell, JumpListDefinition def)
    {
        using var publisher = Samples.Publisher(shell);
        return await publisher.ApplyAsync(def);
    }

    private static int Count(FakeShell shell, string call) => shell.Calls.Count(c => c == call);

    [Fact]
    public void Model_SingleCategoryCompat_And_MultiCategoryHasNoRepresentative()
    {
        var one = Cat("A", ["1"]);
        var single = JumpListDefinition.FromEntries(Samples.AppId, [], one, Track());
        Assert.Equal(["A"], single.Categories.Select(c => c!.Name));
        Assert.Equal("A", single.Category!.Name);
        Assert.Null(JumpListDefinition.FromEntries(Samples.AppId, [], null, null).Category);
        Assert.Empty(JumpListDefinition.FromEntries(Samples.AppId, [], null, null).Categories);

        var cats = new List<JumpCategoryDefinition?> { one, Cat("B", ["2"]) };
        var multi = JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [], cats, Track());
        cats.Clear();
        Assert.Equal(2, multi.Categories.Count);
        Assert.Null(multi.Category);
    }

    [Fact]
    public async Task Categories_AreAppendedInGivenOrder_SkipEmpty_AndTasksAreAddedOnce()
    {
        var shell = new FakeShell();
        var result = await Apply(shell, Def(Track(["k4"]), Cat("C", ["3", "1"]), Cat("Hidden", ["4"]), Cat("A", ["2"])));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategoryAndTasks, result.EffectiveDisplayMode);
        Assert.Equal(["C", "A"], shell.AppendedCategories.Select(c => c.Name));
        Assert.Equal(["Task D3", "Task D1"], shell.AppendedCategories[0].Links.Select(l => l.Title));
        Assert.Equal(1, Count(shell, "AddUserTasks"));
        Assert.Equal(1, Count(shell, "CommitList"));
        Assert.Equal(1, Count(shell, "BeginList"));
        Assert.True(shell.Calls.LastIndexOf("AppendCategory") < shell.Calls.IndexOf("AddUserTasks"));
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
    }

    [Fact]
    public async Task OnlyExcludedCategories_AreCategoryEmpty()
    {
        var result = await Apply(new FakeShell(), Def(Track(["k1", "k2"]), Cat("A", ["1"]), Cat("B", ["2"])));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategoryEmpty, result.EffectiveDisplayMode);
    }

    [Fact]
    public async Task InvalidInput_IsRejectedBeforeAnyComCall()
    {
        var cases = new (string Code, JumpListDefinition Def)[]
        {
            ("Category.NameDuplicate", Def(Track(), Cat("Items", ["1"]), Cat("ITEMS", ["2"]))),
            ("Destination.KeyDuplicate", Def(Track(), Cat("A", ["1"]), Cat("B", ["1"]))),
            ("Category.DuplicateDestination", Def(Track(), Cat("A", ["1"]), new JumpCategoryDefinition
            {
                Name = "B",
                Destinations = [new() { Key = "other", Link = Samples.Task("D1") }],
            })),
            ("Category.Null", Def(Track(), Cat("A", ["1"]), null)),
            ("Tracking.Required", Def(null, Cat("A", ["1"]), Cat("B", ["2"]))),
        };

        foreach (var (code, def) in cases)
        {
            var shell = new FakeShell();
            var result = await Apply(shell, def);
            Assert.Equal(JumpListApplyStatus.ValidationFailed, result.Status);
            Assert.Contains(result.ValidationErrors, e => e.Code == code);
            Assert.Empty(shell.Calls);
        }
    }

    [Fact]
    public async Task RemovalInAnyCategoryOrMatchOnly_IsPendingTogether_ThenReapplyKeepsOthers()
    {
        var hidden = new JumpDestinationMatch("hidden", "--hidden", Samples.Exe);
        var shell = new FakeShell();
        shell.Removed.Add(Raw("2"));
        shell.Removed.Add(Raw("3"));
        shell.Removed.Add(new(RawRemovedKind.Link, Samples.Exe, "--hidden"));
        var def = Def(Track(null, hidden), Cat("A", ["1", "2"]), Cat("B", ["3"]));
        var result = await Apply(shell, def);
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, result.Status);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Equal(["k2", "k3", "hidden"], result.RemovedDestinations.Select(r => r.Key));
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("AppendCategory", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.Equal(3, shell.Removed.Count);

        // Caller persisted the keys: a re-apply drops them wherever they sit and keeps the rest.
        var again = await Apply(shell, Def(Track(["k2", "k3", "hidden"], hidden), Cat("A", ["1", "2"]), Cat("B", ["3"])));
        Assert.True(again.Succeeded);
        Assert.Equal([("A", 1)], shell.AppendedCategories.Select(c => (c.Name, c.Links.Count)));
        Assert.Empty(shell.Removed);
    }

    [Fact]
    public async Task UnreadableRemovedItem_StopsWithoutCommit()
    {
        var shell = new FakeShell();
        shell.Removed.Add(new(RawRemovedKind.Unreadable));
        var result = await Apply(shell, Def(Track(), Cat("A", ["1"]), Cat("B", ["2"])));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationUnidentifiable, result.Status);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    [Fact]
    public async Task LaterCategoryFailure_AbortsAndNeverCommitsEarlierOnes_EvenForOtherHresults()
    {
        foreach (int hr in new[] { Hresults.Fail, Hresults.Custom })
        {
            var shell = new FakeShell();
            shell.Seed(Samples.Task("old"));
            shell.AppendCategoryHrByCall[2] = hr;
            var result = await Apply(shell, Def(Track(), Cat("A", ["1"], Fb("1")), Cat("B", ["2"], Fb("2")), Cat("C", ["3"])));
            Assert.Equal(JumpListApplyStatus.CategoryAppendFailed, result.Status);
            Assert.Equal(hr, result.HResult);
            Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
            Assert.Equal(2, Count(shell, "AppendCategory"));
            Assert.Contains("AbortList", shell.Calls);
            Assert.DoesNotContain("CommitList", shell.Calls);
            Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
            Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
        }
    }

    [Fact]
    public async Task AccessDeniedOnFirstCategory_FallsBackInSameSession_ForAllCategoriesInOrder()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[1] = Hresults.AccessDenied;
        var result = await Apply(shell, Def(Track(["k2"]), Cat("A", ["1", "2"], Fb("1"), Fb("2")), Cat("B", ["3"], Fb("3"))));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksFallback, result.EffectiveDisplayMode);
        Assert.Equal(JumpListFallbackReason.PrivacyAccessDenied, result.FallbackReason);
        Assert.Equal(1, Count(shell, "AppendCategory"));
        Assert.Equal(1, Count(shell, "BeginList"));
        Assert.Equal(1, Count(shell, "CommitList"));
        Assert.Equal(["Task T", "Task F1", "Task F3"], shell.Store.Select(t => t.Title));
        Assert.DoesNotContain(shell.Store, t => t.Title == "Task F2");
    }

    [Fact]
    public async Task AccessDeniedAfterEarlierSuccess_AbortsRestartsTasksOnly_AndCommitsOnce()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[2] = Hresults.AccessDenied;
        var result = await Apply(shell, Def(Track(["k2"]), Cat("A", ["1"], Fb("1")), Cat("B", ["2", "3"], Fb("2"), Fb("3"))));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksFallback, result.EffectiveDisplayMode);
        string[] seq = ["BeginList", "ReadRemoved", "AppendCategory", "AppendCategory", "AbortList", "BeginList", "ReadRemoved", "CreateTasks", "AddUserTasks", "CommitList"];
        Assert.Equal(seq, shell.Calls.Where(seq.Contains).ToArray());
        Assert.Equal(1, Count(shell, "CommitList"));
        Assert.Equal(["Task T", "Task F1", "Task F3"], shell.Store.Select(t => t.Title));
    }

    [Fact]
    public async Task AccessDeniedRetry_DetectsNewRemoval_AbortsAgain_AndNeverRegistersFallback()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[2] = Hresults.AccessDenied;
        shell.RemovedByReadCall[1] = [];
        shell.RemovedByReadCall[2] = [Raw("B2")];
        var def = Def(Track(), Cat("A", ["A1", "A2"], Fb("A1")), Cat("B", ["B1", "B2"], Fb("B1")));
        var result = await Apply(shell, def);

        Assert.False(result.Succeeded);
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, result.Status);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Contains(result.RemovedDestinations, r => r.Key == "kB2" && r.Kind == RemovedDestinationKind.Matched);
        Assert.Equal(2, Count(shell, "BeginList"));
        Assert.Equal(2, Count(shell, "AbortList"));
        Assert.Equal(0, Count(shell, "CommitList"));
        Assert.Equal(2, Count(shell, "ReadRemoved"));
        Assert.Equal(2, Count(shell, "AppendCategory"));
        Assert.DoesNotContain("CreateTasks", shell.Calls);
        Assert.DoesNotContain("AddUserTasks", shell.Calls);
        Assert.Empty(shell.Store);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
    }

    [Fact]
    public async Task AccessDenied_WithoutFallback_SkipsCategories_AndOtherHrDoesNotFallBack()
    {
        var shell = new FakeShell();
        shell.AppendCategoryHrByCall[1] = Hresults.AccessDenied;
        var result = await Apply(shell, Def(Track(), Cat("A", ["1"]), Cat("B", ["2"])));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategorySkipped, result.EffectiveDisplayMode);
        Assert.Equal(["Task T"], shell.Store.Select(t => t.Title));

        var other = new FakeShell();
        other.AppendCategoryHr = Hresults.Fail;
        var failed = await Apply(other, Def(Track(), Cat("A", ["1"], Fb("1"))));
        Assert.Equal(JumpListApplyStatus.CategoryAppendFailed, failed.Status);
        Assert.Equal(JumpListFallbackReason.None, failed.FallbackReason);
    }

    [Fact]
    public async Task ManyCategoriesAndDestinations_AreNotTruncatedOrTreatedAsRemoved()
    {
        var shell = new FakeShell();
        var cats = Enumerable.Range(0, 5).Select(c => Cat("Cat" + c, Enumerable.Range(0, 20).Select(i => $"{c}_{i}").ToArray())).ToArray();
        var result = await Apply(shell, Def(Track(), cats));
        Assert.True(result.Succeeded);
        Assert.Equal(5, shell.AppendedCategories.Count);
        Assert.All(shell.AppendedCategories, c => Assert.Equal(20, c.Links.Count));
        Assert.Empty(result.RemovedDestinations);
    }
}
