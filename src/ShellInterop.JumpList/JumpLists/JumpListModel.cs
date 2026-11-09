namespace ShellInterop.Core.JumpLists;

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

/// <summary>One ordered Tasks entry: either an executable task or a separator.</summary>
public sealed class JumpListEntry
{
    private JumpListEntry(JumpTaskDefinition? task, bool separator, string? key = null) { Task = task; IsSeparator = separator; Key = key; }
    public JumpTaskDefinition? Task { get; }
    public bool IsSeparator { get; }

    /// <summary>Only meaningful for <see cref="JumpCategoryDefinition.TasksFallback"/>: the destination Key this entry stands for, so an excluded Key is never revived.</summary>
    public string? Key { get; }

    public static JumpListEntry ForTask(JumpTaskDefinition task) => new(task ?? throw new ArgumentNullException(nameof(task)), false);

    /// <summary>A fallback task bound to a destination Key (see <see cref="Key"/>).</summary>
    public static JumpListEntry ForKeyedTask(string key, JumpTaskDefinition task)
        => new(task ?? throw new ArgumentNullException(nameof(task)), false, key ?? throw new ArgumentNullException(nameof(key)));

    public static JumpListEntry Separator() => new(null, true);
    internal static JumpListEntry LegacyTask(JumpTaskDefinition? task) => new(task, false);
}

/// <summary>The complete desired Tasks list for one AppUserModelID. Applying it replaces whatever Tasks existed.</summary>
public sealed class JumpListDefinition
{
    public JumpListDefinition(string? appUserModelId, IEnumerable<JumpTaskDefinition?>? tasks)
    {
        AppUserModelId = appUserModelId;
        Categories = Array.AsReadOnly(Array.Empty<JumpCategoryDefinition?>());
        Tasks = Array.AsReadOnly(tasks is null ? [] : tasks.ToArray());
        Entries = Array.AsReadOnly(Tasks.Select(JumpListEntry.LegacyTask).ToArray());
    }

    private JumpListDefinition(string? appUserModelId, JumpListEntry?[] entries,
        JumpCategoryDefinition?[]? categories = null, JumpRemovedDestinationTracking? tracking = null)
    {
        Categories = Array.AsReadOnly(categories ?? []);
        Category = Categories.Count == 1 ? Categories[0] : null;
        RemovedDestinationTracking = tracking;
        AppUserModelId = appUserModelId;
        Entries = Array.AsReadOnly(entries);
        Tasks = Array.AsReadOnly(entries.Where(e => e is null || !e.IsSeparator).Select(e => e?.Task).ToArray());
    }

