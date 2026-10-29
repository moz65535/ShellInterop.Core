using System.Reflection;
using WindowsShell.Core.JumpLists;
using WindowsShell.Core.JumpLists.Native;

namespace WindowsShell.Core.Tests;

public enum InjectionPoint
{
    /// <summary>BeginList has succeeded; the very next step (building the shell links) fails with an HRESULT.</summary>
    CreateTasks,
    /// <summary>BeginList and CreateTasks succeeded; AddUserTasks fails.</summary>
    AddUserTasks,
    /// <summary>BeginList succeeded; the next call throws an unexpected exception.</summary>
    ExceptionAfterBeginList,
    /// <summary>Everything succeeded up to CommitList, which fails (post-commit semantics, not an abort scenario).</summary>
    CommitList,
}

/// <summary>
/// TEST-ONLY decorator over the internal native seam: injects one known failure at one known point, exactly once.
/// It lives in the test assembly, so nothing like it exists in the shipped Core (verified by T36).
/// </summary>
internal sealed class FaultInjectingFactory(IJumpListNativeFactory inner, InjectionPoint point, int hresult) : IJumpListNativeFactory
{
    public int Fired;

    public int Create(out IJumpListNative? list)
    {
        int hr = inner.Create(out IJumpListNative? real);
        list = real is null ? null : new Faulting(real, this);
        return hr;
    }

    private bool ShouldFire(InjectionPoint at) => at == point && Interlocked.CompareExchange(ref Fired, 1, 0) == 0;

    private sealed class Faulting(IJumpListNative real, FaultInjectingFactory owner) : IJumpListNative
    {
        public int SetAppId(string appId) => real.SetAppId(appId);

        public int BeginList() => real.BeginList();

        public int CreateTasks(IReadOnlyList<JumpTaskDefinition> tasks, out ITaskCollectionHandle? collection, out int failedTaskIndex)
        {
            if (owner.ShouldFire(InjectionPoint.ExceptionAfterBeginList))
            {
                throw new InvalidOperationException("injected exception after BeginList");
            }

            if (owner.ShouldFire(InjectionPoint.CreateTasks))
            {
                collection = null;
                failedTaskIndex = 0;
                return owner.InjectedHResult;
            }

            return real.CreateTasks(tasks, out collection, out failedTaskIndex);
        }

        public int AddUserTasks(ITaskCollectionHandle collection) => owner.ShouldFire(InjectionPoint.AddUserTasks) ? owner.InjectedHResult : real.AddUserTasks(collection);

        public int CommitList() => owner.ShouldFire(InjectionPoint.CommitList) ? owner.InjectedHResult : real.CommitList();

        public int AbortList() => real.AbortList();

        public int DeleteList(string appId) => real.DeleteList(appId);

        public void Dispose() => real.Dispose();
    }

    private int InjectedHResult => hresult;
}

public class FailureInjectionTests
{
    private static JumpListPublisher PublisherWith(FaultInjectingFactory factory)
        => new(Samples.Options(), factory, new FakeProcessAppId(Samples.AppId));

    private static async Task<(FakeShell Shell, FaultInjectingFactory Factory, JumpListApplyResult Result)> ApplyBAfterA(InjectionPoint point, int hr = Hresults.Fail, int abortHr = 0)
    {
        var shell = new FakeShell();
        var factory = new FaultInjectingFactory(shell, point, hr);
        using var publisher = PublisherWith(factory);

        // The previously committed list "A" is seeded directly into the fake store; the next request (B) then hits the injected failure.
        shell.Seed(Samples.Task("A1"), Samples.Task("A2"));
        shell.AbortHr = abortHr;
        var result = await publisher.ApplyAsync(Samples.Definition("B1", "B2", "B3"));
        return (shell, factory, result);
    }

