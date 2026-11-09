using ShellInterop.Core.JumpLists;
using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.Tests;

/// <summary>SHELL-JL-04: custom category, removed-destination tracking, privacy fallback. Fake Shell only (no real Shell evidence).</summary>
public class CategoryAndRemovedDestinationTests
{
    private const string Aud = Samples.AppId;

    private static JumpDestinationDefinition Dest(string id, JumpDestinationPathPolicy policy = JumpDestinationPathPolicy.RequirePathMatch)
        => new() { Key = "k" + id, Link = Samples.Task("D" + id), PathPolicy = policy };

    private static RawRemovedDestination RawLink(string id, string? path = null)
        => new(RawRemovedKind.Link, path ?? Samples.Exe, Samples.Task("D" + id).Arguments);

    private static JumpCategoryDefinition Category(IReadOnlyList<JumpListEntry?>? fallback = null, params JumpDestinationDefinition[] dests)
        => new() { Name = "Items", Destinations = dests, TasksFallback = fallback };

    private static JumpListDefinition Def(JumpCategoryDefinition? category, JumpRemovedDestinationTracking? tracking, params string[] tasks)
        => JumpListDefinition.FromEntries(Aud, tasks.Select(t => JumpListEntry.ForTask(Samples.Task(t))), category, tracking);

    private static JumpRemovedDestinationTracking Track(IEnumerable<string>? excluded = null, params JumpDestinationMatch[] matchOnly)
        => new() { ExcludedDestinationKeys = (excluded ?? []).ToArray(), MatchOnly = matchOnly };

    private static JumpListEntry Sep() => JumpListEntry.Separator();

    private static async Task<JumpListApplyResult> Apply(FakeShell shell, JumpListDefinition def)
    {
        using var publisher = Samples.Publisher(shell);
        return await publisher.ApplyAsync(def);
    }

