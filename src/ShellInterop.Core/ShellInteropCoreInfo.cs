using System.Reflection;

namespace ShellInterop.Core;

/// <summary>Provides build information about the ShellInterop.Core assembly.</summary>
public static class ShellInteropCoreInfo
{
    /// <summary>
    /// Gets the informational version of the ShellInterop.Core assembly (for example <c>0.2.0-preview.1</c>),
    /// without any <c>+</c> build metadata suffix.
    /// </summary>
    /// <value>The version string, or <c>"0.0.0"</c> if the assembly has no informational version attribute.</value>
    public static string Version { get; } =
        typeof(ShellInteropCoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
