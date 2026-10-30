# ShellInterop.Core

A Windows-only, UI-framework-independent .NET library that edits the Jump List **Tasks** category through the
documented Windows Shell COM API (`ICustomDestinationList`). It has no package references and does not depend on
WPF, WinForms, Avalonia, the Windows App SDK or any third-party Jump List library. It can be used from Avalonia, WPF,
WinForms or console applications.

> **Status: pre-release (version 0.1.0-preview.1). Not yet published on NuGet.org. Not recommended for production use.**
> Only one real-Shell operation has been verified so far (see [Verification status](#verification-status)).

ShellInterop.Core is an unofficial, independently developed library. It is not developed, provided or endorsed by Microsoft.

## Requirements

- .NET 10, a `net10.0-windows` (or later Windows) project.
- Your own AppUserModelID (AUMID) such as `Company.Product` (at most 128 characters, no spaces).

The library targets `net10.0-windows` only. A plain `net10.0` project cannot reference it (NU1201/NU1202). Cross-platform
applications multi-target `net10.0;net10.0-windows` and reference the library only from the Windows target.

## Quick start

```csharp
using ShellInterop.Core;
using ShellInterop.Core.JumpLists;

// 1. First thing in Main, before any window: set the process AppUserModelID (throws ArgumentException if invalid).
int hr = ProcessAppIdentity.Set("YourCompany.YourApp");   // hr < 0: Shell call failed, skip the Jump List

// 2. One publisher per app (starts one STA worker thread).
var publisher = new JumpListPublisher();

// 3. Replace the Tasks (the whole list is replaced on every call).
string exe = Environment.ProcessPath!;
JumpListApplyResult result = await publisher.ApplyAsync(new JumpListDefinition("YourCompany.YourApp",
[
    new JumpTaskDefinition { Title = "New window", ApplicationPath = exe, Arguments = "--new-window", IconResourcePath = exe },
]));

// 4. Check the result. ShellState.Applied means the Shell API succeeded, not that someone saw it on the taskbar.
if (!result.Succeeded)
{
    Console.Error.WriteLine($"{result.Status} phase={result.FailurePhase} hr={result.Error?.HResultText} shell={result.ShellState}");
}

// 5. On exit: wait for the worker, then dispose.
JumpListShutdownResult shutdown = await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
if (shutdown.Completed) publisher.Dispose();
```

A longer walkthrough (an Avalonia sample, error handling, shutdown inside a UI framework) is in
[docs/USAGE-AVALONIA.md](docs/USAGE-AVALONIA.md). The guide is written in Japanese.

`JumpListPublisher` writes the real Jump List of whatever AppUserModelID you give it. The library has no approval or
isolation gate; the calling application decides when and what to write.

### Shutdown

Do not close the application while a Jump List transaction may be running. `await publisher.ShutdownAsync(timeout)`
first: it rejects new work, completes queued-but-unstarted requests as `PublisherDisposed`, waits for the in-flight COM
transaction to finish and release its objects, and for the STA worker to exit. `Completed == false` means it is **not**
done (the Shell state is unknown), so keep the host open. `Dispose()` is a best-effort synchronous stop that never aborts
the worker.

## Using a local package

The package is not published on NuGet.org. To try it, pack it yourself and use a local feed:

```
dotnet pack src/ShellInterop.Core -c Release -p:Version=0.1.0-local.1 -o <local feed folder>
```

Then reference it, for example from a Windows-only project:

```xml
<PackageReference Include="ShellInterop.Core" Version="0.1.0-local.1" />
```

For a multi-targeted project, add the reference conditionally on the Windows target framework.

## Verification status

| Evidence | Result |
|---|---|
| Unit tests (fake Shell, in-memory COM objects, SDK-header cross-checks) | Included in `tests/ShellInterop.Core.Tests`. They never touch a Jump List. A pass is not a real-Shell result |
| Real Shell, first test: one `ApplyAsync` of two Tasks, seen on the taskbar of a Windows guest VM | PASS (single operation) |

**Not verified against the real Shell yet:** clearing with an empty Tasks list, `DeleteListAsync`, replacing one Tasks set
with another, launching the app from a Jump List task, persistence after a restart. Those paths are covered by unit tests
with a fake Shell only.

## Scope

Implemented: Jump List **Tasks** management, Windows Shell COM interop, AppUserModelID helpers, the STA worker boundary,
COM lifetime handling, apply/delete results and error reporting.

Not implemented: categories, Recent/Frequent, separators, any UI, settings screens, app-specific IPC, update services,
installers, taskbar pin management. Possible future Shell features (DWM thumbnails, DWM Peek, other taskbar Win32 work) are
not implemented; the name `ShellInterop.Core` is not tied to the Jump List feature for that reason.

### OS support

1. `net10.0-windows` means target platform version 7.0. This is a compile-time and NuGet asset-selection setting. It does
   **not** mean Windows 7 is supported.
2. The .NET 10 runtime's own supported-OS list applies.
3. Windows 11 is the primary target. A formal minimum OS requirement is not decided, no OS version check is implemented,
   and behavior on other OS versions has not been verified.

## Build and test

```
dotnet restore ShellInterop.Core.sln
dotnet build ShellInterop.Core.sln -c Release
dotnet test ShellInterop.Core.sln -c Release
```

The tests use a fake Shell and never write a real Jump List.

## Documentation

| File | Content |
|---|---|
| [docs/USAGE-AVALONIA.md](docs/USAGE-AVALONIA.md) | Usage guide with an Avalonia sample (Japanese) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Structure of the library (Japanese) |
| [docs/ERROR-MODEL.md](docs/ERROR-MODEL.md) | Result and error model (Japanese) |
| [docs/API-CONTRACT-REVIEW.md](docs/API-CONTRACT-REVIEW.md) | How the COM declarations were checked against the Windows SDK headers (Japanese) |

## Development and AI Assistance

ShellInterop.Core was developed and is maintained by moz, with substantial assistance from OpenAI ChatGPT, OpenAI Codex,
and Anthropic Claude Code.

AI assistance included architecture and API design discussions, implementation planning, code generation, debugging,
testing, and code review.

Final project decisions and responsibility remain with the maintainer.

## License

MIT License, Copyright (c) 2026 moz. See [LICENSE](LICENSE).

Adopting MIT does not grant rights held by third parties. Two questions are still open and are not resolved by this
license: the permission status of COM declarations (GUIDs, vtable layouts) derived from Windows SDK headers, and rights
review of code produced with AI assistance.
