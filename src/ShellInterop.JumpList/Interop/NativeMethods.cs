using System.Runtime.InteropServices;

namespace ShellInterop.Core.Interop;

internal static partial class NativeMethods
{
    public const uint ClsctxInprocServer = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant pvar);


}

internal static class Hr
{
    public const int Ok = 0;
    public const int Fail = unchecked((int)0x80004005);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int Pointer = unchecked((int)0x80004003);
    public const int InvalidArg = unchecked((int)0x80070057);
    public const int Unexpected = unchecked((int)0x8000FFFF);
    public const int AccessDenied = unchecked((int)0x80070005);

    public static bool Failed(int hr) => hr < 0;

    public static bool Succeeded(int hr) => hr >= 0;

    public static int FromException(Exception ex) => ex switch
    {
        InvalidCastException => NoInterface,
        COMException c => c.HResult,
        _ => Marshal.GetHRForException(ex) is var h and not 0 ? h : Unexpected,
    };
}

/// <summary>CoCreateInstance + RCW creation that surfaces the HRESULT instead of throwing.</summary>
internal static class ComActivator
{
    public static int Create(Guid clsid, Guid iid, out object? instance)
    {
        instance = null;
        int hr = NativeMethods.CoCreateInstance(in clsid, 0, NativeMethods.ClsctxInprocServer, in iid, out nint raw);
        if (Hr.Failed(hr))
        {
            return hr;
        }

        if (raw == 0)
        {
            return Hr.Pointer;
        }

        try
        {
            // GetObjectForIUnknown takes its own reference; drop the one CoCreateInstance gave us.
            instance = Marshal.GetObjectForIUnknown(raw);
        }
        finally
        {
            Marshal.Release(raw);
        }

        return Hr.Ok;
    }
}

internal static class ComRelease
{
    /// <summary>Releases every reference held by the RCW. Only call on RCWs this code created and exclusively owns.</summary>
    public static void Final(object? rcw)
    {
        if (rcw is not null && Marshal.IsComObject(rcw))
        {
            Marshal.FinalReleaseComObject(rcw);
        }
    }
}
