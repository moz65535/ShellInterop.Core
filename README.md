# ShellInterop.Core

English | [日本語](README.ja.md)

Three Windows-only, UI-framework-independent .NET libraries:

- `ShellInterop.Core`: shared process AppUserModelID and Win32 foundation.
- `ShellInterop.JumpList`: Jump List Tasks publisher and Shell COM implementation; references Core.
- `ShellInterop.Notifications`: Toast sender (Windows SDK .NET projection, no Windows App SDK) with read-only Start Menu shortcut inspection; does not reference Core or JumpList. See [Notifications](#notifications).

> **Development split; not published on NuGet.org.** The source tree now carries Core `0.2.0-preview.1` and JumpList `0.2.0-preview.1` (planned publication versions; the old Core `0.1.0-preview.1` must not be overwritten by this breaking tree) and Notifications `0.1.0-preview.1`. As of SHELL-PACK-02 no `ShellInterop.*` package ID was found on NuGet.org. The split passed real Shell regression in SHELL-SPLIT-02. Separators, custom categories and removed-destination tracking have since been checked on the real Shell under limited conditions; see [Verification status](#verification-status).

These libraries are independently developed and are not Microsoft products.

## Migration and compatibility

Earlier `ShellInterop.Core` contained the JumpList API. The split moves its public types into the `ShellInterop.JumpList` assembly. The namespace `ShellInterop.Core.JumpLists` and existing method signatures remain unchanged. Add a reference to `ShellInterop.JumpList` (and Core when using `ProcessAppIdentity`), then rebuild. A Core-only reference no longer supplies JumpList types; binary compatibility with existing compiled consumers is not guaranteed. No type forwarders are provided. Existing users may continue using the old Core release or old local package.

The coordinated packages are set to `0.2.0-preview.1` in the project files; this is the planned number, not a release. Independently review package metadata, dependency versions, XML documentation, and license/third-party rights before publishing.

## Requirements

- .NET 10, a `net10.0-windows` (or later Windows) project.
- Your own AppUserModelID (AUMID) such as `Company.Product` (at most 128 characters, no spaces).

The library targets `net10.0-windows` only. A plain `net10.0` project cannot reference it (NU1201/NU1202). Cross-platform
applications multi-target `net10.0;net10.0-windows` and reference the library only from the Windows target.

`ShellInterop.Notifications` targets `net10.0-windows10.0.19041.0` (the Windows SDK .NET projection needs a versioned Windows TFM). A consuming project must target `net10.0-windows10.0.19041.0` or later: a plain `net10.0-windows` project gets NU1201 (observed in a scratch WPF check; an Avalonia consumer has not been built).

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

`Entries` is the authoritative ordered input; `Tasks` is a task-only view. Input is copied. Error `TaskIndex` refers to the zero-based entry position for this factory; existing task-only callers retain their old index. `RequestedTaskCount` and the local 100-task limit exclude separators. Empty entries retain the existing empty-Tasks behavior; separator-only input is passed to the Shell. The library does not normalize leading, trailing or adjacent separators: Windows ignores the first/last and suppresses one of adjacent separators. BeginList's minimum-slot count excludes separators and headers only while their total is at most four; extras can affect visible destinations. This library does not truncate to that count. Real Shell: separators inside the `TasksFallback` block displayed as expected with this library (SHELL-JL-07, Windows 11); a standalone test application that calls this library's public API (`JumpListPublisher.ApplyAsync` with `JumpListEntry.Separator()`) also confirmed separator display (SHELL-JL-02, Windows 11). Other separator layouts are not verified.

### Shutdown

Do not close the application while a Jump List transaction may be running. `await publisher.ShutdownAsync(timeout)`
first: it rejects new work, completes queued-but-unstarted requests as `PublisherDisposed`, waits for the in-flight COM
transaction to finish and release its objects, and for the STA worker to exit. `Completed == false` means it is **not**
done (the Shell state is unknown), so keep the host open. `Dispose()` is a best-effort synchronous stop that never aborts
the worker.

## Notifications

`ShellInterop.Notifications` sends a title + body toast and inspects whether an AUMID-bearing Start Menu shortcut exists. It never creates, edits, repairs or deletes shortcuts or registry values; your application provides the shortcut (carrying the same AUMID as `NotificationPublisherOptions.Aumid`) for its own deployment.

```csharp
using ShellInterop.Notifications;

var publisher = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "YourCompany.YourApp" });

// A. Static, read-only check of the Start Menu shortcut (ShortcutFound / ShortcutNotFound / Unknown).
NotificationRegistrationInspection registration = publisher.InspectRegistration(Environment.ProcessPath);

// B. Windows notification setting (Unknown means "could not read", not "disabled").
NotificationSettingState setting = publisher.GetSetting();

// C. Submit the toast. Never throws for OS failures; check the result.
NotificationSendResult sent = publisher.Send(new NotificationDefinition("Title", "Body"));
Console.WriteLine($"registration={registration.State} setting={sent.Setting} submitted={sent.Submitted} hr=0x{sent.HResult:X8}");
```

`NotificationDefinition.Silent` (default `false`) can be set with an object initializer, e.g. `new NotificationDefinition("Title", "Body") { Silent = true }`. When `true`, the toast XML carries `<audio silent="true"/>` to ask Windows for no notification sound; when `false` the XML is unchanged. For `Silent`, the generated toast XML is unit-tested; that does not guarantee actual silence on real Windows, which has not been verified.

### LaunchUri (protocol activation)

`NotificationDefinition.LaunchUri` (default `null`) is an optional absolute URI that Windows launches when the toast body is clicked: `new NotificationDefinition("Title", "Body") { LaunchUri = "my-app-x://activate?a=1" }`. When set, the toast XML gets `launch="<uri>"` and `activationType="protocol"` (the value is XML-escaped); when `null` the XML is unchanged. `Silent` and `LaunchUri` can be combined. Setting a value that is not a well-formed absolute URI, or that contains whitespace or control characters, throws `ArgumentException`. The library does not restrict URI schemes.

- **The library only emits the URI.** It does not register the protocol, create the Start Menu shortcut, set the AUMID, or handle the click. Your application owns the protocol registration (for example under HKCU) and the code that runs when Windows starts it.
- **Treat the received URI as untrusted input.** Any process can start a registered protocol with any argument; validate it in your application.
- If the application is not running when the toast is clicked, Windows starts the registered protocol handler, so the handler must be able to start the app (or hand off to the running instance).

One verified application-side arrangement (AI Usage Tracker, not part of this library and not a guarantee for other apps): the app itself creates its Start Menu shortcut with the AUMID and registers a URL protocol under HKCU; the toast carries `LaunchUri` with that protocol; a click makes Windows start the app's executable with the URI, and that short-lived second process hands over to the already running instance through a single-instance gate and exits, after which the running instance restores its main window (a closed app is simply started). All of this logic lives in the application.

Responsibilities, kept separate: the **Start Menu shortcut and AUMID** (app-provided; read-only inspection here), **protocol registration** (app-provided), **sending** (`NotificationPublisher.Send`, `Submitted` only means the Windows API call succeeded), **display by Windows** (not guaranteed; see below), and **launching or restoring the app on click** (app-side handler for the protocol URI).

Three independent facts, none of which guarantees that a toast is visible:

| Fact | Where | Meaning |
|---|---|---|
| Submitted | `NotificationSendResult.Submitted` | The Windows send API call succeeded |
| Setting | `NotificationSendResult.Setting` / `GetSetting()` | The Windows notification setting for the AUMID |
| Registration | `InspectRegistration()` | Static Start Menu shortcut evidence only |

Observed on Windows 11 (SHELL-NOTIF-02 / SHELL-NOTIF-04, dedicated VM):

- **Registration propagation.** Right after a shortcut was registered, a toast sent about one second later returned `Submitted=True` and the inspection reported `ShortcutFound`, yet it was not seen on screen or in the Notification Center when checked about 4.5 minutes later (a brief display cannot be ruled out). The same toast re-sent later was displayed and kept in the Notification Center. A likely cause is a delay before Windows picks up the new AUMID registration; the cause and the required wait time have not been verified.
- **After unregistering.** A toast sent right after the shortcut was removed was still displayed; after a guest restart it was not (with `Submitted=True` and `Setting=Enabled`). Immediate effect of unregistering is not guaranteed; whether a restart is required, or sign-out or time is enough, has not been verified.
- `InspectRegistration` runs on its own short-lived STA thread (60 s limit) and allows one scan per process at a time. If a scan hangs, later inspections return `Unknown` (busy) instead of starting more threads; sending is not affected. Real-Windows behavior on a hung scan has not been verified.

Not implemented: handling the click itself (the library only emits `LaunchUri`; protocol registration and launching or restoring the app are the application's job), COM activator registration, action buttons, notification history, replacing or removing toasts, automatic shortcut registration, automatic repair of the OS registration.

Distribution: the Windows SDK .NET projection adds `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` to the consuming application (about 25 MB in the PoC self-contained publish; the AI Usage Tracker release ZIP, framework-dependent, contains both files).

Four things that are easy to confuse:

| Name | Relation to this library |
|---|---|
| Windows App SDK (NuGet packages, `AppNotificationManager`, native bridge DLLs) | **Not used.** Earlier Tracker builds used it; it was removed. The 19-file Tracker release ZIP built in SHELL-NOTIF-10 contains no Windows App SDK notification component (file names, `deps.json` and binary strings checked) |
| Windows App Runtime (OS-level framework packages) | Not used by this library. In the verified app no Windows App SDK notification module was loaded, and the Windows App Runtime was present on the verification VM's OS. Whether results depend on its presence has **not** been verified: behavior on a machine without it, and independence on all Windows environments, are unconfirmed |
| Windows SDK .NET projection (`Microsoft.Windows.SDK.NET.dll`) | **Used.** Shipped with the consuming application |
| `WinRT.Runtime.dll` (C#/WinRT) | Used, as part of the same projection package. Not the Windows App SDK |

Licensing of these two DLLs (facts only, no legal conclusion): this repository ships only the MIT `LICENSE` and has no third-party notice file. The Tracker release ZIP ships `THIRD-PARTY-NOTICES.md` and the Microsoft Windows 10 SDK license text, and its notice cites Microsoft's Windows SDK redistribution list as naming both files; the source tree and the local nupkg files contain neither DLL (checked in SHELL-LEGAL-03 and SHELL-PACK-02/03), but an application that uses `ShellInterop.Notifications` ships them, so that application must meet Microsoft's redistribution conditions itself. Whether those conditions are met for the Tracker ZIP has not been confirmed. See [docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md).

## Project references and future packages

For development, reference `src/ShellInterop.JumpList/ShellInterop.JumpList.csproj`. It transitively references Core; applications directly using Core APIs may reference Core explicitly. Future NuGet package IDs are `ShellInterop.Core` and `ShellInterop.JumpList`. Core and JumpList are versioned `0.2.0-preview.1` (JumpList's NuGet dependency is `ShellInterop.Core >= 0.2.0-preview.1`). `ShellInterop.Notifications` is an independent package at `0.1.0-preview.1` with its own package README and the LICENSE packed. All three have been packed locally in isolation (SHELL-PACK-01/02), and a minimal consumer restored and built them from a local feed; none is published on NuGet.org. A local pack success is neither a publication approval nor a legal clearance, and publishing a package is separate from distributing an application that uses it (see the licensing notes above).

## Verification status

| Evidence | Result |
|---|---|
| Unit tests (fake Shell, in-memory COM objects, SDK-header cross-checks) | Included in `tests/ShellInterop.Core.Tests` (158 passed in SHELL-JL-06-VERIFY; that count predates the Notifications tests added to the same project and has not been re-measured for this documentation). They never touch a Jump List. A pass is not a real-Shell result |
| Real Shell, split assemblies without separators | SHELL-SPLIT-02: apply, navigation, RunItem, delete and re-register passed in the dedicated VM |
| Real Shell, single custom category | SHELL-JL-05: confirmed |
| Real Shell, multiple custom categories, removed-destination tracking, `TasksFallback` on `E_ACCESSDENIED`, separators inside `TasksFallback` | SHELL-JL-07 (Windows 11 VM): conditional PASS. A real `E_ACCESSDENIED` (privacy setting off) occurred on the first category; fallback, category skip and recovery were confirmed |
| Real Shell, separator display | SHELL-JL-02: confirmed in a standalone test application that uses this library's public API (not a raw-COM implementation) |
| Real Shell, further checks (Windows 11 Home 10.0.26300 VM) | SHELL-JL-08: conditional PASS. Removed destinations did not reappear after Pending, a later Apply, privacy off/on, or an Explorer restart. A real `E_ACCESSDENIED` on a later category (induced by switching the privacy setting mid-transaction) led to `AbortList`, a new `BeginList` and a Tasks-only commit. Taskbar pinning and an Explorer restart did not change the result in the cases tried |
| Notifications, real Windows 11 (SHELL-NOTIF-04, dedicated AUMID, VM) | Registration inspection (before / after / after removal), toast display and Notification Center retention passed; see [Notifications](#notifications) for the registration-propagation caveat |
| Notifications with `Silent` and `LaunchUri`, real Windows 11 VM (SHELL-NOTIF-07-D-VERIFY): CONDITIONAL_PASS | Used by the AI Usage Tracker (framework-dependent build, .NET 10 Desktop Runtime, HKCU protocol registration, per-app Start Menu shortcut with AUMID). Confirmed: toast display; retention in the Notification Center; click restore while minimized; click restore from behind another window (2 times) and from the tray (hidden window, 1 time only); click start after the app had exited (2 times); no Windows App SDK module loaded; notifications turned off then on in Windows settings and recovered. Not verified or only partly verified: audible silence of `Silent`, some failure paths (shortcut or protocol conflicts, real send failures), machines without the Windows App Runtime, other Windows builds. This is one application on one VM, not a guarantee for other applications. For `Silent` and `LaunchUri`, unit tests check the generated toast XML; they do not guarantee silent behavior or click handling on Windows (click restore was checked separately by the minimal smoke on a real Windows 11 VM, SHELL-NOTIF-10-VM-SMOKE). That build included an uncommitted `ShellInterop` change, so it is not a verification of the committed release |
| Notifications, clean release rebuild (SHELL-NOTIF-10): PASS | Static/build result, not a real-Shell result. The Tracker release ZIP (19 files, Release / win-x64 / framework-dependent, .NET SDK 10.0.402) was rebuilt from committed sources only (ShellInterop 1aa7e48, Tracker a55f539) with no Windows App SDK notification dependency (file names, `deps.json`, binary strings). Byte-identical reproduction was not required and not achieved |
| Notifications, new release ZIP on a real Windows 11 VM (SHELL-NOTIF-10-VM-SMOKE): PASS (minimal smoke) | Windows 11 Home build 26300, one user, one VM, one run. The ZIP from the row above started from a new folder; a toast banner was displayed and stayed in the Notification Center; a minimized window was restored by clicking the toast, through a short-lived second process (about 0.65 s) that started from the new folder and was followed by the running instance coming to the front; no second instance remained; no Windows App SDK module was loaded (only `Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll` and `ShellInterop.Notifications.dll` besides the runtime). The hand-over was inferred from process and window-state logs, not by observing the IPC. Only this minimal set was repeated; the other 07-D-VERIFY items were not re-run. Not verified: audible silence, failure injection, other Windows builds, machines without the Windows App Runtime, any other application or environment |

**Not verified against the real Shell:** clearing with an empty Tasks list, persistence after an OS restart, `E_ACCESSDENIED` occurring naturally only on a later category (JL-08 induced it), pinned-item combinations with `TasksFallback` or deletion, the cause of the 11-item display seen when a removed destination was present (the 11-item observation itself is confirmed, see below; reproduction on other machines, a screen-resolution dependence and any relation to `JumpListItems_Maximum` are untested or unknown), other Windows builds, and how the display-count setting below behaves elsewhere. Delete, replacement, and task launching passed SHELL-SPLIT-02 for the launcher scenario.

### Displayed destination count

Registration and display are separate. A successful `ApplyAsync` does not guarantee that every requested category was registered (for example, `TasksFallback` or a skipped category after `E_ACCESSDENIED`), and registered destinations are not guaranteed to be displayed. In the SHELL-JL-08 display-count tests, every `AppendCategory` call and `CommitList` succeeded, yet the Shell displayed fewer destinations than were submitted: with default settings the Shell showed 12 destinations for 5 x 6, 5 x 3, 3 x 6 and 5 x 5 category layouts (the 5 x 5 list was cut inside the third category) and 11 categories for single-destination categories. Put important destinations in the first categories. These are observations on one Windows build and default settings, not a documented fixed limit. Related observations (SHELL-JL-07 / SHELL-JL-08): 12 or 13 single-destination categories showed 11 categories, and a 5 x 6 list with one removed destination showed 11 destinations; why (screen height, removed-destination handling or something else) is not identified, and a screen-resolution dependence was proposed but not tested. The 11-destination display itself was observed, but it is not guaranteed to reproduce on other machines, and how it relates to `JumpListItems_Maximum` is unknown. A raw `BeginList` on that VM reported `pcMinSlots = 10`, which is a minimum slot count, not the display limit.

The Shell reads the `JumpListItems_Maximum` DWORD value under `Explorer\Advanced` (HKCU and HKLM) when it displays the list. A change took effect without re-applying the list, restarting Explorer or restarting the application. In this VM HKCU took precedence over HKLM (both directions were tried: HKLM=20 with HKCU=5 showed 4 destinations, HKLM=5 with HKCU=20 showed 18), HKLM alone also worked, so an HKLM value can stay effective when the HKCU value is absent. Removing both the HKCU and HKLM values restored the default display behavior in the tested VM. Example for 5 x 6: value 20 showed 18 destinations, 5 showed 4, unset showed 12. The relation between the value and the displayed count is not understood, so a value is not a guaranteed count. The Process Monitor observation that `RuntimeBroker.exe` queries the value each time the list opens was made by the maintainer and no capture was kept. This library neither reads nor writes the value and does not truncate input.

## Scope

Implemented: Jump List **Tasks** management, Windows Shell COM interop, AppUserModelID helpers, the STA worker boundary,
COM lifetime handling, apply/delete results and error reporting.

Also implemented in `ShellInterop.JumpList`: separators, one or more custom categories with removed-destination tracking (Tasks fallback on `E_ACCESSDENIED`). Real Shell verification: SHELL-JL-05 (single category) and SHELL-JL-07 (multiple categories; conditional PASS) and SHELL-JL-08 (additional checks, conditional PASS; see [Verification status](#verification-status)).

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
| [README.ja.md](README.ja.md) | Japanese counterpart of this README |
| [docs/ja/JUMPLIST.md](docs/ja/JUMPLIST.md) | JumpList: usage, categories, display limits (Japanese) |
| [docs/ja/NOTIFICATIONS.md](docs/ja/NOTIFICATIONS.md) | Notifications: usage, responsibilities, limits (Japanese) |
| [docs/DOCS-MAP.md](docs/DOCS-MAP.md) | Which English and Japanese documents correspond, and what to update together |
| [docs/JUMPLIST-PACKAGE-README.md](docs/JUMPLIST-PACKAGE-README.md) / [docs/PACKAGE-README.md](docs/PACKAGE-README.md) | Package READMEs for `ShellInterop.JumpList` / `ShellInterop.Core` (English) |
| [docs/USAGE-AVALONIA.md](docs/USAGE-AVALONIA.md) | Usage guide with an Avalonia sample (Japanese) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Structure of the library (Japanese) |
| [docs/ERROR-MODEL.md](docs/ERROR-MODEL.md) | Result and error model (Japanese) |
| [docs/API-CONTRACT-REVIEW.md](docs/API-CONTRACT-REVIEW.md) | How the COM declarations were checked against the Windows SDK headers (Japanese) |
| [docs/COM-PROVENANCE.md](docs/COM-PROVENANCE.md) | Where the COM declarations come from, and what is still open (English) |
| [docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md) | Checklist for GitHub, NuGet and application ZIP publication (English) |

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
review of code produced with AI assistance. The sources of the COM declarations are recorded in
[docs/COM-PROVENANCE.md](docs/COM-PROVENANCE.md); recording them does not settle the rights question.

Publication readiness (GitHub source, NuGet, application ZIP) is tracked in
[docs/PUBLICATION-CHECKLIST.md](docs/PUBLICATION-CHECKLIST.md). An inquiry to Microsoft is not a blanket precondition for
publishing; it is optional when more certainty is wanted. Microsoft's license terms still apply wherever Microsoft files
are redistributed, and none of this is legal advice or a clearance.

## Future design notes (not implemented)

- Custom categories may have a separately editable display name, initially copied from the group name, with an explicit copy-from-group action; renaming the group does not overwrite it.

Packing the JumpList project with the old `0.1.0-preview.1` version remains blocked by a guard (the old Core lacks the split internals); the current `0.2.0-preview.1` of both projects packs. For local tests of other versions, pass the same `-p:Version=...` to both projects and do not publish it.

`JumpListEntry` is an API input model, not a persistence format; persist application data separately and reconstruct entries with the factories.
