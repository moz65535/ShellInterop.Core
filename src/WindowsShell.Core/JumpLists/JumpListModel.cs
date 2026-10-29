namespace WindowsShell.Core.JumpLists;

/// <summary>One entry of the Jump List "Tasks" category. Immutable.</summary>
public sealed record JumpTaskDefinition
{
    /// <summary>Display name (System.Title). Required.</summary>
    public required string Title { get; init; }

    /// <summary>Optional tooltip.</summary>
    public string? Description { get; init; }

    /// <summary>Absolute path of the executable the task launches.</summary>
    public required string ApplicationPath { get; init; }

    /// <summary>Command-line arguments. The Shell requires a non-empty value for every task.</summary>
    public required string Arguments { get; init; }

    /// <summary>Optional absolute working directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Absolute path of the file that holds the icon (.exe/.dll/.ico). The Shell requires an icon location.</summary>
    public required string IconResourcePath { get; init; }

    public int IconResourceIndex { get; init; }
}

/// <summary>The complete desired Tasks list for one AppUserModelID. Applying it replaces whatever Tasks existed.</summary>
public sealed class JumpListDefinition
{
    public JumpListDefinition(string? appUserModelId, IEnumerable<JumpTaskDefinition?>? tasks)
    {
        AppUserModelId = appUserModelId;
        // Defensive copy: later changes to the caller's collection must not leak into an in-flight request.
        Tasks = Array.AsReadOnly(tasks is null ? [] : tasks.ToArray());
    }

    /// <summary>Explicit AppUserModelID. Never inferred.</summary>
    public string? AppUserModelId { get; }

    /// <summary>Ordered tasks; order is preserved all the way to the Shell. May be empty (clears Tasks).</summary>
    public IReadOnlyList<JumpTaskDefinition?> Tasks { get; }
}

public enum JumpListApplyStatus
{
    Success,
    ValidationFailed,
    /// <summary>CoCreate / SetAppID / BeginList failed. No update session exists, nothing to abort.</summary>
    BeginFailed,
    TaskCreationFailed,
    AddTasksFailed,
    /// <summary>CommitList returned a failure. The Shell state is reported as Unknown; AbortList is deliberately not used.</summary>
    CommitFailed,
    /// <summary>A pre-commit failure occurred and AbortList itself failed.</summary>
    AbortFailed,
    DeleteFailed,
    /// <summary>An unexpected exception left the outcome indeterminate.</summary>
    UnknownState,
    Cancelled,
    PublisherDisposed,
}

public enum JumpListFailurePhase
{
    None,
    Validation,
    ProcessAppIdCheck,
    CreateList,
    SetAppId,
    BeginList,
    CreateTask,
    AddUserTasks,
    CommitList,
    AbortList,
    DeleteList,
    Unexpected,
}

/// <summary>What the library can claim about the Shell's stored list after the call.</summary>
public enum JumpListShellState
{
    /// <summary>No change was committed (validation/begin failure, or a pre-commit failure followed by a successful AbortList).</summary>
    Unchanged,
    /// <summary>The Shell API reported success. This is NOT visual confirmation of the taskbar.</summary>
    Applied,
    Unknown,
}

public sealed record JumpListValidationError(string Code, string Message, int? TaskIndex = null);

public sealed record JumpListError(JumpListFailurePhase Phase, int? HResult, string Message, int? TaskIndex = null)
{
    public string HResultText => HResult is { } h ? $"0x{unchecked((uint)h):X8}" : "UNKNOWN";
}

public sealed record JumpListApplyResult
{
    public required JumpListApplyStatus Status { get; init; }

    public bool Succeeded => Status == JumpListApplyStatus.Success;

    public JumpListFailurePhase FailurePhase => Error?.Phase ?? JumpListFailurePhase.None;

    /// <summary>HRESULT of the primary failure; null when not applicable or not obtainable (reported as UNKNOWN, never guessed).</summary>
    public int? HResult => Error?.HResult;

    public int RequestedTaskCount { get; init; }

    public JumpListShellState ShellState { get; init; } = JumpListShellState.Unknown;

    public JumpListError? Error { get; init; }

    /// <summary>For AbortFailed: the failure that made the abort necessary.</summary>
    public JumpListError? PrecedingError { get; init; }

    public IReadOnlyList<JumpListValidationError> ValidationErrors { get; init; } = [];

    /// <summary>Non-fatal observations such as COM cleanup problems.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public TimeSpan QueueWait { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Always false: the library cannot observe the taskbar. Whether the entries really appear needs a human check on the real Windows Shell.</summary>
    public bool VisuallyConfirmed => false;
}

public enum JumpListPublisherState
{
    Running,

    /// <summary>No new requests are accepted; an in-flight COM transaction (if any) is still finishing.</summary>
    ShuttingDown,

    /// <summary>The STA worker has exited and every COM reference has been released.</summary>
    Stopped,
}

/// <summary>Outcome of <see cref="IJumpListPublisher.ShutdownAsync"/>. A timeout is reported as such, never as success.</summary>
public sealed record JumpListShutdownResult(
    bool Completed,
    bool TimedOut,
    bool OperationWasInFlight,
    int DiscardedPendingRequests,
    TimeSpan Elapsed);

public interface IJumpListPublisher : IDisposable
{
    JumpListPublisherState State { get; }

    /// <summary>
    /// Graceful stop: rejects new requests, completes queued-but-unstarted requests as PublisherDisposed, WAITS for the
    /// in-flight COM transaction to finish and release its objects, then waits for the STA worker to exit.
    /// Never aborts a thread or interrupts COM. If the timeout elapses first, <see cref="JumpListShutdownResult.Completed"/>
    /// is false and the publisher stays in ShuttingDown; call again to keep waiting. Safe to call repeatedly and concurrently.
    /// </summary>
    Task<JumpListShutdownResult> ShutdownAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the Tasks category for <see cref="JumpListDefinition.AppUserModelId"/> with the given tasks in one
    /// BeginList..CommitList transaction. Requests are executed one at a time, in submission order.
    /// </summary>
    Task<JumpListApplyResult> ApplyAsync(JumpListDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the whole custom Jump List of an AppUserModelID (cleanup/uninstall use only; not for group switching).
    /// </summary>
    Task<JumpListApplyResult> DeleteListAsync(string? appUserModelId, CancellationToken cancellationToken = default);
}
