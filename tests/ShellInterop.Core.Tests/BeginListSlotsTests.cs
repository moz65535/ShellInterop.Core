using ShellInterop.Core.JumpLists;

namespace ShellInterop.Core.Tests;

/// <summary>SHELL-JL-BEGINLIST-SLOTS-01: BeginList slot count reaches the caller's planner inside the same transaction. Fake Shell only.</summary>
public class BeginListSlotsTests
{
    private static JumpDestinationDefinition Dest(string id) => new() { Key = "k" + id, Link = Samples.Task("D" + id) };

    private static JumpCategoryDefinition Cat(string name, string[] ids, params JumpListEntry?[] fallback)
        => new() { Name = name, Destinations = ids.Select(Dest).ToArray(), TasksFallback = fallback.Length == 0 ? null : fallback };

    private static JumpListEntry Fb(string id) => JumpListEntry.ForKeyedTask("k" + id, Samples.Task("F" + id));

    private static JumpRemovedDestinationTracking Track() => new();

    private static JumpListDefinition Candidates()
        => JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))],
            [Cat("A", ["1", "2", "3"], Fb("1"), Fb("2"), Fb("3"))], Track());

    /// <summary>Keeps only the first <paramref name="keep"/> destinations/fallback entries of the candidate category.</summary>
    private static JumpListDefinition Pick(int keep)
    {
        string[] ids = new[] { "1", "2", "3" }.Take(keep).ToArray();
        var cat = Cat("A", ids, ids.Select(Fb).ToArray());
        return JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))], [cat], Track());
    }

    private static int Count(FakeShell shell, string call) => shell.Calls.Count(c => c == call);

    [Fact]
    public async Task Planner_SeesSlots_BeforeAnyAppend_AndOnlyChosenItemsArePublished()
    {
        var shell = new FakeShell { BeginSlots = 2 };
        using var publisher = Samples.Publisher(shell);
        uint seen = 0;
        int callsAtPlanning = -1;
        var result = await publisher.ApplyPlannedAsync(Candidates(), ctx =>
        {
            seen = ctx.BeginListSlots;
            callsAtPlanning = shell.Calls.Count;
            Assert.Equal(JumpListPlanPhase.Initial, ctx.Phase);
            return Pick((int)ctx.BeginListSlots);
        });

        Assert.True(result.Succeeded);
        Assert.Equal(2u, seen);
        Assert.Equal(2u, result.BeginListSlots);
        // Planning happened right after BeginList/ReadRemoved and before any append/create call.
        Assert.Equal(["Create", "SetAppId", "BeginList"], shell.Calls.Take(callsAtPlanning).Where(c => c != "ReadRemoved"));
        Assert.Equal(["Task D1", "Task D2"], shell.AppendedCategories.Single().Links.Select(l => l.Title));
        Assert.Equal(1, Count(shell, "BeginList"));
        Assert.Equal(1, Count(shell, "CommitList"));
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
    }

    [Fact]
    public async Task ZeroSlots_IsPassedAsZero_NotReplaced()
    {
        var shell = new FakeShell { BeginSlots = 0 };
        using var publisher = Samples.Publisher(shell);
        uint seen = 99;
        var result = await publisher.ApplyPlannedAsync(Candidates(), ctx => { seen = ctx.BeginListSlots; return Pick(0); });
        Assert.True(result.Succeeded);
        Assert.Equal(0u, seen);
        Assert.Equal(0u, result.BeginListSlots);
    }

    [Fact]
    public async Task BeginListFailure_NeverCallsPlanner_AndReportsNoSlots()
    {
        var shell = new FakeShell { BeginHr = Hresults.Fail };
        using var publisher = Samples.Publisher(shell);
        bool called = false;
        var result = await publisher.ApplyPlannedAsync(Candidates(), _ => { called = true; return Pick(1); });
        Assert.Equal(JumpListApplyStatus.BeginFailed, result.Status);
        Assert.False(called);
        Assert.Null(result.BeginListSlots);
        Assert.DoesNotContain("AbortList", shell.Calls);
    }

    [Fact]
    public async Task PrivacyFallbackRetry_ReplansWithTheNewSlotCount()
    {
        var shell = new FakeShell();
        shell.BeginSlotsByCall[1] = 8;
        shell.BeginSlotsByCall[2] = 3;
        shell.AppendCategoryHrByCall[2] = Hresults.AccessDenied;
        using var publisher = Samples.Publisher(shell);
        var seen = new List<(JumpListPlanPhase Phase, uint Slots)>();
        var result = await publisher.ApplyPlannedAsync(
            JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))],
                [Cat("A", ["1"], Fb("1")), Cat("B", ["2", "3"], Fb("2"), Fb("3"))], Track()),
            ctx =>
            {
                seen.Add((ctx.Phase, ctx.BeginListSlots));
                // Initial: everything. Retry: only what fits the (smaller) new budget.
                var all = new[] { Cat("A", ["1"], Fb("1")), Cat("B", ["2", "3"], Fb("2"), Fb("3")) };
                return ctx.Phase == JumpListPlanPhase.Initial
                    ? JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))], all, Track())
                    : JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [JumpListEntry.ForTask(Samples.Task("T"))],
                        [Cat("A", ["1"], Fb("1"))], Track());
            });

        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksFallback, result.EffectiveDisplayMode);
        Assert.Equal([(JumpListPlanPhase.Initial, 8u), (JumpListPlanPhase.PrivacyFallbackRetry, 3u)], seen);
        Assert.Equal(3u, result.BeginListSlots);
        Assert.Equal(2, Count(shell, "BeginList"));
        Assert.Equal(1, Count(shell, "CommitList"));
        Assert.Equal(["Task T", "Task F1"], shell.Store.Select(t => t.Title));
    }

    [Fact]
    public async Task RetryBeginListFailure_DoesNotCallPlannerAgain_AndHasNoSlots()
    {
        var shell = new FakeShell { FailBeginFromCall = 2 };
        shell.AppendCategoryHrByCall[2] = Hresults.AccessDenied;
        using var publisher = Samples.Publisher(shell);
        int calls = 0;
        var def = JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [], [Cat("A", ["1"], Fb("1")), Cat("B", ["2"], Fb("2"))], Track());
        var result = await publisher.ApplyPlannedAsync(def, _ => { calls++; return def; });
        Assert.Equal(JumpListApplyStatus.BeginFailed, result.Status);
        Assert.Equal(1, calls);                   // the retry's planner call never happens
        Assert.Null(result.BeginListSlots);       // the first session's value is not carried over
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    [Fact]
    public async Task PlannerException_AbortsWithoutCommit()
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyPlannedAsync(Candidates(), _ => throw new InvalidOperationException("boom"));
        Assert.Equal(JumpListApplyStatus.PlanFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.PlanGeneration, result.FailurePhase);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.DoesNotContain("AppendCategory", shell.Calls);
        Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
    }

    [Fact]
    public async Task PlannerNull_AbortsWithoutCommit()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyPlannedAsync(Candidates(), _ => null);
        Assert.Equal(JumpListApplyStatus.PlanFailed, result.Status);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    [Fact]
    public async Task InvalidOrMismatchedPlan_AbortsWithoutCommit()
    {
        foreach (var bad in new JumpListDefinition[]
        {
            JumpListDefinition.FromEntriesWithCategories(Samples.AppId, [], [Cat("A", ["1"]), Cat("a", ["2"])], Track()),   // duplicate names
            JumpListDefinition.FromEntriesWithCategories("Other.App.Id", [], [], null),                                      // other AppId
            JumpListDefinition.FromEntries(Samples.AppId, []),                                                                // tracking dropped
        })
        {
            var shell = new FakeShell();
            using var publisher = Samples.Publisher(shell);
            var result = await publisher.ApplyPlannedAsync(Candidates(), _ => bad);
            Assert.Equal(JumpListApplyStatus.PlanFailed, result.Status);
            Assert.Contains("AbortList", shell.Calls);
            Assert.DoesNotContain("CommitList", shell.Calls);
        }
    }

    [Fact]
    public async Task PlannerAbortFailure_KeepsAbortFailedState()
    {
        var shell = new FakeShell { AbortHr = Hresults.Fail };
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyPlannedAsync(Candidates(), _ => throw new InvalidOperationException("boom"));
        Assert.Equal(JumpListApplyStatus.AbortFailed, result.Status);
        Assert.Equal(JumpListShellState.Unknown, result.ShellState);
        Assert.Equal(JumpListFailurePhase.PlanGeneration, result.PrecedingError?.Phase);
    }

    [Fact]
    public async Task PlannedCommitFailure_StillReportsCommitFailed()
    {
        var shell = new FakeShell { CommitHr = Hresults.Fail };
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyPlannedAsync(Candidates(), _ => Pick(1));
        Assert.Equal(JumpListApplyStatus.CommitFailed, result.Status);
        Assert.Equal(JumpListShellState.Unknown, result.ShellState);
        Assert.Equal(10u, result.BeginListSlots);
    }

    [Fact]
    public async Task PlannerRunsOnTheStaWorker_AndExistingApplyAsyncIsUnchanged()
    {
        var shell = new FakeShell { BeginSlots = 7 };
        using var publisher = Samples.Publisher(shell);
        ApartmentState apartment = ApartmentState.Unknown;
        var planned = await publisher.ApplyPlannedAsync(Candidates(), _ => { apartment = Thread.CurrentThread.GetApartmentState(); return Pick(1); });
        Assert.True(planned.Succeeded);
        Assert.Equal(ApartmentState.STA, apartment);

        // Existing API: same definition published as-is, slot count only recorded.
        var plain = await publisher.ApplyAsync(Candidates());
        Assert.True(plain.Succeeded);
        Assert.Equal(7u, plain.BeginListSlots);
        Assert.Equal(["Task D1", "Task D2", "Task D3"], shell.AppendedCategories[^1].Links.Select(l => l.Title));
    }
}
