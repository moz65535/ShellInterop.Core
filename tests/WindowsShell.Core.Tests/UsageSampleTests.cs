using WindowsShell.Core.JumpLists;

namespace WindowsShell.Core.Tests;

/// <summary>
/// Behaviour checks for the call sequence shown in docs/USAGE-AVALONIA.md, run against the fake Shell. They use the
/// internal test constructor (via Samples.Publisher), so they are NOT a compile check of the public API: that is done by
/// building the documented sample as a separate project. A pass here is never evidence of a real Shell success, and
/// ProcessAppIdentity.Set is deliberately not called.
/// </summary>
public class UsageSampleTests
{
    private static JumpListDefinition TwoTasks(string appId) => new(appId,
    [
        new JumpTaskDefinition
        {
            Title = "New window",
            ApplicationPath = Samples.Exe,
            Arguments = "--new-window",
            IconResourcePath = Samples.Exe,
            IconResourceIndex = 0,
            WorkingDirectory = @"C:\PocTest",
        },
        new JumpTaskDefinition
        {
            Title = "Settings",
            Description = "Open the settings",
            ApplicationPath = Samples.Exe,
            Arguments = "--settings",
            IconResourcePath = Samples.Exe,
        },
    ]);

    [Fact]
    public async Task ApplyTwoTasks_Succeeds_AndReportsAppliedButNotVisuallyConfirmed()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);

        JumpListApplyResult result = await publisher.ApplyAsync(TwoTasks(Samples.AppId));

        Assert.True(result.Succeeded);
        Assert.Equal(JumpListApplyStatus.Success, result.Status);
        Assert.Equal(JumpListFailurePhase.None, result.FailurePhase);
        Assert.Null(result.HResult);
        Assert.Equal(2, result.RequestedTaskCount);
        Assert.Equal(JumpListShellState.Applied, result.ShellState);
        Assert.False(result.VisuallyConfirmed);
        Assert.Equal(["New window", "Settings"], shell.Store.Select(t => t.Title));
        Assert.Equal(Samples.AppId, shell.LastAppId);
    }

    [Fact]
    public async Task ApplyEmptyTasks_ClearsTheStoredTasks()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        Assert.True((await publisher.ApplyAsync(TwoTasks(Samples.AppId))).Succeeded);

        JumpListApplyResult result = await publisher.ApplyAsync(new JumpListDefinition(Samples.AppId, []));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.RequestedTaskCount);
        Assert.Equal(JumpListShellState.Applied, result.ShellState);
        Assert.Empty(shell.Store);
    }

    [Fact]
    public async Task ProcessAppIdMismatch_IsValidationFailed_AndNeverReachesTheShell()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell, processAppId: "Other.Application");

        JumpListApplyResult result = await publisher.ApplyAsync(TwoTasks(Samples.AppId));

        Assert.False(result.Succeeded);
        Assert.Equal(JumpListApplyStatus.ValidationFailed, result.Status);
        Assert.Equal(JumpListFailurePhase.ProcessAppIdCheck, result.FailurePhase);
        Assert.Equal(JumpListShellState.Unchanged, result.ShellState);
        Assert.Empty(shell.Calls);
    }

    [Fact]
    public async Task ShutdownAsync_Completes_ThenDisposeReturnsImmediately()
    {
        var shell = new FakeShell();
        var publisher = Samples.Publisher(shell);
        Assert.True((await publisher.ApplyAsync(TwoTasks(Samples.AppId))).Succeeded);

        JumpListShutdownResult shutdown = await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));

        Assert.True(shutdown.Completed);
        Assert.False(shutdown.TimedOut);
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);

        publisher.Dispose();   // worker already exited
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
    }

    [Fact]
    public async Task DisposeWithoutShutdown_StopsThePublisher_AndLaterRequestsAreRejected()
    {
        var shell = new FakeShell();
        var publisher = Samples.Publisher(shell);
        Assert.True((await publisher.ApplyAsync(TwoTasks(Samples.AppId))).Succeeded);

        publisher.Dispose();

        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
        JumpListApplyResult late = await publisher.ApplyAsync(TwoTasks(Samples.AppId));
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, late.Status);
        Assert.Equal(JumpListShellState.Unchanged, late.ShellState);
    }
}
