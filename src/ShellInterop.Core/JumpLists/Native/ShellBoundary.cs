using ShellInterop.Core.Interop;

namespace ShellInterop.Core.JumpLists.Native;

/// <summary>Handle for the shell-link collection built for AddUserTasks. Dispose releases every COM reference exactly once.</summary>
internal interface ITaskCollectionHandle : IDisposable
{
}

/// <summary>
/// Thin seam over ICustomDestinationList. All members return raw HRESULTs and must only be called on the STA worker.
/// The real implementation is <see cref="ShellJumpListNative"/>; tests substitute a fake. A fake success is never a Shell success.
/// </summary>
internal interface IJumpListNative : IDisposable
{
    int SetAppId(string appId);

    int BeginList();

    int CreateTasks(IReadOnlyList<JumpTaskDefinition> tasks, out ITaskCollectionHandle? collection, out int failedTaskIndex);

    int AddUserTasks(ITaskCollectionHandle collection);

    int CommitList();

    int AbortList();

    int DeleteList(string appId);
}

internal interface IJumpListNativeFactory
{
    /// <summary>Creates the native list object. Called on the STA worker, once per request.</summary>
    int Create(out IJumpListNative? list);
}

/// <summary>Reads the process-level explicit AppUserModelID.</summary>
internal interface IProcessAppIdProvider
{
    /// <summary>Returns the explicit ID, or null when none is set / it cannot be read (HRESULT in <paramref name="hr"/>).</summary>
    string? Get(out int hr);
}

internal sealed class Win32ProcessAppIdProvider : IProcessAppIdProvider
{
    public string? Get(out int hr)
    {
        hr = NativeMethods.GetCurrentProcessExplicitAppUserModelID(out nint raw);
        if (Hr.Failed(hr) || raw == 0)
        {
            return null;
        }

        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUni(raw);
        }
        finally
        {
            NativeMethods.CoTaskMemFree(raw);
        }
    }
}

internal sealed class ShellJumpListNativeFactory : IJumpListNativeFactory
{
    public int Create(out IJumpListNative? list)
    {
        list = null;
        int hr = ComActivator.Create(ShellGuids.ClsidDestinationList, ShellGuids.IidCustomDestinationList, out object? obj);
        if (Hr.Failed(hr))
        {
            return hr;
        }

        list = new ShellJumpListNative((ICustomDestinationList)obj!);
        return Hr.Ok;
    }
}
