using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ShellInterop.Notifications;

/// <summary>Seam over the WinRT calls so the send/Setting logic is unit-testable without showing a toast.</summary>
internal interface IToastBackend
{
    /// <summary>Throws if the setting cannot be read.</summary>
    NotificationSettingState GetSetting(string aumid);

    /// <summary>Throws on API failure.</summary>
    void Show(string aumid, string toastXml);
}

// Call path proven in SHELL-NOTIF-02 method P: XmlDocument.LoadXml -> ToastNotification -> CreateToastNotifier(aumid).Show.
internal sealed class WinRtToastBackend : IToastBackend
{
    public NotificationSettingState GetSetting(string aumid) =>
        ToastNotificationManager.CreateToastNotifier(aumid).Setting switch
        {
            NotificationSetting.Enabled => NotificationSettingState.Enabled,
            NotificationSetting.DisabledForApplication => NotificationSettingState.DisabledForApplication,
            NotificationSetting.DisabledForUser => NotificationSettingState.DisabledForUser,
            NotificationSetting.DisabledByGroupPolicy => NotificationSettingState.DisabledByGroupPolicy,
            NotificationSetting.DisabledByManifest => NotificationSettingState.DisabledByManifest,
            _ => NotificationSettingState.Unknown,
        };

    public void Show(string aumid, string toastXml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(toastXml);
        var toast = new ToastNotification(doc);
        ToastNotificationManager.CreateToastNotifier(aumid).Show(toast);
    }
}
