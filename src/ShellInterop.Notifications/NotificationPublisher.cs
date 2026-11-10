namespace ShellInterop.Notifications;

/// <summary>
/// Sends toasts through the Windows SDK .NET projection (no Windows App SDK) and inspects shortcut registration read-only.
/// Sending and registration are separate: this class never creates, edits or deletes shortcuts or registry values, never
/// repairs a registration, and does not refuse to send because a shortcut is missing (the OS may still display from cache).
/// Threading: <see cref="Send"/> and <see cref="GetSetting"/> may be called from any thread (the PoC used MTA) and block only
/// for the synchronous WinRT call. <see cref="InspectRegistration"/> runs the Shell COM work on its own short-lived background
/// STA thread and waits for it (60 s limit; on timeout the result is Unknown and the thread is abandoned, never aborted).
/// Only one registration scan runs per process at a time, across all publisher instances: while a scan (including one
/// abandoned after a timeout) is still running, further calls return Unknown with a "busy" warning and start no new thread.
/// A late-finishing scan only releases that slot; its result is discarded. The instance holds no unmanaged resources, so
/// there is nothing to dispose; the STA thread is a background thread and does not keep the process alive. The assembly does
/// not depend on ShellInterop.Core (the AUMID rule is duplicated internally).
/// </summary>
public sealed class NotificationPublisher
{
    private readonly string _aumid;
    private readonly IToastBackend _backend;
    private readonly IShortcutScanner _scanner;

    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The AUMID is invalid.</exception>
    public NotificationPublisher(NotificationPublisherOptions options)
        : this(options, new WinRtToastBackend(), new StartMenuShortcutScanner())
    {
    }

    internal NotificationPublisher(NotificationPublisherOptions options, IToastBackend backend, IShortcutScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? error = NotificationPublisherOptions.ValidateAumid(options.Aumid);
        if (error is not null) throw new ArgumentException(error, nameof(options));
        _aumid = options.Aumid;
        _backend = backend;
        _scanner = scanner;
    }

    /// <summary>The validated AUMID used for sending and inspection.</summary>
    public string Aumid => _aumid;

    /// <summary>
    /// Reads the Windows notification setting for the AUMID. Returns <see cref="NotificationSettingState.Unknown"/> if it
    /// cannot be read; that is never evidence that notifications are disabled. Never throws for OS failures.
    /// </summary>
    public NotificationSettingState GetSetting() => ReadSetting(out _);

    /// <summary>
    /// Submits the toast. The setting is read first for reporting only; it does not block sending. OS failures are returned
    /// in the result (never thrown). <see cref="NotificationSendResult.Submitted"/> == true means only that the API call
    /// succeeded, not that a toast became visible.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is null.</exception>
    public NotificationSendResult Send(NotificationDefinition notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var setting = ReadSetting(out int? settingHr);
        try
        {
            _backend.Show(_aumid, notification.ToToastXml());
            return new NotificationSendResult(true, setting, 0, null, settingHr);
        }
        catch (Exception e)
        {
            return new NotificationSendResult(false, setting, e.HResult, $"{e.GetType().Name}: {e.Message}", settingHr);
        }
    }

    /// <summary>
    /// Read-only scan of the current user's and the common Start Menu (*.lnk, recursive) for shortcuts whose
    /// System.AppUserModel.ID equals the AUMID. Reports ShortcutFound / ShortcutNotFound / Unknown; read or enumeration
    /// errors, a timeout, or a busy scanner yield Unknown (never "not found"). The result is static evidence only and is not the same as what the OS
    /// will currently display. Blocks the caller while a dedicated STA thread scans; may take noticeable time on large Start Menus.
    /// </summary>
    /// <param name="expectedExecutablePath">Optional full path of the sending executable; each match reports whether its stored target equals it.</param>
    public NotificationRegistrationInspection InspectRegistration(string? expectedExecutablePath = null)
    {
        ScanResult scan;
        try { scan = _scanner.Scan(_aumid); }
        catch (Exception e)
        {
            return new NotificationRegistrationInspection(NotificationRegistrationState.Unknown, _aumid, [],
                [$"scan failed: {e.GetType().Name} 0x{e.HResult:X8}: {e.Message}"]);
        }

        string? expected = NormalizePath(expectedExecutablePath);
        var shortcuts = scan.Matches
            .Select(m => new NotificationShortcutInfo(m.Path, m.TargetPath,
                expected is null ? null : string.Equals(NormalizePath(m.TargetPath), expected, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var state = shortcuts.Count > 0 ? NotificationRegistrationState.ShortcutFound
            : scan.Warnings.Count == 0 ? NotificationRegistrationState.ShortcutNotFound
            : NotificationRegistrationState.Unknown;
        return new NotificationRegistrationInspection(state, _aumid, shortcuts, scan.Warnings);
    }

    private NotificationSettingState ReadSetting(out int? settingHr)
    {
        settingHr = null;
        try { return _backend.GetSetting(_aumid); }
        catch (Exception e) { settingHr = e.HResult; return NotificationSettingState.Unknown; }
    }

    private static string? NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return Path.GetFullPath(p); } catch (Exception) { return p; }
    }
}
