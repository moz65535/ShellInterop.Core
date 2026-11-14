using System.Runtime.InteropServices;

namespace ShellInterop.Core;

/// <summary>Process-level explicit AppUserModelID. Must be set during startup, before any window or Jump List work.</summary>
/// <remarks>
/// This type wraps the Windows Shell functions <c>SetCurrentProcessExplicitAppUserModelID</c> and
/// <c>GetCurrentProcessExplicitAppUserModelID</c> (shell32.dll) and is Windows-only.
/// The AppUserModelID is validated before it is passed to the Shell; the rules are described on <see cref="Set(string, Predicate{string}?)"/>.
/// </remarks>
public static class ProcessAppIdentity
{
    /// <summary>
    /// Validates <paramref name="appId"/> (and the optional safety filter), then calls
    /// SetCurrentProcessExplicitAppUserModelID. Returns the HRESULT. Throws <see cref="ArgumentException"/> for invalid IDs
    /// so a bad or forbidden ID can never reach the Shell.
    /// </summary>
    /// <param name="appId">
    /// The explicit AppUserModelID to assign to the current process. It must be at most 128 characters and have the form
    /// <c>Company.Product[.Sub...]</c>: at least two dot-separated, non-empty segments made only of ASCII letters, digits, '-' or '_'.
    /// <see langword="null"/>, an empty string, and IDs containing spaces or other characters are rejected.
    /// </param>
    /// <param name="allowed">
    /// Optional additional filter. When supplied, it is called with <paramref name="appId"/> after the format checks pass,
    /// and the ID is rejected if it returns <see langword="false"/>. When <see langword="null"/>, no extra filtering is applied.
    /// </param>
    /// <returns>
    /// The HRESULT returned by <c>SetCurrentProcessExplicitAppUserModelID</c>; a negative value indicates failure.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="appId"/> is <see langword="null"/>, empty, too long, malformed, or rejected by <paramref name="allowed"/>.
    /// The exception message is a short validation code (<c>AppId.Null</c>, <c>AppId.Empty</c>, <c>AppId.TooLong</c>,
    /// <c>AppId.InvalidFormat</c> or <c>AppId.NotAllowed</c>). The Shell is not called in this case.
    /// </exception>
    /// <remarks>
    /// Call this once during application startup, before any window is created or any Jump List operation is performed.
    /// Calling it affects the whole process.
    /// </remarks>
    public static int Set(string appId, Predicate<string>? allowed = null)
    {
        var error = AppUserModelIdValidation.Validate(appId, allowed);
        if (error is { } failure)
        {
            throw new ArgumentException(failure.Code, nameof(appId));
        }

        return ProcessAppIdNative.SetCurrentProcessExplicitAppUserModelID(appId);
    }

    /// <summary>The explicit ID of this process, or null if none is set.</summary>
    /// <returns>
    /// The explicit AppUserModelID of the current process, or <see langword="null"/> when none has been set
    /// or the Shell call fails. No exception is thrown for Shell failures.
    /// </returns>
    public static string? TryGet() => ProcessAppIdNative.Get(out _);
}

internal static partial class ProcessAppIdNative
{
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LibraryImport("shell32.dll")]
    private static partial int GetCurrentProcessExplicitAppUserModelID(out nint appId);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint pv);

    internal static string? Get(out int hr)
    {
        hr = GetCurrentProcessExplicitAppUserModelID(out nint raw);
        if (hr < 0 || raw == 0) return null;
        try { return Marshal.PtrToStringUni(raw); }
        finally { CoTaskMemFree(raw); }
    }
}
