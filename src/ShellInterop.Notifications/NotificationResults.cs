namespace ShellInterop.Notifications;

/// <summary>
/// Windows notification setting for the AUMID, as reported by the OS. Independent of both the send API result and the
/// shortcut registration state. <see cref="Enabled"/> does not mean the toast will be displayed.
/// <see cref="Unknown"/> means the setting could not be read; it does NOT mean disabled.
/// </summary>
public enum NotificationSettingState
{
    /// <summary>The setting could not be read or is not a known value. Not an indication of "disabled".</summary>
    Unknown = 0,
    /// <summary>Windows reports notifications enabled for the AUMID (says nothing about registration or display).</summary>
    Enabled,
    /// <summary>Disabled for this application in Windows settings.</summary>
    DisabledForApplication,
    /// <summary>Disabled for the user (for example Focus assist / global toggle).</summary>
    DisabledForUser,
    /// <summary>Disabled by group policy.</summary>
    DisabledByGroupPolicy,
    /// <summary>Disabled because the app manifest does not allow toasts.</summary>
    DisabledByManifest,
}

/// <summary>
/// Outcome of <see cref="NotificationPublisher.Send"/>. Three independent facts are reported separately:
/// A) whether the Windows send API call succeeded (<see cref="Submitted"/>), B) the Windows notification setting
/// (<see cref="Setting"/>), and C) the shortcut registration state, which is obtained from
/// <see cref="NotificationPublisher.InspectRegistration"/> and is never part of this result.
/// <see cref="Submitted"/> == true does NOT mean the toast was shown: a disabled setting or a missing/stale AUMID
/// registration can still suppress it while the API reports success. Whether a toast is visible can only be observed by a user.
/// </summary>
/// <param name="Submitted">The Windows API accepted the toast (ToastNotifier.Show returned without an exception).</param>
/// <param name="Setting">Setting read before sending; <see cref="NotificationSettingState.Unknown"/> if it could not be read.</param>
/// <param name="HResult">0 when <paramref name="Submitted"/>; otherwise the HRESULT of the failure.</param>
/// <param name="FailureReason">Exception type and message when not submitted; otherwise null.</param>
/// <param name="SettingHResult">HRESULT of the failed setting read when <paramref name="Setting"/> is Unknown because of an error; otherwise null.</param>
public sealed record NotificationSendResult(
    bool Submitted,
    NotificationSettingState Setting,
    int HResult,
    string? FailureReason,
    int? SettingHResult);

/// <summary>Result of the read-only Start Menu shortcut scan. Static evidence only; not proof of what the OS will display.</summary>
public enum NotificationRegistrationState
{
    /// <summary>The scan could not be completed (read/enumeration errors) and found no matching shortcut. Not "missing".</summary>
    Unknown = 0,
    /// <summary>At least one Start Menu shortcut carries the AUMID. Does not guarantee the OS registration is valid or current.</summary>
    ShortcutFound,
    /// <summary>The scan completed without errors and no shortcut carries the AUMID. The OS may still display toasts briefly from cached registration.</summary>
    ShortcutNotFound,
}

/// <summary>A Start Menu shortcut that carries the inspected AUMID.</summary>
/// <param name="Path">Full path of the .lnk file.</param>
/// <param name="TargetPath">Stored target path of the shortcut (not resolved), or null.</param>
/// <param name="TargetMatchesExpected">Whether <paramref name="TargetPath"/> equals the expected executable (case-insensitive full path); null when no expected path was given.</param>
public sealed record NotificationShortcutInfo(string Path, string? TargetPath, bool? TargetMatchesExpected);

/// <summary>
/// Result of <see cref="NotificationPublisher.InspectRegistration"/>: read-only scan of the current user's and the common
/// Start Menu for shortcuts carrying the AUMID. It does not read the registry or OS internal caches, does not repair anything,
/// and does not guarantee display: after a shortcut is removed the OS has been observed to keep showing toasts for a while
/// (observed in SHELL-NOTIF-02; how long, and what triggers the refresh, is unverified).
/// </summary>
/// <param name="State">Classification; see <see cref="NotificationRegistrationState"/>.</param>
/// <param name="Aumid">Inspected AUMID.</param>
/// <param name="Shortcuts">Matching shortcuts (may be non-empty even when <paramref name="Warnings"/> is non-empty).</param>
/// <param name="Warnings">Enumeration/read problems encountered; any warning prevents <see cref="NotificationRegistrationState.ShortcutNotFound"/>.</param>
public sealed record NotificationRegistrationInspection(
    NotificationRegistrationState State,
    string Aumid,
    IReadOnlyList<NotificationShortcutInfo> Shortcuts,
    IReadOnlyList<string> Warnings);
