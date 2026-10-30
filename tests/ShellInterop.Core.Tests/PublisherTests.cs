using ShellInterop.Core.JumpLists;

namespace ShellInterop.Core.Tests;

public class PublisherTests
{
    private static async Task<JumpListApplyResult> Apply(FakeShell shell, params string[] ids)
    {
        using var publisher = Samples.Publisher(shell);
        return await publisher.ApplyAsync(Samples.Definition(ids));
    }

    // T05 / T06 / T07
    [Fact]
    public async Task T05_TaskOrder_IsPreservedToTheShell()
    {
        var shell = new FakeShell();
        var result = await Apply(shell, "C", "A", "B");
        Assert.True(result.Succeeded);
        Assert.Equal(["Task C", "Task A", "Task B"], shell.Store.Select(t => t.Title));
        Assert.Equal(JumpListShellState.Applied, result.ShellState);
        Assert.False(result.VisuallyConfirmed);
    }

    [Fact]
    public async Task T06_GroupSwitch_ReplacesEverything_AndSameContentCanBeReapplied()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);

        Assert.True((await publisher.ApplyAsync(Samples.Definition("A1", "A2"))).Succeeded);
        Assert.Equal(["Task A1", "Task A2"], shell.Store.Select(t => t.Title));

        Assert.True((await publisher.ApplyAsync(Samples.Definition("B1", "B2", "B3"))).Succeeded);
        Assert.Equal(["Task B1", "Task B2", "Task B3"], shell.Store.Select(t => t.Title));

        Assert.True((await publisher.ApplyAsync(Samples.Definition("A1", "A2"))).Succeeded);
        Assert.True((await publisher.ApplyAsync(Samples.Definition("A1", "A2"))).Succeeded);
        Assert.Equal(["Task A1", "Task A2"], shell.Store.Select(t => t.Title));
        Assert.Equal(4, shell.CommittedSets.Count);
    }

    [Fact]
    public async Task T07_EmptyTasks_CommitsEmptyTransaction_WithoutBuildingTasks()
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        var result = await Apply(shell);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.RequestedTaskCount);
        Assert.Empty(shell.Store);
        Assert.Equal(["Create", "SetAppId", "BeginList", "CommitList", "DisposeList"], shell.Calls);
    }

    [Fact]
    public async Task SuccessfulCallOrder_IsCreateSetAppIdBeginTasksAddCommit()
    {
        var shell = new FakeShell();
        await Apply(shell, "A1");
        Assert.Equal(["Create", "SetAppId", "BeginList", "CreateTasks", "AddUserTasks", "CommitList", "DisposeTasks", "DisposeList"], shell.Calls);
        Assert.Equal(Samples.AppId, shell.LastAppId);
    }

    // T08
    [Theory]
    [InlineData("Create", JumpListFailurePhase.CreateList)]
    [InlineData("SetAppId", JumpListFailurePhase.SetAppId)]
    [InlineData("BeginList", JumpListFailurePhase.BeginList)]
    public async Task T08_BeginPhaseFailures_AreReported_WithoutAbort_AndKeepOldList(string failing, JumpListFailurePhase phase)
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        switch (failing)
        {
            case "Create": shell.CreateHr = Hresults.Fail; break;
            case "SetAppId": shell.SetAppIdHr = Hresults.Fail; break;
            default: shell.BeginHr = Hresults.Fail; break;
        }

        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.BeginFailed, result.Status);
        Assert.Equal(phase, result.FailurePhase);
        Assert.Equal(Hresults.Fail, result.HResult);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.DoesNotContain("AbortList", shell.Calls);
        Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
    }

    // T09 / T13
    [Fact]
    public async Task T09_TaskCreationFailure_AbortsOnce_ReportsIndex_AndKeepsOldList()
    {
        var shell = new FakeShell { CreateTasksHr = Hresults.AccessDenied, FailTaskIndex = 1 };
        shell.Seed(Samples.Task("old"));
        var result = await Apply(shell, "A1", "A2");
        Assert.Equal(JumpListApplyStatus.TaskCreationFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.CreateTask, result.FailurePhase);
        Assert.Equal(1, result.Error!.TaskIndex);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Equal(1, shell.Calls.Count(c => c == "AbortList"));
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
    }

    // T10 / T13
    [Fact]
    public async Task T10_AddUserTasksFailure_AbortsBeforeDispose_AndKeepsOldList()
    {
        var shell = new FakeShell { AddTasksHr = Hresults.Custom };
        shell.Seed(Samples.Task("old"));
        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.AddTasksFailed, result.Status);
        Assert.Equal(Hresults.Custom, result.HResult);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.True(shell.Calls.IndexOf("AbortList") > shell.Calls.IndexOf("AddUserTasks"));
        Assert.True(shell.Calls.IndexOf("AbortList") < shell.Calls.IndexOf("DisposeList"));
        Assert.DoesNotContain("CommitList", shell.Calls);
        Assert.Equal(["Task old"], shell.Store.Select(t => t.Title));
    }

    // T11 / T14
    [Fact]
    public async Task T11_CommitFailure_IsUnknownState_AndAbortIsNeverUsedAsRollback()
    {
        var shell = new FakeShell { CommitHr = Hresults.Fail };
        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.CommitFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.CommitList, result.FailurePhase);
        Assert.Equal(JumpListShellState.Unknown, result.ShellState);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("AbortList", shell.Calls);
    }

    // T12
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T12_AbortFailure_IsReported_WithPrecedingError(bool failAtCreateTasks)
    {
        var shell = new FakeShell { AbortHr = Hresults.Abort };
        if (failAtCreateTasks) { shell.CreateTasksHr = Hresults.Fail; } else { shell.AddTasksHr = Hresults.Custom; }

        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.AbortFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.AbortList, result.FailurePhase);
        Assert.Equal(Hresults.Abort, result.HResult);
        Assert.Equal(failAtCreateTasks ? JumpListFailurePhase.CreateTask : JumpListFailurePhase.AddUserTasks, result.PrecedingError!.Phase);
        Assert.Equal(failAtCreateTasks ? Hresults.Fail : Hresults.Custom, result.PrecedingError.HResult);
        Assert.Equal(JumpListShellState.Unknown, result.ShellState);
    }

    // T14
    [Fact]
    public async Task T14_AbortNeverFollowsCommit_InAnyScenario()
    {
        foreach (int commitHr in new[] { 0, Hresults.Fail })
        {
            var shell = new FakeShell { CommitHr = commitHr };
            await Apply(shell, "A1");
            int commit = shell.Calls.IndexOf("CommitList");
            Assert.True(commit >= 0);
            Assert.DoesNotContain("AbortList", shell.Calls.Skip(commit));
        }
    }

    // T18
    [Theory]
    [InlineData("SetAppId", Hresults.AccessDenied)]
    [InlineData("BeginList", Hresults.Custom)]
    [InlineData("CreateTasks", Hresults.Fail)]
    [InlineData("AddUserTasks", Hresults.AccessDenied)]
    [InlineData("CommitList", Hresults.Custom)]
    public async Task T18_HResult_IsPropagatedUnchanged(string stage, int hr)
    {
        var shell = new FakeShell();
        switch (stage)
        {
            case "SetAppId": shell.SetAppIdHr = hr; break;
            case "BeginList": shell.BeginHr = hr; break;
            case "CreateTasks": shell.CreateTasksHr = hr; break;
            case "AddUserTasks": shell.AddTasksHr = hr; break;
            default: shell.CommitHr = hr; break;
        }

        var result = await Apply(shell, "A1");
        Assert.False(result.Succeeded);
        Assert.Equal(hr, result.HResult);
        Assert.Equal($"0x{unchecked((uint)hr):X8}", result.Error!.HResultText);
    }

    [Fact]
    public async Task ValidationFailure_HasNoHResult_ReportedAsUnknownNotGuessed()
    {
        using var publisher = Samples.Publisher(new FakeShell());
        var result = await publisher.ApplyAsync(new JumpListDefinition(null, []));
        Assert.Equal(JumpListApplyStatus.ValidationFailed, result.Status);
        Assert.Null(result.HResult);
        Assert.Equal("UNKNOWN", result.Error!.HResultText);
        Assert.NotEmpty(result.ValidationErrors);
    }

    // unexpected exceptions
    [Fact]
    public async Task UnexpectedExceptionBeforeCommit_Aborts_AndIsReported()
    {
        var shell = new FakeShell { ThrowAt = "AddUserTasks" };
        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.UnknownState, result.Status);
        Assert.Equal(JumpListFailurePhase.Unexpected, result.FailurePhase);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Contains("AbortList", shell.Calls);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
    }

    [Fact]
    public async Task UnexpectedExceptionDuringCommit_IsUnknown_WithoutAbort()
    {
        var shell = new FakeShell { ThrowAt = "CommitList" };
        var result = await Apply(shell, "A1");
        Assert.Equal(JumpListApplyStatus.UnknownState, result.Status);
        Assert.Equal(JumpListShellState.Unknown, result.ShellState);
        Assert.DoesNotContain("AbortList", shell.Calls);
    }

    // T17 (fake side): every handle and list is released exactly once on every path
    [Theory]
    [InlineData("none")]
    [InlineData("Create")]
    [InlineData("SetAppId")]
    [InlineData("BeginList")]
    [InlineData("CreateTasks")]
    [InlineData("AddUserTasks")]
    [InlineData("CommitList")]
    [InlineData("AbortList")]
    [InlineData("throwAdd")]
    [InlineData("throwCommit")]
    public async Task T17_ComObjects_AreReleasedExactlyOnce_OnEveryPath(string scenario)
    {
        var shell = new FakeShell();
        switch (scenario)
        {
            case "Create": shell.CreateHr = Hresults.Fail; break;
            case "SetAppId": shell.SetAppIdHr = Hresults.Fail; break;
            case "BeginList": shell.BeginHr = Hresults.Fail; break;
            case "CreateTasks": shell.CreateTasksHr = Hresults.Fail; break;
            case "AddUserTasks": shell.AddTasksHr = Hresults.Fail; break;
            case "CommitList": shell.CommitHr = Hresults.Fail; break;
            case "AbortList": shell.AddTasksHr = Hresults.Fail; shell.AbortHr = Hresults.Fail; break;
            case "throwAdd": shell.ThrowAt = "AddUserTasks"; break;
            case "throwCommit": shell.ThrowAt = "CommitList"; break;
        }

        await Apply(shell, "A1", "A2");
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
        Assert.Equal(0, shell.DoubleDisposes);
    }

    // process AppUserModelID ordering contract
    [Theory]
    [InlineData(null)]
    [InlineData("Other.Product")]
    public async Task ProcessAppIdNotSetOrDifferent_IsRejected_BeforeAnyShellCall(string? processId)
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell, processAppId: processId);
        var result = await publisher.ApplyAsync(Samples.Definition("A1"));
        Assert.Equal(JumpListApplyStatus.ValidationFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.ProcessAppIdCheck, result.FailurePhase);
        Assert.Empty(shell.Calls);
    }

    [Fact]
    public async Task ProcessAppIdCheck_CanBeRelaxedExplicitly()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell, Samples.Options(requireProcessMatch: false), processAppId: null);
        Assert.True((await publisher.ApplyAsync(Samples.Definition("A1"))).Succeeded);
    }

    // DeleteList
    [Fact]
    public async Task DeleteList_Success_And_Failure()
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        using var publisher = Samples.Publisher(shell);
        var ok = await publisher.DeleteListAsync(Samples.AppId);
        Assert.True(ok.Succeeded);
        Assert.Equal(Samples.AppId, shell.DeletedAppId);
        Assert.Empty(shell.Store);

        shell.DeleteHr = Hresults.Custom;
        var bad = await publisher.DeleteListAsync(Samples.AppId);
        Assert.Equal(JumpListApplyStatus.DeleteFailed, bad.Status);
        Assert.Equal(Hresults.Custom, bad.HResult);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
    }

    [Fact]
    public async Task DeleteList_IsNotCalledByNormalApply()
    {
        var shell = new FakeShell();
        await Apply(shell, "A1");
        await Apply(shell);
        Assert.DoesNotContain("DeleteList", shell.Calls);
    }

    // logging
    [Fact]
    public async Task Log_RecordsEveryPhase_OnTheStaWorker()
    {
        var shell = new FakeShell();
        var log = new ListLog();
        using var publisher = Samples.Publisher(shell, Samples.Options(log));
        await publisher.ApplyAsync(Samples.Definition("A1"));
        var events = log.Entries.Select(e => e.Event).ToList();
        foreach (string expected in new[] { "ApplyStart", "ProcessAppIdCheck", "CreateList", "SetAppID", "BeginList", "CreateTasks", "AddUserTasks", "CommitList", "Release", "ApplyEnd" })
        {
            Assert.Contains(expected, events);
        }

        Assert.All(log.Entries.Where(e => e.Event == "CommitList"), e => Assert.Equal(ApartmentState.STA, e.Apartment));
        Assert.All(log.Entries, e => Assert.Equal(Environment.ProcessId, e.ProcessId));
    }
}
