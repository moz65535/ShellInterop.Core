using System.Runtime.InteropServices;
using System.Text;

namespace ShellInterop.Notifications;

internal sealed record ShortcutMatch(string Path, string? TargetPath);

internal sealed record ScanResult(IReadOnlyList<ShortcutMatch> Matches, IReadOnlyList<string> Warnings);

internal interface IShortcutScanner
{
    ScanResult Scan(string aumid);
}

/// <summary>
/// At most one scan is in flight per gate. The slot is held until the worker thread actually finishes, so a scan abandoned
/// after a timeout keeps the slot and later requests are refused instead of spawning more STA threads.
/// </summary>
internal sealed class ScanGate
{
    private int _busy;
    internal static ScanGate Shared { get; } = new(); // process-wide: shared by every NotificationPublisher / scanner instance
    internal bool TryEnter() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    internal void Exit() => Volatile.Write(ref _busy, 0);
}

/// <summary>Read-only: enumerates *.lnk files and reads their System.AppUserModel.ID. Never writes, resolves or deletes.</summary>
internal sealed class StartMenuShortcutScanner : IShortcutScanner
{
    private static readonly TimeSpan DefaultScanTimeout = TimeSpan.FromSeconds(60);
    private readonly string[] _directories;
    private readonly Action<string, List<ShortcutMatch>, List<string>> _scanCore;
    private readonly TimeSpan _timeout;
    private readonly ScanGate _gate;

    public StartMenuShortcutScanner()
        : this([Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)])
    {
    }

    internal StartMenuShortcutScanner(string[] directories)
    {
        _directories = directories;
        _scanCore = ScanCore;
        _timeout = DefaultScanTimeout;
        _gate = ScanGate.Shared;
    }

    // Test seam: replaces the Shell COM work, the timeout and the gate. Production code never uses this.
    internal StartMenuShortcutScanner(Action<string, List<ShortcutMatch>, List<string>> scanCore, TimeSpan timeout, ScanGate gate)
    {
        _directories = [];
        _scanCore = scanCore;
        _timeout = timeout;
        _gate = gate;
    }

    public ScanResult Scan(string aumid)
    {
        // A previous scan (possibly abandoned after a timeout) is still running: do not start another STA thread.
        if (!_gate.TryEnter())
        {
            return new ScanResult([], ["scan skipped: a previous registration scan is still running (busy)"]);
        }

        // Lists are private to this call; a worker that finishes after the timeout can only touch its own, discarded lists.
        var matches = new List<ShortcutMatch>();
        var warnings = new List<string>();
        var done = new ManualResetEventSlim();
        // Own background thread (not NotificationPublisher.RunOnSta) so a hung Shell call can be abandoned after the timeout
        // without blocking process exit. It is never aborted: the COM call is not assumed to be cancellable.
        var thread = new Thread(() =>
        {
            try { _scanCore(aumid, matches, warnings); }
            catch (Exception e) { warnings.Add($"scan failed: {e.GetType().Name} 0x{e.HResult:X8}"); }
            finally
            {
                _gate.Exit(); // released only when the work really ends (normal, failed, or late after a timeout)
                done.Set();
            }
        }) { IsBackground = true, Name = "ShellInterop.Notifications.STA" };
        thread.SetApartmentState(ApartmentState.STA);
        try { thread.Start(); }
        catch (Exception)
        {
            _gate.Exit(); // the worker never started, so nothing else will release the slot
            throw;
        }
        if (!done.Wait(_timeout))
        {
            return new ScanResult([], [$"scan timed out after {_timeout.TotalSeconds:0.###}s; the worker is abandoned and further scans are refused until it ends"]);
        }
        return new ScanResult(matches, warnings);
    }

    private void ScanCore(string aumid, List<ShortcutMatch> matches, List<string> warnings)
    {
        foreach (string dir in _directories.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue; // a missing Start Menu folder contains no shortcut
            List<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch (Exception e) { warnings.Add($"enumerate {dir}: {e.GetType().Name}: {e.Message}"); continue; }
            foreach (string file in files)
            {
                try
                {
                    var (target, id) = ShellLinkReader.Read(file);
                    if (string.Equals(id, aumid, StringComparison.Ordinal)) matches.Add(new ShortcutMatch(file, target));
                }
                catch (Exception e) { warnings.Add($"read {file}: {e.GetType().Name} 0x{e.HResult:X8}"); }
            }
        }
    }
}

// Interfaces per shobjidl_core.h / propsys.h / objidl.h (vtable order follows the headers). Same shapes as the SHELL-NOTIF-02 PoC.
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFileNative
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort vt, r1, r2, r3;
    public IntPtr p;
    public IntPtr p2;
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PropertyKey pkey);
    void GetValue(ref PropertyKey key, out PropVariant pv);
    void SetValue(ref PropertyKey key, ref PropVariant pv);
    void Commit();
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass { }

internal static partial class ShellLinkReader
{
    // System.AppUserModel.ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, pid 5.
    private static readonly PropertyKey PkeyAumid = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant pvar);

    /// <summary>Must run on an STA thread. Read-only (STGM_READ); the link is not resolved.</summary>
    internal static (string? Target, string? Aumid) Read(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFileNative)link).Load(lnkPath, 0 /* STGM_READ */);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();
            string? aumid = null;
            var key = PkeyAumid;
            ((IPropertyStore)link).GetValue(ref key, out PropVariant pv);
            try { if (pv.vt == 31 /* VT_LPWSTR */ && pv.p != IntPtr.Zero) aumid = Marshal.PtrToStringUni(pv.p); }
            finally { PropVariantClear(ref pv); }
            return (target, aumid);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
}
