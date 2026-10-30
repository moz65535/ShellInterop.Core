# WindowsShell.Core

A small, **Windows-only**, UI-framework-independent .NET library that manages the **Jump List Tasks** category of a
taskbar button through the documented Windows Shell COM API (`ICustomDestinationList`). It works from Avalonia, WPF,
WinForms or a plain console/background process: it has no UI dependency and **no package dependencies**.

> **Pre-release / PoC (version 0.1.0-preview.1).** Not production ready. See "Status and limits" below.

WindowsShell.Core is an unofficial, independently developed library. It is not developed, provided or endorsed by Microsoft.

## Target framework

`net10.0-windows` only. A plain `net10.0` project cannot reference it (NuGet reports NU1201/NU1202). Cross-platform
apps multi-target `net10.0;net10.0-windows` and reference the package only from the Windows target:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
  <PackageReference Include="WindowsShell.Core" Version="..." />
</ItemGroup>
```

## Basic use

1. Choose your own AppUserModelID (`Company.Product`, at most 128 characters, no spaces) and set it **first thing in
   `Main`, before any window is created**. `ProcessAppIdentity.Set` throws `ArgumentException` for an invalid ID.
2. Create **one** `JumpListPublisher` per app (it starts one STA worker thread).
3. `ApplyAsync` **replaces the whole Tasks list** in a single transaction on every call.
4. Read the returned `JumpListApplyResult`. `ShellState.Applied` means the Shell API succeeded; it is not a visual
   confirmation (`VisuallyConfirmed` is always `false`).
5. On exit, `await publisher.ShutdownAsync(timeout)` and call `Dispose()` only when it completed.

```csharp
using WindowsShell.Core;
using WindowsShell.Core.JumpLists;

ProcessAppIdentity.Set("YourCompany.YourApp");
var publisher = new JumpListPublisher();

string exe = Environment.ProcessPath!;
JumpListApplyResult result = await publisher.ApplyAsync(new JumpListDefinition("YourCompany.YourApp",
[
    new JumpTaskDefinition { Title = "New window", ApplicationPath = exe, Arguments = "--new-window", IconResourcePath = exe },
]));

JumpListShutdownResult shutdown = await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
if (shutdown.Completed) publisher.Dispose();
```

`JumpListPublisher` writes the real Jump List of the AppUserModelID you give it; deciding when to write is up to the
calling app. Do not close the app while a transaction may be running: call `ShutdownAsync` first.

## Status and limits

- **Not implemented:** custom categories, Recent/Frequent, separators, differential updates, a dedicated Clear method
  (an empty Tasks list is applied as a replacement), taskbar pin management, thumbnails/Peek.
- **Verified against the real Windows Shell:** one `ApplyAsync` of two Tasks, observed in a Windows guest VM.
- **Not verified against the real Shell:** clearing with an empty list, `DeleteListAsync`, replacing one Tasks set
  with another, launching the app from a task, persistence after a restart. These paths are covered by unit tests with
  a fake Shell only.
- Windows 11 is the primary target; no minimum OS version is formally decided and no OS version check is implemented.
- Framework-dependent: the .NET 10 runtime is not included.

## License

MIT License. Copyright (c) 2026 moz. See the `LICENSE` file in the package.
