using System.Collections.Concurrent;
using System.Diagnostics;
using ShellInterop.Core.Interop;
using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.JumpLists;

/// <summary>
/// Publishes the Jump List Tasks category through ICustomDestinationList.
/// All COM work happens on one dedicated STA thread owned by this instance; requests are queued and executed strictly
/// one at a time in submission order. No COM object outlives the request that created it. The class does not
/// depend on any UI framework or dispatcher: callers simply await the returned Task from whatever thread they like.
/// </summary>
public sealed class JumpListPublisher : IJumpListPublisher
{
    private readonly JumpListPublisherOptions _options;
    private readonly IJumpListNativeFactory _factory;
    private readonly IProcessAppIdProvider _processAppId;
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _workerExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposeRequested;
    private volatile bool _inFlight;
    private volatile bool _stopped;
    private bool _disposed;
    private bool _inFlightAtShutdown;
    private int _discarded;

    public JumpListPublisher(JumpListPublisherOptions? options = null)
        : this(options, new ShellJumpListNativeFactory(), new Win32ProcessAppIdProvider())
    {
    }

    internal JumpListPublisher(JumpListPublisherOptions? options, IJumpListNativeFactory factory, IProcessAppIdProvider processAppId)
    {
        _options = options ?? new JumpListPublisherOptions();
        _factory = factory;
        _processAppId = processAppId;
        _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "ShellInterop.Core.JumpList.STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Managed id of the STA worker (diagnostics/tests).</summary>
    internal int WorkerThreadId => _thread.ManagedThreadId;

    /// <summary>True while the worker thread has not yet exited (tests).</summary>
    internal bool WorkerIsAlive => _thread.IsAlive;

    public Task<JumpListApplyResult> ApplyAsync(JumpListDefinition definition, CancellationToken cancellationToken = default)
    {
        int count = definition?.Tasks.Count ?? 0;
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Simple(JumpListApplyStatus.Cancelled, count, JumpListShellState.Unchanged));
        }

        var errors = JumpListValidator.Validate(definition, _options);
        if (errors.Count > 0)
        {
            Log("ValidationFailed", $"{errors.Count} error(s): {string.Join("; ", errors.Select(e => e.Code))}");
            return Task.FromResult(Simple(JumpListApplyStatus.ValidationFailed, count, JumpListShellState.Unchanged) with
            {
                Error = new JumpListError(JumpListFailurePhase.Validation, null, $"{errors.Count} validation error(s)."),
                ValidationErrors = errors,
            });
        }