    // 1. legacy Tasks-only apply: untouched path
    [Fact]
    public async Task Legacy_TasksOnly_DoesNotReadRemovedList()
    {
        var shell = new FakeShell();
        var result = await Apply(shell, Samples.Definition("A"));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksOnly, result.EffectiveDisplayMode);
        Assert.DoesNotContain("ReadRemoved", shell.Calls);
        Assert.Empty(result.RemovedDestinations);
    }

    // 2. category order + coexistence with Tasks
    [Fact]
    public async Task Category_IsAppendedBeforeTasks_AndCommitted()
    {
        var shell = new FakeShell();
        var result = await Apply(shell, Def(Category(null, Dest("1"), Dest("2")), Track(), "T"));
        Assert.True(result.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategoryAndTasks, result.EffectiveDisplayMode);
        Assert.Equal(JumpListFallbackReason.None, result.FallbackReason);
        string[] order = ["BeginList", "ReadRemoved", "CreateDestinations", "AppendCategory", "CreateTasks", "AddUserTasks", "CommitList"];
        Assert.Equal(order, shell.Calls.Where(order.Contains).ToArray());
        var appended = Assert.Single(shell.AppendedCategories);
        Assert.Equal("Items", appended.Name);
        Assert.Equal(["Task D1", "Task D2"], appended.Links.Select(l => l.Title));
        Assert.Equal(["Task T"], shell.Store.Select(t => t.Title));
    }

    // 3. new removal: abort, pending, no commit, nothing consumed
    [Fact]
    public async Task NewRemoval_AbortsAndReportsPending_WithoutCommit()
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        shell.Removed.Add(RawLink("1"));
        var result = await Apply(shell, Def(Category(null, Dest("1"), Dest("2")), Track(), "T"));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, result.Status);
        Assert.False(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.DoesNotContain("AppendCategory", shell.Calls);
        Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
        Assert.Single(shell.Removed);
        var r = Assert.Single(result.RemovedDestinations);
        Assert.Equal(("k1", RemovedDestinationKind.Matched, MatchConfidence.Exact, false), (r.Key, r.Kind, r.Confidence, r.AlreadyExcluded));
        Assert.Equal(JumpListDisplayMode.None, result.EffectiveDisplayMode);
    }

    // 4. persisted exclusion: re-apply never re-registers, consumes the list
    [Fact]
    public async Task ExcludedKey_IsNotAppended_AndRemovedListIsConsumedOnCommit()
    {
        var shell = new FakeShell();
        shell.Removed.Add(RawLink("1"));
        var def = Def(Category(null, Dest("1"), Dest("2")), Track(["k1"]));
        var result = await Apply(shell, def);
        Assert.True(result.Succeeded);
        Assert.Equal(["Task D2"], shell.AppendedCategories.Single().Links.Select(l => l.Title));
        Assert.True(result.RemovedDestinations.Single().AlreadyExcluded);
        Assert.Empty(shell.Removed);

        var all = await Apply(new FakeShell(), Def(Category(null, Dest("1")), Track(["k1"])));
        Assert.True(all.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategoryEmpty, all.EffectiveDisplayMode);
    }

    // 5 + 6. no category, tracking on; MatchOnly recognises hidden group; foreign things never block
    [Fact]
    public async Task TrackingWithoutCategory_DetectsMatchOnlyRemoval()
    {
        var shell = new FakeShell();
        var hidden = new JumpDestinationMatch("hidden", Samples.Task("H").Arguments, Samples.Exe);
        shell.Removed.Add(new(RawRemovedKind.Link, Samples.Exe, hidden.Arguments));
        var result = await Apply(shell, Def(null, Track(null, hidden), "T"));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, result.Status);
        Assert.Equal("hidden", result.RemovedDestinations.Single().Key);
        Assert.DoesNotContain("CommitList", shell.Calls);

        var ok = await Apply(new FakeShell(), Def(null, Track(null, hidden), "T"));
        Assert.True(ok.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksOnly, ok.EffectiveDisplayMode);
    }

    [Fact]
    public async Task ForeignLinkAndShellItem_AreReportedButDoNotBlockCommit()
    {
        var shell = new FakeShell();
        shell.Removed.Add(new(RawRemovedKind.Link, @"C:\Other\x.exe", "--something-else"));
        shell.Removed.Add(new(RawRemovedKind.ShellItem));
        var result = await Apply(shell, Def(Category(null, Dest("1")), Track()));
        Assert.True(result.Succeeded);
        Assert.Equal([RemovedDestinationKind.Foreign, RemovedDestinationKind.ForeignItem], result.RemovedDestinations.Select(r => r.Kind));
        Assert.Contains("CommitList", shell.Calls);
    }

    // 7. path change handling
    [Fact]
    public async Task PathMismatch_IsAmbiguousUnlessArgumentsOnlyIsAllowed_AndPathCaseIsIgnored()
    {
        var strict = new FakeShell();
        strict.Removed.Add(RawLink("1", @"C:\Moved\host.exe"));
        var r1 = await Apply(strict, Def(Category(null, Dest("1")), Track()));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationUnidentifiable, r1.Status);
        Assert.Equal(RemovedDestinationKind.Ambiguous, r1.RemovedDestinations.Single().Kind);
        Assert.Contains("AbortList", strict.Calls);
        Assert.DoesNotContain("CommitList", strict.Calls);

        var loose = new FakeShell();
        loose.Removed.Add(RawLink("1", @"C:\Moved\host.exe"));
        var r2 = await Apply(loose, Def(Category(null, Dest("1", JumpDestinationPathPolicy.AllowArgumentsOnly)), Track()));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, r2.Status);
        Assert.Equal(MatchConfidence.ArgumentsOnly, r2.RemovedDestinations.Single().Confidence);

        var cased = new FakeShell();
        cased.Removed.Add(RawLink("1", Samples.Exe.ToUpperInvariant()));
        var r3 = await Apply(cased, Def(Category(null, Dest("1")), Track(["k1"])));
        Assert.True(r3.Succeeded);
        Assert.Equal(MatchConfidence.Exact, r3.RemovedDestinations.Single().Confidence);
    }

    // 8. validation
    [Fact]
    public void Validation_RejectsMissingTracking_Duplicates_AndBadFallback()
    {
        string[] Codes(JumpListDefinition d) => JumpListValidator.Validate(d, Samples.Options()).Select(e => e.Code).ToArray();

        Assert.Contains("Tracking.Required", Codes(Def(Category(null, Dest("1")), null)));
        Assert.Empty(Codes(Def(null, null, "T")));

        var sameKey = new JumpDestinationDefinition { Key = "k1", Link = Samples.Task("Z") };
        Assert.Contains("Destination.KeyDuplicate", Codes(Def(Category(null, Dest("1"), sameKey), Track())));

        var sameArgs = new JumpDestinationDefinition { Key = "other", Link = Samples.Task("D1") };
        Assert.Contains("Category.DuplicateDestination", Codes(Def(Category(null, Dest("1"), sameArgs), Track())));
        var matchSameArgs = new JumpDestinationMatch("m", Samples.Task("D1").Arguments, Samples.Exe);
        Assert.Contains("Category.DuplicateDestination", Codes(Def(Category(null, Dest("1")), Track(null, matchSameArgs))));

        Assert.Contains("Match.PathRequired", Codes(Def(null, Track(null, new JumpDestinationMatch("m", "--x")))));
        Assert.Empty(Codes(Def(null, Track(null, new JumpDestinationMatch("m", "--x", null, JumpDestinationPathPolicy.AllowArgumentsOnly)))));

        Assert.Contains("Category.NameEmpty", Codes(Def(new() { Name = " ", Destinations = [] }, Track())));

        var dupOfNormal = new[] { JumpListEntry.ForTask(Samples.Task("T")) };
        Assert.Contains("Fallback.Invalid", Codes(Def(Category(dupOfNormal, Dest("1")), Track(), "T")));
        var unknownKey = new[] { JumpListEntry.ForKeyedTask("nope", Samples.Task("F")) };
        Assert.Contains("Fallback.Invalid", Codes(Def(Category(unknownKey, Dest("1")), Track())));
    }

    // 9. unreadable
    [Fact]
    public async Task Unreadable_AbortsWithoutCommit_AndReadFailureIsUnidentifiable()
    {
        var shell = new FakeShell();
        shell.Removed.Add(new(RawRemovedKind.Unreadable));
        shell.Removed.Add(RawLink("1"));
        var r = await Apply(shell, Def(Category(null, Dest("1")), Track(["k1"])));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationUnidentifiable, r.Status);
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.Contains("AbortList", shell.Calls);

        var failing = new FakeShell { ReadRemovedHr = Hresults.Fail };
        var r2 = await Apply(failing, Def(Category(null, Dest("1")), Track()));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationUnidentifiable, r2.Status);
        Assert.Equal(JumpListFailurePhase.ReadRemoved, r2.FailurePhase);
        Assert.Equal(Hresults.Fail, r2.HResult);
        Assert.DoesNotContain("CommitList", failing.Calls);
    }

    // 10 + 11. E_ACCESSDENIED: skip vs explicit fallback; separators and duplicates
    [Fact]
    public async Task AccessDenied_WithoutFallback_SkipsCategoryAndStillCommitsTasks()
    {
        var shell = new FakeShell { AppendCategoryHr = Hresults.AccessDenied };
        var r = await Apply(shell, Def(Category(null, Dest("1")), Track(), "T"));
        Assert.True(r.Succeeded);
        Assert.Equal(JumpListDisplayMode.CategorySkipped, r.EffectiveDisplayMode);
        Assert.Equal(JumpListFallbackReason.PrivacyAccessDenied, r.FallbackReason);
        Assert.Equal(["Task T"], shell.Store.Select(t => t.Title));
        Assert.DoesNotContain("AbortList", shell.Calls);
    }

    [Fact]
    public async Task AccessDenied_WithFallback_AddsNormalizedBlock_AndNeverRevivesExcludedKeys()
    {
        var shell = new FakeShell { AppendCategoryHr = Hresults.AccessDenied };
        JumpListEntry?[] fallback =
        [
            Sep(), JumpListEntry.ForKeyedTask("k1", Samples.Task("F1")), Sep(), Sep(),
            JumpListEntry.ForKeyedTask("k2", Samples.Task("F2")), JumpListEntry.ForTask(Samples.Task("F3")), Sep(),
        ];
        var r = await Apply(shell, Def(Category(fallback, Dest("1"), Dest("2")), Track(["k2"]), "T"));
        Assert.True(r.Succeeded);
        Assert.Equal(JumpListDisplayMode.TasksFallback, r.EffectiveDisplayMode);
        Assert.Equal(["Task T", "-", "Task F1", "-", "Task F3"],
            shell.LastEntries.Select(e => e.IsSeparator ? "-" : e.Task!.Title));
        Assert.Equal(["Task T", "Task F1", "Task F3"], shell.Store.Select(t => t.Title));
    }

    [Fact]
    public async Task AccessDenied_FallbackAfterNormalSeparator_DoesNotAddASecondOne()
    {
        var shell = new FakeShell { AppendCategoryHr = Hresults.AccessDenied };
        var def = JumpListDefinition.FromEntries(Aud, [JumpListEntry.ForTask(Samples.Task("T")), Sep()],
            Category([JumpListEntry.ForTask(Samples.Task("F"))], Dest("1")), Track());
        var r = await Apply(shell, def);
        Assert.True(r.Succeeded);
        Assert.Equal(["Task T", "-", "Task F"], shell.LastEntries.Select(e => e.IsSeparator ? "-" : e.Task!.Title));
    }

    // 12. other HRESULTs propagate
    [Fact]
    public async Task OtherAppendFailure_AbortsWithoutFallback()
    {
        var shell = new FakeShell { AppendCategoryHr = Hresults.Custom };
        var fb = new[] { JumpListEntry.ForTask(Samples.Task("F")) };
        var r = await Apply(shell, Def(Category(fb, Dest("1")), Track(), "T"));
        Assert.Equal(JumpListApplyStatus.CategoryAppendFailed, r.Status);
        Assert.Equal(JumpListFailurePhase.AppendCategory, r.FailurePhase);
        Assert.Equal(Hresults.Custom, r.HResult);
        Assert.Equal(JumpListShellState.Unchanged, r.ShellState);
        Assert.Contains("AbortList", shell.Calls);
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.DoesNotContain("AddUserTasks", shell.Calls);

        var both = new FakeShell { AppendCategoryHr = Hresults.Custom, AbortHr = Hresults.Abort };
        var r2 = await Apply(both, Def(Category(null, Dest("1")), Track()));
        Assert.Equal(JumpListApplyStatus.AbortFailed, r2.Status);
        Assert.Equal(Hresults.Custom, r2.PrecedingError!.HResult);

        var create = new FakeShell { CreateDestinationsHr = Hresults.Fail };
        var r3 = await Apply(create, Def(Category(null, Dest("1")), Track()));
        Assert.Equal(JumpListApplyStatus.TaskCreationFailed, r3.Status);
        Assert.DoesNotContain("CommitList", create.Calls);
    }

    // 13. success -> denied -> success: nothing cached
    [Fact]
    public async Task Sequence_SuccessDeniedSuccess_RetriesAppendEveryTime()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var def = Def(Category(null, Dest("1")), Track(), "T");
        var a = await publisher.ApplyAsync(def);
        shell.AppendCategoryHr = Hresults.AccessDenied;
        var b = await publisher.ApplyAsync(def);
        shell.AppendCategoryHr = 0;
        var c = await publisher.ApplyAsync(def);
        Assert.Equal([JumpListDisplayMode.CategoryAndTasks, JumpListDisplayMode.CategorySkipped, JumpListDisplayMode.CategoryAndTasks],
            new[] { a, b, c }.Select(x => x.EffectiveDisplayMode));
        Assert.Equal(3, shell.Calls.Count(x => x == "AppendCategory"));
        Assert.Equal(2, shell.AppendedCategories.Count);
    }

    // 14. state consistency: pending -> persist -> re-apply; commit failure; shutdown
    [Fact]
    public async Task PendingThenReapply_CommitsOnce_AndStateStaysConsistent()
    {
        var shell = new FakeShell();
        shell.Removed.Add(RawLink("1"));
        using var publisher = Samples.Publisher(shell);
        var first = await publisher.ApplyAsync(Def(Category(null, Dest("1"), Dest("2")), Track(), "T"));
        Assert.Equal(JumpListApplyStatus.RemovedDestinationsPending, first.Status);
        Assert.Equal(0, shell.Calls.Count(x => x == "CommitList"));

        string persisted = first.RemovedDestinations.Single(r => r.Kind == RemovedDestinationKind.Matched && !r.AlreadyExcluded).Key!;
        var second = await publisher.ApplyAsync(Def(Category(null, Dest("1"), Dest("2")), Track([persisted]), "T"));
        Assert.True(second.Succeeded);
        Assert.Equal(1, shell.Calls.Count(x => x == "CommitList"));
        Assert.Equal(["Task D2"], shell.AppendedCategories.Single().Links.Select(l => l.Title));

        var sd = await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
        Assert.True(sd.Completed);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
        Assert.Equal(0, shell.DoubleDisposes);
    }

    [Fact]
    public async Task CommitFailure_AfterCategory_IsUnknownAndNotAborted()
    {
        var shell = new FakeShell { CommitHr = Hresults.Fail };
        var r = await Apply(shell, Def(Category(null, Dest("1")), Track(), "T"));
        Assert.Equal(JumpListApplyStatus.CommitFailed, r.Status);
        Assert.Equal(JumpListShellState.Unknown, r.ShellState);
        Assert.DoesNotContain("AbortList", shell.Calls);
        Assert.Equal(JumpListDisplayMode.None, r.EffectiveDisplayMode);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
    }
}
