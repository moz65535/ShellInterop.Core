using System.Runtime.InteropServices;

namespace ShellInterop.Core;

/// <summary>Process-level explicit AppUserModelID. Must be set during startup, before any window or Jump List work.</summary>
public static class ProcessAppIdentity
{
    /// <summary>
    /// Validates <paramref name="appId"/> (and the optional safety filter), then calls
    /// SetCurrentProcessExplicitAppUserModelID. Returns the HRESULT. Throws <see cref="ArgumentException"/> for invalid IDs
    /// so a bad or forbidden ID can never reach the Shell.
    /// </summary>
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
