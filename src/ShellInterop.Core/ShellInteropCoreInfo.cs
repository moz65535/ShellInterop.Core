using System.Reflection;

namespace ShellInterop.Core;

public static class ShellInteropCoreInfo
{
    public static string Version { get; } =
        typeof(ShellInteropCoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
