using System.Runtime.InteropServices;
using System.Text;

namespace WindowsShell.Core.Interop;

// Every GUID and every vtable slot below was checked against the Windows SDK 10.0.26100.0 headers
// (ShObjIdl_core.h, ObjectArray.h, propsys.h, propkey.h). See docs/API-CONTRACT-REVIEW.md.
// Interfaces are classic [ComImport]; all methods are [PreserveSig] so HRESULTs are never turned into exceptions.

internal static class ShellGuids
{
    public static readonly Guid ClsidDestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    public static readonly Guid ClsidEnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    public static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    public static readonly Guid IidCustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    public static readonly Guid IidObjectArray = new("92CA9DCD-5622-4bba-A805-5E9F541BD8C9");
    public static readonly Guid IidObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    public static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    public static readonly Guid IidPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    // PKEY_Title (System.Title): propkey.h DEFINE_PROPERTYKEY(PKEY_Title, F29F85E0-4FF9-1068-AB91-08002B27B3D9, 2)
    public static readonly PropertyKey PkeyTitle = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;
}

/// <summary>Minimal PROPVARIANT (24 bytes on x64, 16 on x86) — only VT_LPWSTR is ever produced.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort VtLpwstr = 31;

    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Data1;
    public nint Data2;

    public static PropVariant FromString(string value) => new()
    {
        Vt = VtLpwstr,
        Data1 = Marshal.StringToCoTaskMemUni(value),
    };

    public readonly string? AsString() => Vt == VtLpwstr && Data1 != 0 ? Marshal.PtrToStringUni(Data1) : null;
}

[ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICustomDestinationList
{
    [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);

    [PreserveSig] int BeginList(out uint pcMinSlots, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);

    [PreserveSig] int AppendKnownCategory(int category);

    [PreserveSig] int AddUserTasks(IObjectArray poa);

    [PreserveSig] int CommitList();

    [PreserveSig] int GetRemovedDestinations(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig] int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);

    [PreserveSig] int AbortList();
}

[ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig] int GetCount(out uint pcObjects);

    [PreserveSig] int GetAt(uint uiIndex, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectCollection
{
    // NOT derived from IObjectArray on the managed side: classic COM interop does not flatten base-interface methods into
    // the derived vtable, so the two inherited IObjectArray slots must be redeclared first (header: IObjectCollection : IObjectArray).
    [PreserveSig] int GetCount(out uint pcObjects);

    [PreserveSig] int GetAt(uint uiIndex, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [PreserveSig] int AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);

    [PreserveSig] int AddFromArray(IObjectArray poaSource);

    [PreserveSig] int RemoveObjectAt(uint uiIndex);

    [PreserveSig] int Clear();
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);

    [PreserveSig] int GetIDList(out nint ppidl);

    [PreserveSig] int SetIDList(nint pidl);

    [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    [PreserveSig] int GetHotkey(out ushort pwHotkey);

    [PreserveSig] int SetHotkey(ushort wHotkey);

    [PreserveSig] int GetShowCmd(out int piShowCmd);

    [PreserveSig] int SetShowCmd(int iShowCmd);

    [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    [PreserveSig] int Resolve(nint hwnd, uint fFlags);

    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint cProps);

    [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);

    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant pv);

    [PreserveSig] int SetValue(in PropertyKey key, in PropVariant propvar);

    [PreserveSig] int Commit();
}
