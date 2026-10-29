using WindowsShell.Core.Interop;
using WindowsShell.Core.JumpLists;
using WindowsShell.Core.JumpLists.Native;

namespace WindowsShell.Core;

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
        var errors = JumpListValidator.ValidateAppId(appId, new JumpListPublisherOptions { AppIdFilter = allowed });
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors.Select(e => e.Code)), nameof(appId));
        }

        return NativeMethods.SetCurrentProcessExplicitAppUserModelID(appId);
    }

    /// <summary>The explicit ID of this process, or null if none is set.</summary>
    public static string? TryGet() => new Win32ProcessAppIdProvider().Get(out _);
}