    // T29
    [Fact]
    public async Task T29_FailureRightAfterBeginList_AtCreateTasks_IsAbortedAndKeepsTheOldList()
    {
        var (shell, factory, r) = await ApplyBAfterA(InjectionPoint.CreateTasks);
        Assert.Equal(1, factory.Fired);
        Assert.Equal(JumpListApplyStatus.TaskCreationFailed, r.Status);
        Assert.Equal(JumpListFailurePhase.CreateTask, r.FailurePhase);
        Assert.Equal(Hresults.Fail, r.HResult);
        Assert.Equal(["Task A1", "Task A2"], shell.Store.Select(t => t.Title));      // previous list retained (fake store)
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    // T30
    [Fact]
    public async Task T30_FailureAtAddUserTasks_AfterTasksWereBuilt_IsAbortedAndKeepsTheOldList()
    {
        var (shell, factory, r) = await ApplyBAfterA(InjectionPoint.AddUserTasks, Hresults.AccessDenied);
        Assert.Equal(1, factory.Fired);
        Assert.Equal(JumpListApplyStatus.AddTasksFailed, r.Status);
        Assert.Equal(Hresults.AccessDenied, r.HResult);
        Assert.Equal(["Task A1", "Task A2"], shell.Store.Select(t => t.Title));
        Assert.DoesNotContain("CommitList", shell.Calls);
    }

    // T31: BeginList really succeeded first, then the failure, then exactly one AbortList
    [Theory]
    [InlineData(InjectionPoint.CreateTasks)]
    [InlineData(InjectionPoint.AddUserTasks)]
    public async Task T31_BeginListSucceeded_ThenInjectedFailure_ThenExactlyOneAbort(InjectionPoint point)
    {
        var (shell, _, _) = await ApplyBAfterA(point);
        int begin = shell.Calls.IndexOf("BeginList");
        int abort = shell.Calls.IndexOf("AbortList");
        Assert.True(begin >= 0 && abort > begin, "AbortList must follow a successful BeginList");
        Assert.Equal(1, shell.Calls.Count(c => c == "AbortList"));
        Assert.Equal(0, shell.BeginHr);                                  // BeginList itself did not fail: this is not a validation/begin failure
    }

    // T32
    [Theory]
    [InlineData(InjectionPoint.CreateTasks, JumpListApplyStatus.TaskCreationFailed)]
    [InlineData(InjectionPoint.AddUserTasks, JumpListApplyStatus.AddTasksFailed)]
    public async Task T32_AbortSucceeds_ClassifiedAsTheOriginalFailure_WithUnchangedState(InjectionPoint point, JumpListApplyStatus expected)
    {
        var (_, _, r) = await ApplyBAfterA(point);
        Assert.Equal(expected, r.Status);
        Assert.Equal(JumpListShellState.Unchanged, r.ShellState);
        Assert.Null(r.PrecedingError);
        Assert.False(r.Succeeded);
        Assert.False(r.VisuallyConfirmed);   // the fake proves the contract only; the real Shell still has to be observed on a real Windows installation
    }

    // T33
    [Theory]
    [InlineData(InjectionPoint.CreateTasks, JumpListFailurePhase.CreateTask)]
    [InlineData(InjectionPoint.AddUserTasks, JumpListFailurePhase.AddUserTasks)]
    public async Task T33_AbortFails_IsAbortFailed_UnknownState_AndKeepsTheOriginalCause(InjectionPoint point, JumpListFailurePhase originalPhase)
    {
        var (shell, _, r) = await ApplyBAfterA(point, Hresults.Custom, abortHr: Hresults.Abort);
        Assert.Equal(JumpListApplyStatus.AbortFailed, r.Status);
        Assert.Equal(JumpListShellState.Unknown, r.ShellState);
        Assert.Equal(Hresults.Abort, r.HResult);
        Assert.Equal(originalPhase, r.PrecedingError!.Phase);
        Assert.Equal(Hresults.Custom, r.PrecedingError.HResult);
        Assert.Equal(1, shell.Calls.Count(c => c == "AbortList"));
    }

    // T34
    [Theory]
    [InlineData(InjectionPoint.CreateTasks, 0)]
    [InlineData(InjectionPoint.AddUserTasks, 0)]
    [InlineData(InjectionPoint.AddUserTasks, Hresults.Abort)]
    [InlineData(InjectionPoint.ExceptionAfterBeginList, 0)]
    [InlineData(InjectionPoint.CommitList, 0)]
    public async Task T34_EveryComObjectIsReleasedExactlyOnce_AfterAnInjectedFailure(InjectionPoint point, int abortHr)
    {
        var (shell, factory, _) = await ApplyBAfterA(point, abortHr: abortHr);
        Assert.Equal(1, factory.Fired);
        Assert.Equal(shell.ListsCreated, shell.ListsDisposed);
        Assert.Equal(shell.HandlesCreated, shell.HandlesDisposed);
        Assert.Equal(0, shell.DoubleDisposes);
        // Abort (when used) is issued before the list is released.
        int abort = shell.Calls.IndexOf("AbortList");
        Assert.True(abort < 0 || abort < shell.Calls.IndexOf("DisposeList"));
    }

    [Fact]
    public async Task UnexpectedExceptionRightAfterBeginList_IsAborted_AndReportedAsUnknownState()
    {
        var (shell, _, r) = await ApplyBAfterA(InjectionPoint.ExceptionAfterBeginList);
        Assert.Equal(JumpListApplyStatus.UnknownState, r.Status);
        Assert.Equal(JumpListFailurePhase.Unexpected, r.FailurePhase);
        Assert.Equal(JumpListShellState.Unchanged, r.ShellState);      // abort succeeded, commit never ran
        Assert.Equal(1, shell.Calls.Count(c => c == "AbortList"));
        Assert.Equal(["Task A1", "Task A2"], shell.Store.Select(t => t.Title));
    }

    // T35
    [Fact]
    public async Task T35_CommitFailure_IsNotRolledBackByAbort_AndIsUnknown()
    {
        var (shell, _, r) = await ApplyBAfterA(InjectionPoint.CommitList);
        Assert.Equal(JumpListApplyStatus.CommitFailed, r.Status);
        Assert.Equal(JumpListShellState.Unknown, r.ShellState);
        Assert.DoesNotContain("AbortList", shell.Calls);
        Assert.Contains("AddUserTasks", shell.Calls);                     // everything before the commit did happen
    }

    [Fact]
    public async Task OneShotInjection_RecoversOnTheNextApply()
    {
        var shell = new FakeShell();
        var factory = new FaultInjectingFactory(shell, InjectionPoint.AddUserTasks, Hresults.Fail);
        using var publisher = PublisherWith(factory);
        shell.Seed(Samples.Task("A1"));

        Assert.False((await publisher.ApplyAsync(Samples.Definition("B1"))).Succeeded);
        Assert.Equal(["Task A1"], shell.Store.Select(t => t.Title));          // S10 analogue: old list survives
        Assert.True((await publisher.ApplyAsync(Samples.Definition("B1"))).Succeeded);   // S11 analogue: re-apply works
        Assert.Equal(["Task B1"], shell.Store.Select(t => t.Title));
        Assert.Equal(1, factory.Fired);
    }

    // T36
    [Fact]
    public void T36_ProductionPath_ContainsNoFaultInjection()
    {
        var core = typeof(JumpListPublisher).Assembly;
        string[] suspicious = ["Inject", "Fault", "Chaos"];

        foreach (Type t in core.GetTypes())
        {
            Assert.DoesNotContain(suspicious, s => t.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(suspicious, s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
            }
        }

        // The public constructor wires the real Shell factory, never a decorator, and options expose no injection knob.
        using var publisher = new JumpListPublisher(new JumpListPublisherOptions { RequireProcessAppIdMatch = false });
        object? factory = typeof(JumpListPublisher).GetField("_factory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(publisher);
        Assert.IsType<ShellJumpListNativeFactory>(factory);
        Assert.DoesNotContain(typeof(JumpListPublisherOptions).GetProperties(), p => suspicious.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(typeof(IJumpListPublisher).GetMembers(), m => suspicious.Any(s => m.Name.Contains(s, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task HealthyRun_ThroughTheDecorator_FiresNothing()
    {
        var shell = new FakeShell();
        var factory = new FaultInjectingFactory(shell, InjectionPoint.CreateTasks, Hresults.Fail);
        using var publisher = PublisherWith(factory);
        // Zero tasks never reaches CreateTasks, so an armed injection point is simply not hit.
        Assert.True((await publisher.ApplyAsync(Samples.Definition())).Succeeded);
        Assert.Equal(0, factory.Fired);
    }
}
