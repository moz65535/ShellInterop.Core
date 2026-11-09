# ShellInterop.Core

Two Windows-only, UI-framework-independent .NET libraries:

- `ShellInterop.Core`: shared process AppUserModelID and Win32 foundation.
- `ShellInterop.JumpList`: Jump List Tasks publisher and Shell COM implementation; references Core.

> **Development split; not published on NuGet.org.** The existing Core version remains 0.1.0-preview.1; do not publish this breaking source tree under that old version. The split without separators passed real Shell regression in SHELL-SPLIT-02; separator display has not yet been verified in the real Shell.

These libraries are independently developed and are not Microsoft products.

## Migration and compatibility

Earlier `ShellInterop.Core` contained the JumpList API. The split moves its public types into the `ShellInterop.JumpList` assembly. The namespace `ShellInterop.Core.JumpLists` and existing method signatures remain unchanged. Add a reference to `ShellInterop.JumpList` (and Core when using `ProcessAppIdentity`), then rebuild. A Core-only reference no longer supplies JumpList types; binary compatibility with existing compiled consumers is not guaranteed. No type forwarders are provided. Existing users may continue using the old Core release or old local package.

For a future release, select and document a new preview version (for example `0.2.0-preview.1`) for the coordinated packages; that is a proposal, not a version change or a release. Independently review package metadata, dependency versions, XML documentation, and license/third-party rights before publishing.

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

### Separators in Tasks

The original `new JumpListDefinition(appId, tasks)` remains supported. To mix entries, use the new factory:

```csharp
var definition = JumpListDefinition.FromEntries(appId,
[
    JumpListEntry.ForTask(firstTask),
    JumpListEntry.Separator(),
    JumpListEntry.ForTask(secondTask),
]);
await publisher.ApplyAsync(definition);
```

`Entries` is the authoritative ordered input; `Tasks` is a task-only view. Input is copied. Error `TaskIndex` refers to the zero-based entry position for this factory; existing task-only callers retain their old index. `RequestedTaskCount` and the local 100-task limit exclude separators. Empty entries retain the existing empty-Tasks behavior; separator-only input is passed to the Shell. The library does not normalize leading, trailing or adjacent separators: Windows ignores the first/last and suppresses one of adjacent separators. BeginList's minimum-slot count excludes separators and headers only while their total is at most four; extras can affect visible destinations. This library does not truncate to that count. Real Shell separator display is not yet verified.

### Shutdown

Do not close the application while a Jump List transaction may be running. `await publisher.ShutdownAsync(timeout)`
first: it rejects new work, completes queued-but-unstarted requests as `PublisherDisposed`, waits for the in-flight COM
transaction to finish and release its objects, and for the STA worker to exit. `Completed == false` means it is **not**
done (the Shell state is unknown), so keep the host open. `Dispose()` is a best-effort synchronous stop that never aborts
the worker.

## Project references and future packages

For development, reference `src/ShellInterop.JumpList/ShellInterop.JumpList.csproj`. It transitively references Core; applications directly using Core APIs may reference Core explicitly. Future NuGet package IDs are `ShellInterop.Core` and `ShellInterop.JumpList`. Packaging and publication require a coordinated new version and have not been performed here.

## Verification status

| Evidence | Result |
|---|---|
| Unit tests (fake Shell, in-memory COM objects, SDK-header cross-checks) | Included in `tests/ShellInterop.Core.Tests`. They never touch a Jump List. A pass is not a real-Shell result |
| Real Shell, split assemblies without separators | SHELL-SPLIT-02: apply, navigation, RunItem, delete and re-register passed in the dedicated VM |
| Real Shell, separator display | Not yet verified |

**Still unverified against the real Shell:** clearing with an empty Tasks list, separator display, and persistence after a restart. Delete, replacement, and task launching passed SHELL-SPLIT-02 for the launcher scenario.

## Scope

Implemented: Jump List **Tasks** management, Windows Shell COM interop, AppUserModelID helpers, the STA worker boundary,
COM lifetime handling, apply/delete results and error reporting.

Also implemented in `ShellInterop.JumpList`: separators, one or more custom categories with removed-destination tracking (Tasks fallback on `E_ACCESSDENIED`). Real Shell verification: SHELL-JL-05 (single category) and SHELL-JL-07 (multiple categories; conditional PASS, a real `E_ACCESSDENIED` on a later category and taskbar-pin effects are not verified).

Not implemented: Recent/Frequent, any UI, settings screens, app-specific IPC, update services,
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

The tests use a fake Shell and never write a real Jump List. Some tests create in-memory COM Shell objects.

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

## Future design notes (not implemented)

- Custom categories may have a separately editable display name, initially copied from the group name, with an explicit copy-from-group action; renaming the group does not overwrite it.

Packing the JumpList project with the unchanged 0.1.0-preview.1 version is blocked: the old Core package lacks required split internals. For local packaging tests only, pass a coordinated version to both projects (for example `-p:Version=0.2.0-local.1`); do not publish this test version.

`JumpListEntry` is an API input model, not a persistence format; persist application data separately and reconstruct entries with the factories.
