namespace ShellInterop.Notifications;

/// <summary>Options for <see cref="NotificationPublisher"/>.</summary>
public sealed record NotificationPublisherOptions
{
    /// <summary>
    /// AppUserModelID the toast is sent as: 'Company.Product[.Sub...]', at least two non-empty segments of ASCII letters,
    /// digits, '-' or '_', at most 128 characters (same rule as ShellInterop.Core). The library never registers this ID.
    /// </summary>
    public required string Aumid { get; init; }

    internal const int AumidMaxLength = 128;

    // Intentionally duplicated: this assembly does not reference ShellInterop.Core. Keep the rule identical to Core's internal
    // AppUserModelIdValidation (a parity test in the test project compares both). Input is never normalized; it is rejected
    // here, before any Windows API is called.
    internal static string? ValidateAumid(string? id)
    {
        if (id is null) return "AUMID is null.";
        if (id.Length == 0) return "AUMID is empty.";
        if (id.Length > AumidMaxLength) return $"AUMID has {id.Length} characters; the limit is {AumidMaxLength}.";
        string[] segments = id.Split('.');
        bool ok = segments.Length >= 2 && segments.All(s => s.Length > 0 && s.All(IsAumidChar));
        return ok ? null : "AUMID must be 'Company.Product[.Sub...]' with non-empty segments of ASCII letters, digits, '-' or '_'.";
    }

    private static bool IsAumidChar(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
