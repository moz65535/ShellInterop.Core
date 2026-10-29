using WindowsShell.Core.JumpLists;

namespace WindowsShell.Core.Tests;

public class ThreadingAndSafetyTests
{
    private sealed class ShutdownRequestedSignal : IJumpListLogSink
    {
        public ManualResetEventSlim Signal { get; } = new();

        public void Write(JumpListLogEntry entry)
        {
            if (entry.Event == "ShutdownRequested")
            {
                Signal.Set();
            }
        }
    }

    // T16
    [Fact]
    public async Task T16_AllShellCalls_RunOnOneDedicatedStaThread_NotTheCaller()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        await publisher.ApplyAsync(Samples.Definition("A1"));
        await publisher.ApplyAsync(Samples.Definition("B1"));
        await publisher.DeleteListAsync(Samples.AppId);

        Assert.NotEmpty(shell.Threads);
        Assert.All(shell.Threads, t => Assert.Equal(ApartmentState.STA, t.Apartment));
        Assert.Single(shell.Threads.Select(t => t.ThreadId).Distinct());
        Assert.Equal(publisher.WorkerThreadId, shell.Threads[0].ThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, publisher.WorkerThreadId);
    }

    [Fact]
    public async Task T16_CallerApartment_DoesNotMatter()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var tcs = new TaskCompletionSource<JumpListApplyResult>();
        var mta = new Thread(() => tcs.SetResult(publisher.ApplyAsync(Samples.Definition("A1")).GetAwaiter().GetResult()));
        mta.SetApartmentState(ApartmentState.MTA);
        mta.Start();
        Assert.True((await tcs.Task).Succeeded);
        Assert.All(shell.Threads, t => Assert.Equal(ApartmentState.STA, t.Apartment));
    }

    // T15
    [Fact]
    public async Task T15_ConcurrentRequests_AreSerialized_InSubmissionOrder()
    {
        var shell = new FakeShell();
        using var publisher = Samples.Publisher(shell);
        var tasks = Enumerable.Range(0, 30)
            .Select(i => publisher.ApplyAsync(Samples.Definition("T" + i)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, shell.MaxConcurrentLists);
        Assert.Equal(Enumerable.Range(0, 30).Select(i => "Task T" + i), shell.CommittedSets.Select(s => s.Single().Title));
    }

    [Fact]
    public async Task T15_ParallelCallers_NeverOverlapInsideTheShell()
    {
        var shell = new FakeShell { OnCommit = () => Thread.Sleep(2) };
        using var publisher = Samples.Publisher(shell);
        var all = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => publisher.ApplyAsync(Samples.Definition("P" + i)))));
        Assert.All(all, r => Assert.True(r.Succeeded));
        Assert.Equal(1, shell.MaxConcurrentLists);
    }

    [Fact]
    public async Task Cancellation_BeforeStart_And_WhileQueued()
    {
        var shell = new FakeShell();
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        shell.OnCommit = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
        using var publisher = Samples.Publisher(shell);

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        Assert.Equal(JumpListApplyStatus.Cancelled, (await publisher.ApplyAsync(Samples.Definition("X"), preCancelled.Token)).Status);
        Assert.Empty(shell.Calls);

        var first = publisher.ApplyAsync(Samples.Definition("FIRST"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        using var cts = new CancellationTokenSource();
        var queued = publisher.ApplyAsync(Samples.Definition("QUEUED"), cts.Token);
        cts.Cancel();
        release.Set();

        Assert.True((await first).Succeeded);
        Assert.Equal(JumpListApplyStatus.Cancelled, (await queued).Status);
        Assert.Equal(JumpListShellState.Unchanged, (await queued).ShellState);
        Assert.Equal(["Task FIRST"], shell.Store.Select(t => t.Title));
    }

    [Fact]
    public async Task Dispose_CompletesPendingRequests_AndRejectsNewOnes()
    {
        var shell = new FakeShell();
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        shell.OnCommit = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
        var shutdownRequested = new ShutdownRequestedSignal();
        var publisher = Samples.Publisher(shell, Samples.Options(log: shutdownRequested));

        var running = publisher.ApplyAsync(Samples.Definition("RUN"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var pending = publisher.ApplyAsync(Samples.Definition("PENDING"));

        var disposer = Task.Run(publisher.Dispose);
        // Release the in-flight commit only after Dispose has marked the publisher as shutting down (the "ShutdownRequested"
        // entry is written after that state change). Otherwise the worker could take PENDING before shutdown began.
        Assert.True(shutdownRequested.Signal.Wait(TimeSpan.FromSeconds(10)));
        release.Set();
        await disposer;

        Assert.True((await running).Succeeded);
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, (await pending).Status);
        Assert.Equal(JumpListApplyStatus.PublisherDisposed, (await publisher.ApplyAsync(Samples.Definition("LATE"))).Status);
        publisher.Dispose(); // idempotent
        Assert.Equal(["Task RUN"], shell.Store.Select(t => t.Title));
    }

    [Fact]
    public async Task Timing_IsMeasured()
    {
        var shell = new FakeShell { OnCommit = () => Thread.Sleep(20) };
        using var publisher = Samples.Publisher(shell);
        var result = await publisher.ApplyAsync(Samples.Definition("A1"));
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(15));
        Assert.True(result.QueueWait >= TimeSpan.Zero);
    }

}
