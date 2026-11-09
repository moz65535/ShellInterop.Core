namespace ShellInterop.Core;

// Shared policy used by ProcessAppIdentity and JumpList; preserves the original validation order and messages.
internal static class AppUserModelIdValidation
{
    internal const int MaxLength = 128;

    internal static (string Code, string Message)? Validate(string? id, Predicate<string>? filter)
    {
        if (id is null) return ("AppId.Null", "AppUserModelID is null. Implicit/system AppUserModelIDs are never used.");
        if (id.Length == 0) return ("AppId.Empty", "AppUserModelID is empty.");
        if (id.Length > MaxLength) return ("AppId.TooLong", $"AppUserModelID has {id.Length} characters; the limit is {MaxLength}.");
        string[] segments = id.Split('.');
        bool ok = segments.Length >= 2 && segments.All(s => s.Length > 0 && s.All(IsAppIdChar));
        if (!ok) return ("AppId.InvalidFormat", "AppUserModelID must be 'Company.Product[.Sub...]' with non-empty segments of ASCII letters, digits, '-' or '_' and no spaces.");
        if (filter is not null && !filter(id)) return ("AppId.NotAllowed", "AppUserModelID is rejected by the configured AppIdFilter.");
        return null;
    }

    private static bool IsAppIdChar(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
