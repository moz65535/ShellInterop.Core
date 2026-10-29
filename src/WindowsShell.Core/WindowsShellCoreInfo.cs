using System.Reflection;

namespace WindowsShell.Core;

public static class WindowsShellCoreInfo
{
    public static string Version { get; } =
        typeof(WindowsShellCoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
