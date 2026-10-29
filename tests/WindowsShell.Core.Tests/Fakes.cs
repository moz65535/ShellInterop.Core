using WindowsShell.Core.JumpLists;
using WindowsShell.Core.JumpLists.Native;

namespace WindowsShell.Core.Tests;

internal static class Hresults
{
    public const int Fail = unchecked((int)0x80004005);
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int Custom = unchecked((int)0x8007007B);
    public const int Abort = unchecked((int)0x80004004);
}

/// <summary>In-memory stand-in for the Shell. A success here is NEVER evidence of a real Shell success.</summary>
internal sealed class FakeShell : IJumpListNativeFactory
{
    private readonly object _lock = new();
    private int _active;

    public List<string> Calls { get; } = [];

    public List<(string Call, int ThreadId, ApartmentState Apartment)> Threads { get; } = [];

    public int CreateHr;
    public int SetAppIdHr;
    public int BeginHr;
    public int CreateTasksHr;
    public int AddTasksHr;
    public int CommitHr;
    public int AbortHr;
    public int DeleteHr;
    public int FailTaskIndex;

    /// <summary>Name of a call that throws an InvalidOperationException instead of returning.</summary>
    public string? ThrowAt;

    public Action? OnCommit;

    /// <summary>Runs inside DeleteList, so tests can hold a DeleteList transaction open deterministically.</summary>
    public Action? OnDelete;

    /// <summary>The simulated stored Tasks list (replaced on a successful commit, like the Shell's transaction).</summary>
    public List<JumpTaskDefinition> Store { get; } = [];

    public List<List<JumpTaskDefinition>> CommittedSets { get; } = [];

    public string? DeletedAppId;
    public string? LastAppId;
    public int ListsCreated;
    public int ListsDisposed;
    public int HandlesCreated;
    public int HandlesDisposed;
    public int DoubleDisposes;
    public int MaxConcurrentLists;

    public void Seed(params JumpTaskDefinition[] tasks)
    {
        Store.Clear();
        Store.AddRange(tasks);
    }

    public int Create(out IJumpListNative? list)
    {
        Record("Create");
        if (CreateHr < 0)
        {
            list = null;
            return CreateHr;
        }

        lock (_lock)
        {
            ListsCreated++;
            _active++;
            MaxConcurrentLists = Math.Max(MaxConcurrentLists, _active);
        }

        list = new FakeList(this);
        return 0;
    }

    internal void Record(string call)
    {
        lock (_lock)
        {
            Calls.Add(call);
            Threads.Add((call, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        }

        if (ThrowAt == call)
        {
            throw new InvalidOperationException("fake failure at " + call);
        }
    }

    internal void ListDisposed(bool wasAlreadyDisposed)
    {
        lock (_lock)
        {
            if (wasAlreadyDisposed)
            {
                DoubleDisposes++;
                return;
            }

            ListsDisposed++;
            _active--;
            Calls.Add("DisposeList");
        }
    }

    internal void HandleDisposed(bool wasAlreadyDisposed)
    {
        lock (_lock)
        {
            if (wasAlreadyDisposed)
            {
                DoubleDisposes++;
            }
            else
            {
                HandlesDisposed++;
                Calls.Add("DisposeTasks");
            }
        }
    }

    private sealed class FakeHandle(FakeShell owner, List<JumpTaskDefinition> tasks) : ITaskCollectionHandle
    {
        private bool _disposed;

        public List<JumpTaskDefinition> Tasks { get; } = tasks;

        public void Dispose()
        {
            owner.HandleDisposed(_disposed);
            _disposed = true;
        }
    }

    private sealed class FakeList(FakeShell owner) : IJumpListNative
    {
        private List<JumpTaskDefinition>? _pending;
        private bool _disposed;
        private bool _committed;

        public int SetAppId(string appId)
        {
            owner.Record("SetAppId");
            owner.LastAppId = appId;
            return owner.SetAppIdHr;
        }

        public int BeginList()
        {
            owner.Record("BeginList");
            if (owner.BeginHr >= 0)
            {
                _pending = [];
            }

            return owner.BeginHr;
        }

        public int CreateTasks(IReadOnlyList<JumpTaskDefinition> tasks, out ITaskCollectionHandle? collection, out int failedTaskIndex)
        {
            owner.Record("CreateTasks");
            failedTaskIndex = -1;
            if (owner.CreateTasksHr < 0)
            {
                collection = null;
                failedTaskIndex = owner.FailTaskIndex;
                return owner.CreateTasksHr;
            }

            lock (owner._lock)
            {
                owner.HandlesCreated++;
            }

            collection = new FakeHandle(owner, tasks.ToList());
            return 0;
        }

        public int AddUserTasks(ITaskCollectionHandle collection)
        {
            owner.Record("AddUserTasks");
            if (owner.AddTasksHr >= 0)
            {
                _pending = ((FakeHandle)collection).Tasks.ToList();
            }

            return owner.AddTasksHr;
        }

        public int CommitList()
        {
            owner.Record("CommitList");
            owner.OnCommit?.Invoke();
            if (owner.CommitHr >= 0)
            {
                _committed = true;
                owner.Store.Clear();
                owner.Store.AddRange(_pending ?? []);
                owner.CommittedSets.Add(owner.Store.ToList());
            }

            return owner.CommitHr;
        }

        public int AbortList()
        {
            owner.Record("AbortList");
            if (owner.AbortHr >= 0)
            {
                _pending = null; // discards the session; never touches what was committed before
            }

            _ = _committed;
            return owner.AbortHr;
        }

        public int DeleteList(string appId)
        {
            owner.Record("DeleteList");
            owner.DeletedAppId = appId;
            owner.OnDelete?.Invoke();
            if (owner.DeleteHr >= 0)
            {
                owner.Store.Clear();
            }

            return owner.DeleteHr;
        }

        public void Dispose()
        {
            owner.ListDisposed(_disposed);
            _disposed = true;
        }
    }
}

internal sealed class FakeProcessAppId(string? value) : IProcessAppIdProvider
{
    public string? Value { get; set; } = value;

    public int Hr { get; set; }

    public string? Get(out int hr)
    {
        hr = Hr;
        return Value;
    }
}

internal sealed class ListLog : IJumpListLogSink
{
    private readonly object _lock = new();

    public List<JumpListLogEntry> Entries { get; } = [];

    public void Write(JumpListLogEntry entry)
    {
        lock (_lock)
        {
            Entries.Add(entry);
        }
    }
}

internal static class Samples
{
    public const string AppId = "WindowsShell.Core.Poc.TestHost";
    public const string Exe = @"C:\PocTest\host.exe";

    public static JumpTaskDefinition Task(string id) => new()
    {
        Title = "Task " + id,
        Description = "Description " + id,
        ApplicationPath = Exe,
        Arguments = "--poc-task=" + id,
        WorkingDirectory = @"C:\PocTest",
        IconResourcePath = Exe,
        IconResourceIndex = 0,
    };

    public static JumpListDefinition Definition(params string[] ids)
        => new(AppId, ids.Select(Task));

    public static JumpListPublisherOptions Options(IJumpListLogSink? log = null, Predicate<string>? filter = null, bool requireProcessMatch = true) => new()
    {
        RequireExistingFiles = false,
        RequireProcessAppIdMatch = requireProcessMatch,
        AppIdFilter = filter,
        LogSink = log,
        DisposeTimeout = TimeSpan.FromSeconds(10),
    };

    public static JumpListPublisher Publisher(FakeShell shell, JumpListPublisherOptions? options = null, string? processAppId = AppId)
        => new(options ?? Options(), shell, new FakeProcessAppId(processAppId));
}