        return Enqueue(count, cancellationToken, queueWait => ExecuteApply(definition!, queueWait));
    }

    public Task<JumpListApplyResult> DeleteListAsync(string? appUserModelId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Simple(JumpListApplyStatus.Cancelled, 0, JumpListShellState.Unchanged));
        }

        var errors = JumpListValidator.ValidateAppId(appUserModelId, _options);
        if (errors.Count > 0)
        {
            Log("ValidationFailed", $"DeleteList rejected: {string.Join("; ", errors.Select(e => e.Code))}");
            return Task.FromResult(Simple(JumpListApplyStatus.ValidationFailed, 0, JumpListShellState.Unchanged) with
            {
                Error = new JumpListError(JumpListFailurePhase.Validation, null, $"{errors.Count} validation error(s)."),
                ValidationErrors = errors,
            });
        }

        return Enqueue(0, cancellationToken, queueWait => ExecuteDelete(appUserModelId!, queueWait));
    }

    public JumpListPublisherState State
        => _stopped ? JumpListPublisherState.Stopped : _disposeRequested ? JumpListPublisherState.ShuttingDown : JumpListPublisherState.Running;

    /// <summary>
    /// Synchronous, best-effort stop (kept for IDisposable callers). It never aborts the worker: if the in-flight COM
    /// transaction outlives <see cref="JumpListPublisherOptions.DisposeTimeout"/> it returns while the state is still
    /// ShuttingDown and logs DisposeTimedOut. Hosts that must not close during a transaction use <see cref="ShutdownAsync"/>.
    /// Calling it from the worker thread (e.g. inside a log sink) does not join itself.
    /// </summary>
    public void Dispose()
    {
        BeginShutdown();
        if (Thread.CurrentThread == _thread)
        {
            return;
        }

        if (!_thread.Join(_options.DisposeTimeout))
        {
            Log("DisposeTimedOut", $"worker still running after {_options.DisposeTimeout.TotalMilliseconds:F0} ms; state={State}");
        }
    }

    public async Task<JumpListShutdownResult> ShutdownAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        BeginShutdown();
        bool fromWorker = Thread.CurrentThread == _thread;   // cannot wait for our own exit
        if (!fromWorker)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task delay = Task.Delay(timeout ?? Timeout.InfiniteTimeSpan, delayCts.Token);
            await Task.WhenAny(_workerExited.Task, delay).ConfigureAwait(false);
            delayCts.Cancel();
        }

        bool completed = _workerExited.Task.IsCompleted;
        var result = new JumpListShutdownResult(
            completed,
            !completed && !fromWorker && !cancellationToken.IsCancellationRequested,
            _inFlightAtShutdown,
            Volatile.Read(ref _discarded),
            sw.Elapsed);
        Log(completed ? "ShutdownCompleted" : "ShutdownNotCompleted",
            $"timedOut={result.TimedOut} inFlightAtRequest={result.OperationWasInFlight} discarded={result.DiscardedPendingRequests} elapsedMs={sw.Elapsed.TotalMilliseconds:F0}");
        return result;
    }

    private void BeginShutdown()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;          // from here on Enqueue refuses; checked under the same lock as _queue.Add
            _disposeRequested = true;
            _inFlightAtShutdown = _inFlight;
            _queue.CompleteAdding();
        }

        Log("ShutdownRequested", $"inFlight={_inFlightAtShutdown}");
    }

    // ---------------------------------------------------------------- queue / worker

    private sealed record WorkItem(
        Func<TimeSpan, JumpListApplyResult> Run,
        int RequestedTaskCount,
        TaskCompletionSource<JumpListApplyResult> Completion,
        CancellationToken Token,
        Stopwatch QueueTimer);

    private Task<JumpListApplyResult> Enqueue(int taskCount, CancellationToken token, Func<TimeSpan, JumpListApplyResult> run)
    {
        var item = new WorkItem(run, taskCount, new(TaskCreationOptions.RunContinuationsAsynchronously), token, Stopwatch.StartNew());
        lock (_gate)
        {
            if (!_disposed)
            {
                try
                {
                    _queue.Add(item);
                    return item.Completion.Task;
                }
                catch (InvalidOperationException)
                {
                    // CompleteAdding raced us; fall through to the disposed result.
                }
            }
        }

        return Task.FromResult(Simple(JumpListApplyStatus.PublisherDisposed, taskCount, JumpListShellState.Unchanged));
    }

    private void WorkerLoop()
    {
        Log("WorkerStart", "STA worker started");
        try
        {
            foreach (WorkItem item in _queue.GetConsumingEnumerable())
            {
                TimeSpan wait = item.QueueTimer.Elapsed;
                JumpListApplyResult result;
                try
                {
                    if (_disposeRequested)
                    {
                        // Not started before shutdown: never reaches the Shell.
                        Interlocked.Increment(ref _discarded);
                        result = Simple(JumpListApplyStatus.PublisherDisposed, item.RequestedTaskCount, JumpListShellState.Unchanged);
                    }
                    else if (item.Token.IsCancellationRequested)
                    {
                        result = Simple(JumpListApplyStatus.Cancelled, item.RequestedTaskCount, JumpListShellState.Unchanged);
                    }
                    else
                    {
                        _inFlight = true;
                        result = item.Run(wait);
                    }
                }
                catch (Exception ex)
                {
                    // Last-resort guard so a bug can never kill the worker or leave a caller hanging.
                    Log("WorkerError", ex.GetType().Name + ": " + ex.Message);
                    result = Simple(JumpListApplyStatus.UnknownState, item.RequestedTaskCount, JumpListShellState.Unknown) with
                    {
                        Error = new JumpListError(JumpListFailurePhase.Unexpected, Hr.FromException(ex), ex.Message),
                    };
                }
                finally
                {
                    _inFlight = false;
                }

                item.Completion.TrySetResult(result with { QueueWait = wait });
            }
        }
        finally
        {
            // Nothing can still be waiting: complete any leftovers, then release the queue and publish "worker exited" exactly once.
            while (_queue.TryTake(out WorkItem? left))
            {
                left.Completion.TrySetResult(Simple(JumpListApplyStatus.PublisherDisposed, left.RequestedTaskCount, JumpListShellState.Unchanged));
            }

            _queue.Dispose();
            _stopped = true;
            Log("WorkerStop", "STA worker stopped");
            _workerExited.TrySetResult();
        }
    }

    // ---------------------------------------------------------------- transactions (run on the STA worker)

    private enum Stage { None, Created, SessionOpen, Committing }

    private JumpListApplyResult ExecuteApply(JumpListDefinition definition, TimeSpan queueWait)
    {
        var sw = Stopwatch.StartNew();
        string appId = definition.AppUserModelId!;
        JumpListEntry[] entries = definition.Entries.Select(e => e!).ToArray();
        int taskCount = definition.Tasks.Count;
        var diagnostics = new List<string>();
        var categories = definition.Categories.Select(c => c!).ToArray();
        var tracking = definition.RemovedDestinationTracking;
        IReadOnlyList<RemovedDestination> removed = [];
        var displayMode = JumpListDisplayMode.TasksOnly;
        var fallbackReason = JumpListFallbackReason.None;
        Log("ApplyStart", $"aumid={appId} tasks={taskCount} categories={categories.Length} tracking={tracking is not null}");

        JumpListApplyResult Done(JumpListApplyStatus status, JumpListShellState state, JumpListError? error = null, JumpListError? preceding = null)
            => new()
            {
                Status = status,
                RequestedTaskCount = taskCount,
                ShellState = state,
                Error = error,
                PrecedingError = preceding,
                Diagnostics = diagnostics.ToArray(),
                QueueWait = queueWait,
                Elapsed = sw.Elapsed,
                EffectiveDisplayMode = status == JumpListApplyStatus.Success ? displayMode : JumpListDisplayMode.None,
                FallbackReason = fallbackReason,
                RemovedDestinations = removed,
            };

        if (CheckProcessAppId(appId) is { } mismatch)
        {
            return Done(JumpListApplyStatus.ValidationFailed, JumpListShellState.Unchanged, mismatch);
        }

        IJumpListNative? list = null;
        ITaskCollectionHandle? collection = null;
        var categoryCollections = new List<ITaskCollectionHandle>();
        var stage = Stage.None;
        try
        {
            int hr = _factory.Create(out list);
            Log("CreateList", "ICustomDestinationList creation", hr);
            if (Hr.Failed(hr))
            {
                return Done(JumpListApplyStatus.BeginFailed, JumpListShellState.Unchanged, new(JumpListFailurePhase.CreateList, hr, "Could not create the destination list object."));
            }

            stage = Stage.Created;

            hr = list!.SetAppId(appId);
            Log("SetAppID", appId, hr);
            if (Hr.Failed(hr))
            {
                return Done(JumpListApplyStatus.BeginFailed, JumpListShellState.Unchanged, new(JumpListFailurePhase.SetAppId, hr, "SetAppID failed."));
            }

            hr = list.BeginList();
            Log("BeginList", "begin transaction", hr);
            if (Hr.Failed(hr))
            {
                // No session was opened, so there is nothing to abort.
                return Done(JumpListApplyStatus.BeginFailed, JumpListShellState.Unchanged, new(JumpListFailurePhase.BeginList, hr, "BeginList failed."));
            }

            stage = Stage.SessionOpen;

            var excluded = new HashSet<string>(tracking?.ExcludedDestinationKeys ?? [], StringComparer.Ordinal);

            // Reads and classifies the removed list of the CURRENT session (one list for all categories). Returns the
            // aborted result when the session must stop, or null when it is safe to continue.
            JumpListApplyResult? CheckRemoved()
            {
                if (tracking is null)
                {
                    return null;
                }

                // Read BEFORE anything else: CommitList would otherwise discard the removed list without the caller knowing.
                int rhr = list!.ReadRemovedDestinations(out var raw);
                Log("ReadRemoved", $"count={raw.Count}", rhr);
                if (Hr.Failed(rhr))
                {
                    return AbortAfterFailure(list!, JumpListApplyStatus.RemovedDestinationUnidentifiable,
                        new(JumpListFailurePhase.ReadRemoved, rhr, "The removed-destinations list could not be read."), Done);
                }

                removed = RemovedDestinationClassifier.Classify(raw, RemovedDestinationClassifier.BuildMatchers(definition), excluded);
                if (removed.Any(r => r.Kind is RemovedDestinationKind.Unreadable or RemovedDestinationKind.Ambiguous))
                {
                    return AbortAfterFailure(list!, JumpListApplyStatus.RemovedDestinationUnidentifiable,
                        new(JumpListFailurePhase.ReadRemoved, null, "A removed destination could not be identified safely (unreadable or ambiguous); nothing was committed."), Done);
                }

                if (removed.Any(r => r.Kind == RemovedDestinationKind.Matched && !r.AlreadyExcluded))
                {
                    // Not an error: the caller must persist the keys first, then Apply again. Nothing is committed.
                    return AbortAfterFailure(list!, JumpListApplyStatus.RemovedDestinationsPending,
                        new(JumpListFailurePhase.ReadRemoved, null, "New removed destinations detected; persist them and apply again."), Done, statusOnly: true);
                }

                return null;
            }

            if (CheckRemoved() is { } removedStop)
            {
                return removedStop;
            }

            var finalEntries = entries.ToList();
            // Categories keep the caller's order; a category whose destinations are all excluded is never appended.
            var plans = categories
                .Select(c => (Category: c, Shown: c.Destinations.Where(d => !excluded.Contains(d.Key)).ToArray()))
                .Where(p => p.Shown.Length > 0)
                .ToList();
            if (categories.Length > 0 && plans.Count == 0)
            {
                displayMode = JumpListDisplayMode.CategoryEmpty;
            }

            bool accessDenied = false;
            int appended = 0;
            foreach (var (category, shown) in plans)
            {
                hr = list.CreateDestinations(shown.Select(d => d.Link).ToArray(), out var handle, out int failedDestination);
                Log("CreateDestinations", $"category={category.Name} count={shown.Length} failedIndex={failedDestination}", hr);
                if (Hr.Failed(hr))
                {
                    return AbortAfterFailure(list, JumpListApplyStatus.TaskCreationFailed,
                        new(JumpListFailurePhase.CreateTask, hr, "Shell link creation failed for a category destination.", failedDestination >= 0 ? failedDestination : null), Done);
                }

                categoryCollections.Add(handle!);
                hr = list.AppendCategory(category.Name, handle!);
                Log("AppendCategory", $"name={category.Name} count={shown.Length}", hr);
                if (Hr.Succeeded(hr))
                {
                    appended++;
                    displayMode = JumpListDisplayMode.CategoryAndTasks;
                }
                else if (hr == Hr.AccessDenied)
                {
                    // Privacy setting/policy applies to the whole list: stop appending, never commit a partial category set.
                    accessDenied = true;
                    break;
                }
                else
                {
                    return AbortAfterFailure(list, JumpListApplyStatus.CategoryAppendFailed,
                        new(JumpListFailurePhase.AppendCategory, hr, "AppendCategory failed."), Done);
                }
            }

            if (accessDenied)
            {
                fallbackReason = JumpListFallbackReason.PrivacyAccessDenied;
                displayMode = JumpListDisplayMode.CategorySkipped;
                if (appended > 0)
                {
                    // Earlier categories were already appended in this session: discard it and retry ONCE with Tasks only,
                    // so categories and fallback tasks never mix. The retry is not itself retried.
                    int ahr = SafeAbort(list);
                    Log("AbortList", "abort before privacy fallback retry", ahr);
                    if (Hr.Failed(ahr))
                    {
                        return Done(JumpListApplyStatus.AbortFailed, JumpListShellState.Unknown, new(JumpListFailurePhase.AbortList, ahr, "AbortList failed."),
                            new(JumpListFailurePhase.AppendCategory, Hr.AccessDenied, "AppendCategory failed with E_ACCESSDENIED after earlier categories succeeded."));
                    }

                    stage = Stage.Created;
                    hr = list.BeginList();
                    Log("BeginList", "begin transaction (privacy fallback retry)", hr);
                    if (Hr.Failed(hr))
                    {
                        return Done(JumpListApplyStatus.BeginFailed, JumpListShellState.Unchanged, new(JumpListFailurePhase.BeginList, hr, "BeginList failed on the privacy fallback retry."));
                    }

                    stage = Stage.SessionOpen;
                    if (CheckRemoved() is { } retryStop)
                    {
                        return retryStop;
                    }
                }

                foreach (var category in plans.Select(p => p.Category))
                {
                    if (category.TasksFallback is not { } fallback)
                    {
                        continue;
                    }

                    var block = BuildFallbackBlock(fallback, excluded);
                    if (block.Count > 0)
                    {
                        if (finalEntries.Count > 0 && !finalEntries[^1].IsSeparator)
                        {
                            finalEntries.Add(JumpListEntry.Separator());
                        }

                        finalEntries.AddRange(block);
                        displayMode = JumpListDisplayMode.TasksFallback;
                    }
                }
            }

            if (finalEntries.Count > 0)
            {
                hr = list.CreateTasks(finalEntries, out collection, out int failedIndex);
                Log("CreateTasks", $"count={finalEntries.Count} failedIndex={failedIndex}", hr);
                if (Hr.Failed(hr))
                {
                    return AbortAfterFailure(list, JumpListApplyStatus.TaskCreationFailed,
                        new(JumpListFailurePhase.CreateTask, hr, "Shell link creation failed.", failedIndex >= 0 ? failedIndex : null), Done);
                }

                hr = list.AddUserTasks(collection!);
                Log("AddUserTasks", $"count={finalEntries.Count}", hr);
                if (Hr.Failed(hr))
                {
                    return AbortAfterFailure(list, JumpListApplyStatus.AddTasksFailed,
                        new(JumpListFailurePhase.AddUserTasks, hr, "AddUserTasks failed."), Done);
                }
            }

            stage = Stage.Committing;
            hr = list.CommitList();
            Log("CommitList", "commit transaction", hr);
            if (Hr.Failed(hr))
            {
                // AbortList is NOT a rollback and the session outcome is ambiguous after a failed commit, so it is not called.
                return Done(JumpListApplyStatus.CommitFailed, JumpListShellState.Unknown, new(JumpListFailurePhase.CommitList, hr, "CommitList failed; the stored list may or may not have changed."));
            }

            return Done(JumpListApplyStatus.Success, JumpListShellState.Applied);
        }
        catch (Exception ex)
        {
            Log("UnexpectedException", ex.GetType().Name + ": " + ex.Message);
            var error = new JumpListError(JumpListFailurePhase.Unexpected, Hr.FromException(ex), ex.GetType().Name + ": " + ex.Message);
            if (stage == Stage.SessionOpen && list is not null)
            {
                int ahr = SafeAbort(list);
                Log("AbortList", "abort after unexpected exception", ahr);
                return Hr.Failed(ahr)
                    ? Done(JumpListApplyStatus.AbortFailed, JumpListShellState.Unknown, new(JumpListFailurePhase.AbortList, ahr, "AbortList failed."), error)
                    : Done(JumpListApplyStatus.UnknownState, JumpListShellState.Unchanged, error);
            }

            return Done(JumpListApplyStatus.UnknownState, JumpListShellState.Unknown, error);
        }
        finally
        {
            Release(collection, "task collection", diagnostics);
            foreach (var handle in categoryCollections)
            {
                Release(handle, "category collection", diagnostics);
            }

            Release(list, "destination list", diagnostics);
            Log("ApplyEnd", $"elapsedMs={sw.Elapsed.TotalMilliseconds:F1}");
        }
    }

    private JumpListApplyResult AbortAfterFailure(
        IJumpListNative list,
        JumpListApplyStatus failureStatus,
        JumpListError failure,
        Func<JumpListApplyStatus, JumpListShellState, JumpListError?, JumpListError?, JumpListApplyResult> done,
        bool statusOnly = false)
    {
        int ahr = SafeAbort(list);
        Log("AbortList", "abort after pre-commit failure", ahr);
        return Hr.Failed(ahr)
            ? done(JumpListApplyStatus.AbortFailed, JumpListShellState.Unknown, new(JumpListFailurePhase.AbortList, ahr, "AbortList failed."), failure)
            : done(failureStatus, JumpListShellState.Unchanged, statusOnly ? null : failure, null);
    }

    /// <summary>Drops excluded keyed entries, then removes leading, trailing and consecutive separators.</summary>
    private static List<JumpListEntry> BuildFallbackBlock(IReadOnlyList<JumpListEntry?> fallback, IReadOnlySet<string> excluded)
    {
        var block = new List<JumpListEntry>();
        foreach (var e in fallback.Select(e => e!).Where(e => e.Key is null || !excluded.Contains(e.Key)))
        {
            if (e.IsSeparator && (block.Count == 0 || block[^1].IsSeparator))
            {
                continue;
            }

            block.Add(e);
        }

        while (block.Count > 0 && block[^1].IsSeparator)
        {
            block.RemoveAt(block.Count - 1);
        }

        return block;
    }

    private JumpListApplyResult ExecuteDelete(string appId, TimeSpan queueWait)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<string>();
        Log("DeleteStart", $"aumid={appId}");

        JumpListApplyResult Done(JumpListApplyStatus status, JumpListShellState state, JumpListError? error = null)
            => new()
            {
                Status = status,
                ShellState = state,
                Error = error,
                Diagnostics = diagnostics.ToArray(),
                QueueWait = queueWait,
                Elapsed = sw.Elapsed,
            };

        if (CheckProcessAppId(appId) is { } mismatch)
        {
            return Done(JumpListApplyStatus.ValidationFailed, JumpListShellState.Unchanged, mismatch);
        }

        IJumpListNative? list = null;
        try
        {
            int hr = _factory.Create(out list);
            Log("CreateList", "ICustomDestinationList creation", hr);
            if (Hr.Failed(hr))
            {
                return Done(JumpListApplyStatus.DeleteFailed, JumpListShellState.Unchanged, new(JumpListFailurePhase.CreateList, hr, "Could not create the destination list object."));
            }

            hr = list!.DeleteList(appId);
            Log("DeleteList", appId, hr);
            return Hr.Failed(hr)
                ? Done(JumpListApplyStatus.DeleteFailed, JumpListShellState.Unknown, new(JumpListFailurePhase.DeleteList, hr, "DeleteList failed."))
                : Done(JumpListApplyStatus.Success, JumpListShellState.Applied);
        }
        catch (Exception ex)
        {
            Log("UnexpectedException", ex.GetType().Name + ": " + ex.Message);
            return Done(JumpListApplyStatus.UnknownState, JumpListShellState.Unknown,
                new(JumpListFailurePhase.Unexpected, Hr.FromException(ex), ex.GetType().Name + ": " + ex.Message));
        }
        finally
        {
            Release(list, "destination list", diagnostics);
            Log("DeleteEnd", $"elapsedMs={sw.Elapsed.TotalMilliseconds:F1}");
        }
    }

    private JumpListError? CheckProcessAppId(string appId)
    {
        if (!_options.RequireProcessAppIdMatch)
        {
            return null;
        }

        string? processId = _processAppId.Get(out int hr);
        Log("ProcessAppIdCheck", $"process={processId ?? "(none)"} requested={appId}", hr);
        return string.Equals(processId, appId, StringComparison.Ordinal)
            ? null
            : new JumpListError(JumpListFailurePhase.ProcessAppIdCheck, Hr.Failed(hr) ? hr : null,
                "The process-level explicit AppUserModelID does not match the requested one (set it before any Jump List work).");
    }

    private int SafeAbort(IJumpListNative list)
    {
        try
        {
            return list.AbortList();
        }
        catch (Exception ex)
        {
            return Hr.FromException(ex);
        }
    }

    private void Release(IDisposable? resource, string what, List<string> diagnostics)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
            Log("Release", what);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Releasing {what} failed: {ex.GetType().Name}: {ex.Message}");
            Log("ReleaseFailed", $"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static JumpListApplyResult Simple(JumpListApplyStatus status, int taskCount, JumpListShellState state)
        => new() { Status = status, RequestedTaskCount = taskCount, ShellState = state };

    private void Log(string eventName, string message, int? hr = null)
    {
        if (_options.LogSink is not { } sink)
        {
            return;
        }

        try
        {
            sink.Write(new JumpListLogEntry(
                DateTimeOffset.Now,
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.GetApartmentState(),
                eventName,
                message,
                hr));
        }
        catch
        {
            // Logging must never affect the update.
        }
    }
}
