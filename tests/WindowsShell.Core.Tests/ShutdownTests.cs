using WindowsShell.Core.JumpLists;

namespace WindowsShell.Core.Tests;

/// <summary>A controllable "COM operation": the fake blocks inside it until the test releases it. All waits are bounded.</summary>
internal sealed class OperationGate
{
    private readonly ManualResetEventSlim _entered = new();
    private readonly ManualResetEventSlim _release = new();

    public void Block()
    {
        _entered.Set();
        // Bounded so a broken test can never hang the runner; the assertions would then fail visibly.
        _release.Wait(TimeSpan.FromSeconds(20));
    }

    public void WaitEntered() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "operation never started");

    public void Release() => _release.Set();
}

internal sealed class ActionSink(Action<JumpListLogEntry> action) : IJumpListLogSink
{
    public void Write(JumpListLogEntry entry) => action(entry);
}

public class ShutdownTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static async Task AssertStillPending(Task task)
    {
        // Negative check: the worker is held by the test's gate, so the task cannot complete; 150 ms is only the observation window.
        Task finished = await Task.WhenAny(task, Task.Delay(150));
        Assert.NotSame(task, finished);
    }

    // T01
    [Fact]
    public async Task T01_ShutdownDuringApply_WaitsForTheCommit_AndReturnsTheRealResult()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        var log = new ListLog();
        using var publisher = Samples.Publisher(shell, Samples.Options(log));

        var apply = publisher.ApplyAsync(Samples.Definition("A1"));
        gate.WaitEntered();
        var shutdown = publisher.ShutdownAsync();

        await AssertStillPending(shutdown);
        await AssertStillPending(apply);
        Assert.Equal(JumpListPublisherState.ShuttingDown, publisher.State);

        gate.Release();
        var applied = await apply.WaitAsync(Bound);
        var stopped = await shutdown.WaitAsync(Bound);

        Assert.True(applied.Succeeded);                 // the in-flight transaction ran to its real end
        Assert.Equal(["Task A1"], shell.Store.Select(t => t.Title));
        Assert.True(stopped.Completed);
        Assert.False(stopped.TimedOut);
        Assert.True(stopped.OperationWasInFlight);
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);   // T10: COM released before "stopped"
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
        Assert.Equal(0, shell.DoubleDisposes);
    }

    // T02
    [Fact]
    public async Task T02_ShutdownDuringDeleteList_WaitsForTheDelete()
    {
        var shell = new FakeShell();
        shell.Seed(Samples.Task("old"));
        var gate = new OperationGate();
        shell.OnDelete = gate.Block;
        using var publisher = Samples.Publisher(shell);

        var delete = publisher.DeleteListAsync(Samples.AppId);
        gate.WaitEntered();
        var shutdown = publisher.ShutdownAsync();
        await AssertStillPending(shutdown);

        gate.Release();
        Assert.True((await delete.WaitAsync(Bound)).Succeeded);
        var stopped = await shutdown.WaitAsync(Bound);
        Assert.True(stopped.Completed && stopped.OperationWasInFlight);
        Assert.Empty(shell.Store);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
    }

    // T03 + T09
    [Fact]
    public async Task T03_QueuedUnstartedRequests_AreCompletedAsDisposed_AndNeverReachTheShell()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        using var publisher = Samples.Publisher(shell);

        var running = publisher.ApplyAsync(Samples.Definition("RUN"));
        gate.WaitEntered();
        var queued = new[]
        {
            publisher.ApplyAsync(Samples.Definition("Q1")),
            publisher.ApplyAsync(Samples.Definition("Q2")),
            publisher.DeleteListAsync(Samples.AppId),
        };

        var shutdown = publisher.ShutdownAsync();
        gate.Release();

        var all = await Task.WhenAll(queued).WaitAsync(Bound);     // T09: nothing is left incomplete
        Assert.All(all, r => Assert.Equal(JumpListApplyStatus.PublisherDisposed, r.Status));
        Assert.All(all, r => Assert.Equal(JumpListShellState.Unchanged, r.ShellState));
        Assert.True((await running.WaitAsync(Bound)).Succeeded);
        var stopped = await shutdown.WaitAsync(Bound);
        Assert.Equal(3, stopped.DiscardedPendingRequests);
        Assert.Equal(1, shell.ListsCreated);                        // only the running one ever touched the Shell
        Assert.Equal(["Task RUN"], shell.Store.Select(t => t.Title));
        Assert.DoesNotContain("DeleteList", shell.Calls);
    }

    // T04
    [Fact]
    public async Task T04_ConcurrentShutdownRequests_AreSafe_AndWorkerStopIsLoggedOnce()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        var log = new ListLog();
        var publisher = Samples.Publisher(shell, Samples.Options(log));

        var apply = publisher.ApplyAsync(Samples.Definition("A1"));
        gate.WaitEntered();
        var shutdowns = Enumerable.Range(0, 6).Select(_ => Task.Run(() => publisher.ShutdownAsync())).ToArray();
        var disposer = Task.Run(publisher.Dispose);
        gate.Release();

        var results = await Task.WhenAll(shutdowns).WaitAsync(Bound);
        await disposer.WaitAsync(Bound);
        Assert.All(results, r => Assert.True(r.Completed));
        Assert.True((await apply.WaitAsync(Bound)).Succeeded);
        Assert.Equal(1, log.Entries.Count(e => e.Event == "WorkerStop"));
        Assert.Equal(1, log.Entries.Count(e => e.Event == "ShutdownRequested"));
        publisher.Dispose();                                         // after stop: still a harmless no-op
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
    }

    // T05 + T06
    [Fact]
    public async Task T05_T06_RequestsAfterShutdownBegan_AreRejected_BothWhileDrainingAndAfterStop()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        using var publisher = Samples.Publisher(shell);

        var running = publisher.ApplyAsync(Samples.Definition("RUN"));
        gate.WaitEntered();
        var shutdown = publisher.ShutdownAsync();

        var applyDuring = await publisher.ApplyAsync(Samples.Definition("LATE")).WaitAsync(Bound);
        var deleteDuring = await publisher.DeleteListAsync(Samples.AppId).WaitAsync(Bound);
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, applyDuring.Status);
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, deleteDuring.Status);

        gate.Release();
        await running.WaitAsync(Bound);
        await shutdown.WaitAsync(Bound);

        Assert.Equal(JumpListApplyStatus.PublisherDisposed, (await publisher.ApplyAsync(Samples.Definition("LATER"))).Status);
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, (await publisher.DeleteListAsync(Samples.AppId)).Status);
        Assert.Equal(1, shell.ListsCreated);
        Assert.DoesNotContain("DeleteList", shell.Calls);
    }

    // T07
    [Fact]
    public async Task T07_ShutdownTimeout_IsReportedHonestly_NothingForced_AndCanBeResumed()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        using var publisher = Samples.Publisher(shell);

        var apply = publisher.ApplyAsync(Samples.Definition("A1"));
        gate.WaitEntered();

        var first = await publisher.ShutdownAsync(TimeSpan.FromMilliseconds(80)).WaitAsync(Bound);
        Assert.False(first.Completed);
        Assert.True(first.TimedOut);
        Assert.True(first.OperationWasInFlight);
        Assert.Equal(JumpListPublisherState.ShuttingDown, publisher.State);   // NOT reported as stopped
        Assert.False(apply.IsCompleted);                                       // the transaction was not cut off

        gate.Release();
        var second = await publisher.ShutdownAsync(Bound).WaitAsync(Bound);
        Assert.True(second.Completed);
        Assert.True((await apply.WaitAsync(Bound)).Succeeded);
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
    }

    [Fact]
    public async Task T07_SyncDisposeTimeout_DoesNotHang_DoesNotClaimStopped_AndIsLogged()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        var log = new ListLog();
        var options = new JumpListPublisherOptions
        {
            RequireExistingFiles = false,
            LogSink = log,
            DisposeTimeout = TimeSpan.FromMilliseconds(100),
        };
        var publisher = Samples.Publisher(shell, options);

        var apply = publisher.ApplyAsync(Samples.Definition("A1"));
        gate.WaitEntered();
        publisher.Dispose();                                                   // returns after ~100 ms, worker still inside Commit

        Assert.Equal(JumpListPublisherState.ShuttingDown, publisher.State);
        Assert.Contains(log.Entries, e => e.Event == "DisposeTimedOut");
        Assert.False(apply.IsCompleted);

        gate.Release();
        Assert.True((await apply.WaitAsync(Bound)).Succeeded);
        Assert.True((await publisher.ShutdownAsync(Bound)).Completed);
    }

    [Fact]
    public async Task T07_CancelDuringCommit_NeverReportsUnchanged_TheRealOutcomeIsReturned()
    {
        var shell = new FakeShell();
        var gate = new OperationGate();
        shell.OnCommit = gate.Block;
        using var publisher = Samples.Publisher(shell);
        using var cts = new CancellationTokenSource();

        var apply = publisher.ApplyAsync(Samples.Definition("A1"), cts.Token);
        gate.WaitEntered();
        cts.Cancel();                       // token is NOT a COM interrupt
        gate.Release();

        var result = await apply.WaitAsync(Bound);
        Assert.Equal(JumpListApplyStatus.Success, result.Status);
        Assert.Equal(JumpListShellState.Applied, result.ShellState);
        Assert.Equal(["Task A1"], shell.Store.Select(t => t.Title));
    }

    // T08
    [Fact]
    public async Task T08_WorkerThreadExits_AfterShutdown()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        await publisher.ApplyAsync(Samples.Definition("A1"));
        Assert.True(publisher.WorkerIsAlive);

        Assert.True((await publisher.ShutdownAsync(Bound)).Completed);
        Assert.True(SpinWait.SpinUntil(() => !publisher.WorkerIsAlive, Bound), "STA worker did not exit");
    }

    [Fact]
    public async Task ShutdownOfAnIdlePublisher_CompletesImmediately_WithNothingInFlight()
    {
        using var publisher = Samples.Publisher(new FakeShell());
        var r = await publisher.ShutdownAsync(Bound).WaitAsync(Bound);
        Assert.True(r.Completed);
        Assert.False(r.OperationWasInFlight);
        Assert.Equal(0, r.DiscardedPendingRequests);
    }

    // T11
    [Fact]
    public async Task T11_DisposeAndShutdownCalledFromTheWorkerThread_DoNotDeadlock()
    {
        var shell = new FakeShell();
        JumpListPublisher? publisher = null;
        Task<JumpListShutdownResult>? inner = null;
        var sink = new ActionSink(e =>
        {
            if (e.Event == "CommitList" && publisher is not null && inner is null)
            {
                inner = publisher.ShutdownAsync();      // on the worker: must not wait for itself
                publisher.Dispose();                    // on the worker: must not Join itself
            }
        });
        publisher = Samples.Publisher(shell, Samples.Options(sink));

        var result = await publisher.ApplyAsync(Samples.Definition("A1")).WaitAsync(Bound);
        Assert.True(result.Succeeded);                  // shutdown requested mid-transaction; the transaction still completed
        var selfCall = await inner!.WaitAsync(Bound);
        Assert.False(selfCall.Completed);               // honest: it could not have waited for its own exit
        Assert.True((await publisher.ShutdownAsync(Bound)).Completed);
        Assert.Equal(JumpListPublisherState.Stopped, publisher.State);
    }

}
