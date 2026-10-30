namespace ShellInterop.Core.JumpLists;

/// <summary>
/// Pure input validation. Limits marked "local" are conservative caps chosen here, not Shell-documented values.
/// </summary>
public static class JumpListValidator
{
    /// <summary>Documented: at most 128 characters, no spaces (Application User Model IDs, learn.microsoft.com).</summary>
    public const int MaxAppIdLength = 128;

    public const int MaxPathLength = 259;   // MAX_PATH - 1
    public const int MaxTitleLength = 256;  // local
    public const int MaxDescriptionLength = 1023; // INFOTIPSIZE - 1
    public const int MaxArgumentsLength = 2048; // local
    public const int MaxTasks = 100; // local

    private static readonly char[] ExtraInvalidPathChars = ['*', '?', '"', '<', '>', '|'];

    public static IReadOnlyList<JumpListValidationError> Validate(JumpListDefinition? definition, JumpListPublisherOptions? options = null)
    {
        options ??= new JumpListPublisherOptions();
        var errors = new List<JumpListValidationError>();
        if (definition is null)
        {
            errors.Add(new("Definition.Null", "Definition is null."));
            return errors;
        }

        ValidateAppId(definition.AppUserModelId, options, errors);

        if (definition.Tasks.Count > MaxTasks)
        {
            errors.Add(new("Tasks.TooMany", $"At most {MaxTasks} tasks are accepted (local cap); got {definition.Tasks.Count}."));
        }

        for (int i = 0; i < definition.Tasks.Count; i++)
        {
            ValidateTask(definition.Tasks[i], i, options, errors);
        }

        return errors;
    }

    public static IReadOnlyList<JumpListValidationError> ValidateAppId(string? appId, JumpListPublisherOptions? options = null)
    {
        var errors = new List<JumpListValidationError>();
        ValidateAppId(appId, options ?? new JumpListPublisherOptions(), errors);
        return errors;
    }

    private static void ValidateAppId(string? id, JumpListPublisherOptions options, List<JumpListValidationError> errors)
    {
        if (id is null)
        {
            errors.Add(new("AppId.Null", "AppUserModelID is null. Implicit/system AppUserModelIDs are never used."));
            return;
        }

        if (id.Length == 0)
        {
            errors.Add(new("AppId.Empty", "AppUserModelID is empty."));
            return;
        }

        if (id.Length > MaxAppIdLength)
        {
            errors.Add(new("AppId.TooLong", $"AppUserModelID has {id.Length} characters; the limit is {MaxAppIdLength}."));
            return;
        }

        // Documented form: CompanyName.ProductName[.SubProduct[.Version]], no spaces. Stricter local rule: ASCII
        // letters/digits/'-'/'_' per dot-separated segment, at least two non-empty segments.
        string[] segments = id.Split('.');
        bool ok = segments.Length >= 2 && segments.All(s => s.Length > 0 && s.All(IsAppIdChar));
        if (!ok)
        {
            errors.Add(new("AppId.InvalidFormat", "AppUserModelID must be 'Company.Product[.Sub...]' with non-empty segments of ASCII letters, digits, '-' or '_' and no spaces."));
            return;
        }

        if (options.AppIdFilter is { } filter && !filter(id))
        {
            errors.Add(new("AppId.NotAllowed", "AppUserModelID is rejected by the configured AppIdFilter."));
        }
    }

    private static bool IsAppIdChar(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    private static void ValidateTask(JumpTaskDefinition? task, int index, JumpListPublisherOptions options, List<JumpListValidationError> errors)
    {
        void Add(string code, string message) => errors.Add(new(code, message, index));

        if (task is null)
        {
            Add("Task.Null", "Task entry is null.");
            return;
        }

        CheckText(task.Title, "Title", MaxTitleLength, required: true, Add);
        CheckText(task.Description, "Description", MaxDescriptionLength, required: false, Add);
        // Arguments are required: AddUserTasks does not support argument-less IShellLinks (only blank separators).
        CheckText(task.Arguments, "Arguments", MaxArgumentsLength, required: true, Add);

        if (CheckPath(task.ApplicationPath, "ApplicationPath", Add))
        {
            if (!task.ApplicationPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Add("ApplicationPath.NotExe", "ApplicationPath must point to an .exe file.");
            }
            else if (options.RequireExistingFiles && !(options.FileExists ?? File.Exists)(task.ApplicationPath))
            {
                Add("ApplicationPath.NotFound", "ApplicationPath does not exist.");
            }
        }

        if (CheckPath(task.IconResourcePath, "IconResourcePath", Add)
            && options.RequireExistingFiles && !(options.FileExists ?? File.Exists)(task.IconResourcePath))
        {
            Add("IconResourcePath.NotFound", "IconResourcePath does not exist.");
        }

        if (task.WorkingDirectory is not null
            && CheckPath(task.WorkingDirectory, "WorkingDirectory", Add)
            && options.RequireExistingFiles && !(options.DirectoryExists ?? Directory.Exists)(task.WorkingDirectory))
        {
            Add("WorkingDirectory.NotFound", "WorkingDirectory does not exist.");
        }
    }

    private static void CheckText(string? value, string name, int max, bool required, Action<string, string> add)
    {
        if (value is null)
        {
            if (required)
            {
                add($"{name}.Null", $"{name} is null.");
            }

            return;
        }

        if (required && string.IsNullOrWhiteSpace(value))
        {
            add($"{name}.Empty", $"{name} is empty.");
        }

        if (value.Length > max)
        {
            add($"{name}.TooLong", $"{name} exceeds {max} characters.");
        }

        if (value.Any(char.IsControl))
        {
            add($"{name}.ControlChar", $"{name} contains control characters (including NUL).");
        }
    }

    /// <summary>Syntax-only path check; returns false (after reporting) when the path is unusable.</summary>
    private static bool CheckPath(string? path, string name, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            add($"{name}.Empty", $"{name} is empty.");
            return false;
        }

        if (path.Length > MaxPathLength)
        {
            add($"{name}.TooLong", $"{name} exceeds {MaxPathLength} characters.");
            return false;
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            add($"{name}.DevicePath", $"{name} must not be a device/extended-length path.");
            return false;
        }

        if (path.Any(char.IsControl) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(ExtraInvalidPathChars) >= 0)
        {
            add($"{name}.InvalidChars", $"{name} contains characters that are not valid in a Windows path.");
            return false;
        }

        if (!Path.IsPathFullyQualified(path) || path.Contains(".." + Path.DirectorySeparatorChar) || path.Contains(".." + Path.AltDirectorySeparatorChar))
        {
            add($"{name}.NotAbsolute", $"{name} must be a fully qualified path without '..' segments.");
            return false;
        }

        return true;
    }
}
