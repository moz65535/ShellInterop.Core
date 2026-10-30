namespace ShellInterop.Core.JumpLists;

public sealed record JumpListLogEntry(
    DateTimeOffset Timestamp,
    int ProcessId,
    int ThreadId,
    ApartmentState Apartment,
    string Event,
    string Message,
    int? HResult = null);

public interface IJumpListLogSink
{
    /// <summary>Must not throw; exceptions are swallowed by the publisher so logging can never break an update.</summary>
    void Write(JumpListLogEntry entry);
}

public sealed class JumpListPublisherOptions
{
    /// <summary>
    /// When true (default) the process-level explicit AppUserModelID must already equal the definition's ID, which makes
    /// "SetCurrentProcessExplicitAppUserModelID before any Jump List work" an enforced precondition.
    /// </summary>
    public bool RequireProcessAppIdMatch { get; init; } = true;

    /// <summary>When true (default) executable/icon/working-directory paths must exist at request time.</summary>
    public bool RequireExistingFiles { get; init; } = true;

    /// <summary>Safety boundary: if set, only IDs for which this returns true may be written or deleted.</summary>
    public Predicate<string>? AppIdFilter { get; init; }

    public IJumpListLogSink? LogSink { get; init; }

    /// <summary>Test seams for the existence checks; default to File.Exists / Directory.Exists.</summary>
    public Func<string, bool>? FileExists { get; init; }

    public Func<string, bool>? DirectoryExists { get; init; }

    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
