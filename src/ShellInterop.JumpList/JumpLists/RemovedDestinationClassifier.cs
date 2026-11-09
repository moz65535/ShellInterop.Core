using ShellInterop.Core.JumpLists.Native;

namespace ShellInterop.Core.JumpLists;

/// <summary>Identity used to recognise a removed-list link. Built from category destinations and MatchOnly entries.</summary>
internal sealed record DestinationMatcher(string Key, string Arguments, string? ApplicationPath, JumpDestinationPathPolicy PathPolicy);

/// <summary>
/// Pure classification of the raw removed list. Matching uses only Arguments (Ordinal) and path (OrdinalIgnoreCase);
/// display names and positions are never consulted. Anything that cannot be identified safely is Unreadable/Ambiguous.
/// </summary>
internal static class RemovedDestinationClassifier
{
    public static IReadOnlyList<DestinationMatcher> BuildMatchers(JumpListDefinition definition)
    {
        var list = new List<DestinationMatcher>();
        foreach (var category in definition.Categories)
        {
            foreach (var d in category!.Destinations)
            {
                list.Add(new(d.Key, d.Link.Arguments, d.Link.ApplicationPath, d.PathPolicy));
            }
        }

        if (definition.RemovedDestinationTracking is { } tracking)
        {
            foreach (var m in tracking.MatchOnly)
            {
                list.Add(new(m.Key, m.Arguments, m.ApplicationPath, m.PathPolicy));
            }
        }

        return list;
    }

    public static IReadOnlyList<RemovedDestination> Classify(
        IReadOnlyList<RawRemovedDestination> raw,
        IReadOnlyList<DestinationMatcher> matchers,
        IReadOnlySet<string> excluded)
    {
        var result = new List<RemovedDestination>(raw.Count);
        foreach (var item in raw)
        {
            result.Add(item.Kind switch
            {
                RawRemovedKind.ShellItem => new(null, RemovedDestinationKind.ForeignItem, MatchConfidence.None, false, null, null),
                RawRemovedKind.Link => ClassifyLink(item.Path, item.Arguments, matchers, excluded),
                _ => new(null, RemovedDestinationKind.Unreadable, MatchConfidence.None, false, null, null),
            });
        }

        return result;
    }

    private static RemovedDestination ClassifyLink(string? path, string? arguments, IReadOnlyList<DestinationMatcher> matchers, IReadOnlySet<string> excluded)
    {
        if (path is null || arguments is null)
        {
            return new(null, RemovedDestinationKind.Unreadable, MatchConfidence.None, false, path, arguments);
        }

        var candidates = matchers.Where(m => string.Equals(m.Arguments, arguments, StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0)
        {
            return new(null, RemovedDestinationKind.Foreign, MatchConfidence.None, false, path, arguments);
        }

        if (candidates.Length > 1)
        {
            return new(null, RemovedDestinationKind.Ambiguous, MatchConfidence.None, false, path, arguments);
        }

        var m = candidates[0];
        if (m.ApplicationPath is not null && string.Equals(m.ApplicationPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return new(m.Key, RemovedDestinationKind.Matched, MatchConfidence.Exact, excluded.Contains(m.Key), path, arguments);
        }

        return m.PathPolicy == JumpDestinationPathPolicy.AllowArgumentsOnly
            ? new(m.Key, RemovedDestinationKind.Matched, MatchConfidence.ArgumentsOnly, excluded.Contains(m.Key), path, arguments)
            : new(null, RemovedDestinationKind.Ambiguous, MatchConfidence.None, false, path, arguments);
    }
}
