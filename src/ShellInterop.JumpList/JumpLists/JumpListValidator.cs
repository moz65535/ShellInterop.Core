using ShellInterop.Core;

namespace ShellInterop.Core.JumpLists;

/// <summary>
/// Pure input validation. Limits marked "local" are conservative caps chosen here, not Shell-documented values.
/// </summary>
public static class JumpListValidator
{
    /// <summary>Documented: at most 128 characters, no spaces (Application User Model IDs, learn.microsoft.com).</summary>
    public const int MaxAppIdLength = AppUserModelIdValidation.MaxLength;

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

        for (int i = 0; i < definition.Entries.Count; i++)
        {
            var entry = definition.Entries[i];
            if (entry is null)
                errors.Add(new("Entry.Null", "Entry is null.", i));
            else if (!entry.IsSeparator)
                ValidateTask(entry.Task, i, options, errors);
        }

        ValidateCategoryAndTracking(definition, options, errors);
        return errors;
    }

    public const int MaxDestinations = 100;   // local
    public const int MaxMatchOnly = 1000;     // local
    public const int MaxKeyLength = 256;      // local
    public const int MaxCategoryNameLength = 256; // local

    private static void ValidateCategoryAndTracking(JumpListDefinition definition, JumpListPublisherOptions options, List<JumpListValidationError> errors)
    {
        var categories = definition.Categories;
        var tracking = definition.RemovedDestinationTracking;

        if (categories.Count > 0 && tracking is null)
        {
            errors.Add(new("Tracking.Required", "A category requires RemovedDestinationTracking: without it CommitList would silently discard the user's removals."));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var arguments = new HashSet<string>(StringComparer.Ordinal);

        void CheckKey(string? key, string prefix, int index)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(new($"{prefix}.KeyEmpty", "Key is empty.", index));
                return;
            }

            if (key.Length > MaxKeyLength || key.Any(char.IsControl))
            {
                errors.Add(new($"{prefix}.KeyInvalid", $"Key must be at most {MaxKeyLength} characters without control characters.", index));
            }

            if (!keys.Add(key))
            {
                errors.Add(new("Destination.KeyDuplicate", "Key is used more than once among destinations and MatchOnly entries.", index));
            }
        }

        void CheckArguments(string? args, int index)
        {
            if (args is not null && !arguments.Add(args))
            {
                errors.Add(new("Category.DuplicateDestination", "Arguments are used more than once (Ordinal); the Shell cannot tell such links apart.", index));
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (category is null)
            {
                errors.Add(new("Category.Null", "Category is null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(category.Name))
            {
                errors.Add(new("Category.NameEmpty", "Category name is empty."));
            }
            else if (category.Name.Length > MaxCategoryNameLength)
            {
                errors.Add(new("Category.NameTooLong", $"Category name exceeds {MaxCategoryNameLength} characters."));
            }
            else if (category.Name.Any(char.IsControl))
            {
                errors.Add(new("Category.NameControlChar", "Category name contains control characters."));
            }
            else if (!names.Add(category.Name))
            {
                errors.Add(new("Category.NameDuplicate", "Category names must be unique (case-insensitive); names are never renamed automatically."));
            }

            if (category.Destinations is null)
            {
                errors.Add(new("Category.DestinationsNull", "Destinations is null."));
            }
            else
            {
                if (category.Destinations.Count > MaxDestinations)
                {
                    errors.Add(new("Category.TooMany", $"At most {MaxDestinations} destinations are accepted (local cap)."));
                }

                for (int i = 0; i < category.Destinations.Count; i++)
                {
                    var d = category.Destinations[i];
                    if (d is null)
                    {
                        errors.Add(new("Destination.Null", "Destination is null.", i));
                        continue;
                    }

                    CheckKey(d.Key, "Destination", i);
                    ValidateTask(d.Link, i, options, errors, "Destination.");
                    CheckArguments(d.Link?.Arguments, i);
                }
            }
        }

        if (tracking is not null)
        {
            if (tracking.ExcludedDestinationKeys is null || tracking.ExcludedDestinationKeys.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add(new("Tracking.ExcludedKeyInvalid", "ExcludedDestinationKeys must be non-null with non-empty keys."));
            }

            if (tracking.MatchOnly is null)
            {
                errors.Add(new("Tracking.MatchOnlyNull", "MatchOnly is null."));
            }
            else
            {
                if (tracking.MatchOnly.Count > MaxMatchOnly)
                {
                    errors.Add(new("Tracking.TooManyMatchOnly", $"At most {MaxMatchOnly} MatchOnly entries are accepted (local cap)."));
                }

                for (int i = 0; i < tracking.MatchOnly.Count; i++)
                {
                    var m = tracking.MatchOnly[i];
                    if (m is null)
                    {
                        errors.Add(new("Match.Null", "MatchOnly entry is null.", i));
                        continue;
                    }

                    CheckKey(m.Key, "Match", i);
                    void Add(string code, string message) => errors.Add(new("Match." + code, message, i));
                    CheckText(m.Arguments, "Arguments", MaxArgumentsLength, required: true, Add);
                    CheckArguments(m.Arguments, i);
                    if (m.ApplicationPath is null)
                    {
                        if (m.PathPolicy == JumpDestinationPathPolicy.RequirePathMatch)
                        {
                            Add("PathRequired", "ApplicationPath is required unless PathPolicy is AllowArgumentsOnly.");
                        }
                    }
                    else
                    {
                        CheckPath(m.ApplicationPath, "ApplicationPath", Add);
                    }
                }
            }
        }

        var normal = new HashSet<(string, string)>(
            definition.Tasks.Where(t => t is not null).Select(t => (t!.ApplicationPath?.ToUpperInvariant() ?? "", t.Arguments ?? "")));
        var seen = new HashSet<(string, string)>();
        foreach (var fallback in categories.Select(c => c?.TasksFallback).OfType<IReadOnlyList<JumpListEntry?>>())
        {
            int taskCount = 0;
            for (int i = 0; i < fallback.Count; i++)
            {
                var e = fallback[i];
                if (e is null)
                {
                    errors.Add(new("Fallback.Invalid", "Fallback entry is null.", i));
                    continue;
                }

                if (e.IsSeparator)
                {
                    continue;
                }

                taskCount++;
                ValidateTask(e.Task, i, options, errors, "Fallback.");
                if (e.Key is not null && !keys.Contains(e.Key))
                {
                    errors.Add(new("Fallback.Invalid", "A keyed fallback entry must use the Key of a destination or MatchOnly entry.", i));
                }

                if (e.Task is { } t)
                {
                    var id = (t.ApplicationPath?.ToUpperInvariant() ?? "", t.Arguments ?? "");
                    if (normal.Contains(id) || !seen.Add(id))
                    {
                        errors.Add(new("Fallback.Invalid", "Fallback task duplicates an ordinary or another fallback task (same path and arguments).", i));
                    }
                }
            }

            if (taskCount > MaxTasks)
            {
                errors.Add(new("Fallback.Invalid", $"At most {MaxTasks} fallback tasks are accepted (local cap)."));
            }
        }
    }

    public static IReadOnlyList<JumpListValidationError> ValidateAppId(string? appId, JumpListPublisherOptions? options = null)
    {
        var errors = new List<JumpListValidationError>();
        ValidateAppId(appId, options ?? new JumpListPublisherOptions(), errors);
        return errors;
    }

    private static void ValidateAppId(string? id, JumpListPublisherOptions options, List<JumpListValidationError> errors)
    {
        if (AppUserModelIdValidation.Validate(id, options.AppIdFilter) is { } error)
        {
            errors.Add(new(error.Code, error.Message));
        }
    }

    private static void ValidateTask(JumpTaskDefinition? task, int index, JumpListPublisherOptions options, List<JumpListValidationError> errors, string codePrefix = "")
    {
        void Add(string code, string message) => errors.Add(new(codePrefix + code, message, index));

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