    /// <summary>Build from ordered entries. Entries are the authoritative input; Tasks is their task-only view.</summary>
    public static JumpListDefinition FromEntries(string? appUserModelId, IEnumerable<JumpListEntry?> entries)
        => new(appUserModelId, (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray());

    /// <summary>
    /// Build with a custom category and/or removed-destination tracking. A category REQUIRES tracking (validated).
    /// Collections are copied, so later changes by the caller do not affect the definition.
    /// </summary>
    public static JumpListDefinition FromEntries(string? appUserModelId, IEnumerable<JumpListEntry?> entries,
        JumpCategoryDefinition? category, JumpRemovedDestinationTracking? tracking)
        => new(appUserModelId, (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray(),
            category is null ? null : [Snapshot(category)], SnapshotTracking(tracking));

    /// <summary>
    /// Build with several custom categories, appended in the given order (the order is the display priority; nothing is sorted).
    /// Names must be unique (case-insensitive); Keys and Arguments are unique across ALL categories. An empty list means Tasks only.
    /// Tracking is required whenever there is at least one category. Null elements are reported by validation.
    /// </summary>
    public static JumpListDefinition FromEntriesWithCategories(string? appUserModelId, IEnumerable<JumpListEntry?> entries,
        IEnumerable<JumpCategoryDefinition?> categories, JumpRemovedDestinationTracking? tracking)
        => new(appUserModelId, (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray(),
            (categories ?? throw new ArgumentNullException(nameof(categories))).Select(c => c is null ? null : Snapshot(c)).ToArray(),
            SnapshotTracking(tracking));

    private static JumpCategoryDefinition Snapshot(JumpCategoryDefinition category) => category with
    {
        Destinations = category.Destinations?.ToArray()!,
        TasksFallback = category.TasksFallback?.ToArray(),
    };

    private static JumpRemovedDestinationTracking? SnapshotTracking(JumpRemovedDestinationTracking? tracking)
        => tracking is null ? null : tracking with
        {
            MatchOnly = tracking.MatchOnly?.ToArray()!,
            ExcludedDestinationKeys = tracking.ExcludedDestinationKeys?.ToArray()!,
        };

    /// <summary>The custom categories in display order (read-only snapshot). Empty = Tasks only. This is the canonical set.</summary>
    public IReadOnlyList<JumpCategoryDefinition?> Categories { get; }

    /// <summary>
    /// Single-category compatibility view: the category when <see cref="Categories"/> has exactly one element, otherwise null
    /// (no category, or several, in which case no single one represents the list).
    /// </summary>
    public JumpCategoryDefinition? Category { get; }

    /// <summary>
    /// Opt-in removed-destination tracking. When set, BeginList's removed list is read on every Apply (even without a
    /// category) so the caller can persist user removals before CommitList consumes them. Null = the removed list is
    /// neither read nor protected, and CommitList discards it.
    /// </summary>
    public JumpRemovedDestinationTracking? RemovedDestinationTracking { get; }

    /// <summary>Explicit AppUserModelID. Never inferred.</summary>
    public string? AppUserModelId { get; }

    /// <summary>Ordered tasks only, excluding separators; may be empty.</summary>
    public IReadOnlyList<JumpTaskDefinition?> Tasks { get; }

    /// <summary>Authoritative ordered entries, including separators. An empty list clears Tasks.</summary>
    public IReadOnlyList<JumpListEntry?> Entries { get; }
}

/// <summary>How a removed-list link is allowed to be matched to a known destination.</summary>
public enum JumpDestinationPathPolicy
{
    /// <summary>Arguments (Ordinal) AND path (OrdinalIgnoreCase) must both match. Equal Arguments with another path is Ambiguous, never silently accepted.</summary>
    RequirePathMatch,

    /// <summary>The caller asserts that Arguments alone is a stable identifier unique to this application, so a different path (moved/updated exe) still matches with <see cref="MatchConfidence.ArgumentsOnly"/>.</summary>
    AllowArgumentsOnly,
}

/// <summary>A custom-category destination: a caller-owned stable Key plus the link shown in the category.</summary>
public sealed record JumpDestinationDefinition
{
    /// <summary>Stable caller identifier (not a display name, not an index). Non-empty, unique within the definition.</summary>
    public required string Key { get; init; }

    /// <summary>Same requirements as a Tasks entry; Arguments must be unique within the definition (Ordinal).</summary>
    public required JumpTaskDefinition Link { get; init; }

    public JumpDestinationPathPolicy PathPolicy { get; init; } = JumpDestinationPathPolicy.RequirePathMatch;
}

/// <summary>Matching-only identity (no link is created): recognises hidden or already-excluded destinations in the removed list.</summary>
public sealed record JumpDestinationMatch(
    string Key,
    string Arguments,
    string? ApplicationPath = null,
    JumpDestinationPathPolicy PathPolicy = JumpDestinationPathPolicy.RequirePathMatch);

/// <summary>The single custom category appended before Tasks.</summary>
public sealed record JumpCategoryDefinition
{
    /// <summary>Category title, 1..256 characters, no control characters.</summary>
    public required string Name { get; init; }

    /// <summary>Destinations to show. Keys in <see cref="JumpRemovedDestinationTracking.ExcludedDestinationKeys"/> are skipped.</summary>
    public required IReadOnlyList<JumpDestinationDefinition> Destinations { get; init; }

    /// <summary>
    /// Used ONLY when AppendCategory fails with E_ACCESSDENIED (privacy setting/policy). Explicit static commands the caller
    /// chose; destinations are never converted automatically. Null = no fallback (the category is skipped).
    /// Entries made with <see cref="JumpListEntry.ForKeyedTask"/> are dropped while their Key is excluded.
    /// Must not duplicate an ordinary task (same path and arguments). Do not put history-derived items here.
    /// </summary>
    public IReadOnlyList<JumpListEntry?>? TasksFallback { get; init; }
}

/// <summary>Removed-destination tracking contract. Independent of whether a category is used.</summary>
public sealed record JumpRemovedDestinationTracking
{
    /// <summary>Identities to recognise in the removed list although they are not displayed now (hidden group, previously registered items).</summary>
    public IReadOnlyList<JumpDestinationMatch> MatchOnly { get; init; } = [];

    /// <summary>Keys the caller has ALREADY PERSISTED as user-removed. Never pass an unsaved removal: the library commits only when every detected removal is in this set.</summary>
    public IReadOnlyCollection<string> ExcludedDestinationKeys { get; init; } = [];
}

public enum RemovedDestinationKind
{
    /// <summary>Recognised as a known destination.</summary>
    Matched,

    /// <summary>A readable link that matches no known destination. Reported only; does not block Commit.</summary>
    Foreign,

    /// <summary>A Shell item (not a link), e.g. system-tracked recent items. Reported only; does not block Commit.</summary>
    ForeignItem,

    /// <summary>Could not be read completely (COM error, truncation, no path). Blocks Commit.</summary>
    Unreadable,

    /// <summary>Arguments match a known destination but the path differs and the definition does not allow that. Blocks Commit.</summary>
    Ambiguous,
}

public enum MatchConfidence { None, Exact, ArgumentsOnly }

/// <summary>One entry of the Shell's removed-destinations list as classified by the library.</summary>
public sealed record RemovedDestination(
    string? Key,
    RemovedDestinationKind Kind,
    MatchConfidence Confidence,
    bool AlreadyExcluded,
    string? ApplicationPath,
    string? Arguments);

/// <summary>What the COM calls did. This is not visual confirmation.</summary>
public enum JumpListDisplayMode
{
    /// <summary>Not applied (the call did not succeed) or not applicable.</summary>
    None,
    /// <summary>No category requested.</summary>
    TasksOnly,
    /// <summary>AppendCategory, AddUserTasks and CommitList all reported success.</summary>
    CategoryAndTasks,
    /// <summary>A category was requested but every destination was excluded, so none was appended.</summary>
    CategoryEmpty,
    /// <summary>AppendCategory returned E_ACCESSDENIED; the caller's TasksFallback was added to Tasks.</summary>
    TasksFallback,
    /// <summary>AppendCategory returned E_ACCESSDENIED and no fallback was specified; the category was omitted.</summary>
    CategorySkipped,
}

public enum JumpListFallbackReason { None, PrivacyAccessDenied }

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

    /// <summary>
    /// NOT a failure and NOT committed: new user removals were detected and AbortList was called. The caller must persist
    /// the returned Matched keys, add them to ExcludedDestinationKeys and Apply again.
    /// </summary>
    RemovedDestinationsPending,

    /// <summary>The removed list could not be identified safely (unreadable or ambiguous item, or it could not be read). Nothing was committed.</summary>
    RemovedDestinationUnidentifiable,

    /// <summary>AppendCategory failed with an HRESULT other than E_ACCESSDENIED; AbortList was called and nothing was committed.</summary>
    CategoryAppendFailed,
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
    ReadRemoved,
    AppendCategory,
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

    /// <summary>Set only when Status is Success: which COM path was taken. Not visual confirmation.</summary>
    public JumpListDisplayMode EffectiveDisplayMode { get; init; }

    /// <summary>Why the category was not appended as requested (E_ACCESSDENIED); None otherwise.</summary>
    public JumpListFallbackReason FallbackReason { get; init; }

    /// <summary>Classified removed list (only when tracking was enabled and the list was read). For Pending, persist the Matched entries that are not AlreadyExcluded.</summary>
    public IReadOnlyList<RemovedDestination> RemovedDestinations { get; init; } = [];

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
