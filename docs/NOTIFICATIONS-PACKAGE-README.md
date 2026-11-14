# ShellInterop.Notifications

Windows-only .NET library that sends a title + body toast through the Windows SDK .NET projection (no Windows App SDK) and performs a read-only check for an AUMID-bearing Start Menu shortcut. Pre-release. Unofficial, independently developed library; not a Microsoft product.

It never creates, edits, repairs or deletes shortcuts or registry values. Your application provides the Start Menu shortcut (carrying the same AUMID as `NotificationPublisherOptions.Aumid`) as part of its own deployment.

## Requirements

- Target framework: `net10.0-windows10.0.19041.0` (Windows 10 version 2004 or later API surface).
- Windows only. It does not reference `ShellInterop.Core` or `ShellInterop.JumpList`.
- No Windows App SDK package reference.

## Install

```
dotnet add package ShellInterop.Notifications --prerelease
```

(This package is a pre-release, so `--prerelease` is required.)

## Minimal example

```csharp
using ShellInterop.Notifications;

var publisher = new NotificationPublisher(new NotificationPublisherOptions { Aumid = "YourCompany.YourApp" });

// Static, read-only check of the Start Menu shortcut (ShortcutFound / ShortcutNotFound / Unknown).
NotificationRegistrationInspection registration = publisher.InspectRegistration(Environment.ProcessPath);

// Windows notification setting (Unknown means "could not read", not "disabled").
NotificationSettingState setting = publisher.GetSetting();

// Submit the toast. Does not throw for OS failures; check the result.
NotificationSendResult sent = publisher.Send(new NotificationDefinition("Title", "Body"));
Console.WriteLine($"registration={registration.State} setting={sent.Setting} submitted={sent.Submitted} hr=0x{sent.HResult:X8}");
```

## Main API

| Type | Purpose |
|---|---|
| `NotificationPublisher` | `Send`, `GetSetting`, `InspectRegistration` |
| `NotificationPublisherOptions` | `Aumid` in `Company.Product[.Sub...]` form (two or more non-empty segments, ASCII letters/digits/`-`/`_`, at most 128 characters). Invalid values throw `ArgumentException`. The library does not register the ID |
| `NotificationDefinition` | Title, body, `Silent`, `LaunchUri`. Text is validated at construction (empty strings and characters invalid in XML 1.0 are rejected, not replaced) |
| `NotificationSendResult`, `NotificationSettingState`, `NotificationRegistrationInspection`, `NotificationRegistrationState` | Results |

`Send` and `GetSetting` can be called from any thread. `InspectRegistration` runs on its own short-lived STA thread (60 s limit); on timeout the result is `Unknown`, and only one scan runs per process at a time.

## Silent

`new NotificationDefinition("Title", "Body") { Silent = true }` adds `<audio silent="true"/>` to the toast XML to ask Windows for no sound. The generated XML is unit-tested; actual silence on real Windows has **not** been verified by listening.

## LaunchUri

`NotificationDefinition.LaunchUri` (default `null`) is an absolute URI Windows launches when the toast body is clicked, for example `{ LaunchUri = "my-app-x://activate?a=1" }`. When set, the toast XML gets `launch="<uri>"` and `activationType="protocol"` (XML-escaped). It can be combined with `Silent`. A value that is not a well-formed absolute URI, or contains whitespace or control characters, throws `ArgumentException`. URI schemes are not restricted.

**The library only emits the URI.** The application owns the protocol registration (for example under HKCU), the Start Menu shortcut with the AUMID, and the code that runs when Windows starts the protocol handler. Treat the received URI as untrusted input and validate it; any process can start a registered protocol with any argument.

## Responsibilities

| Item | Owner |
|---|---|
| Start Menu shortcut and AUMID | Application (read-only inspection here) |
| Protocol registration and click handling | Application |
| Sending (`Send`) | Library |
| Display by Windows | Not guaranteed |

## Submitted is not "displayed"

Three independent facts, none of which guarantees that a toast is visible:

| Fact | Where | Meaning |
|---|---|---|
| Submitted | `NotificationSendResult.Submitted` | The Windows send API call succeeded |
| Setting | `NotificationSendResult.Setting` / `GetSetting()` | The Windows notification setting for the AUMID |
| Registration | `InspectRegistration()` | Static Start Menu shortcut evidence only |

## Known limitations

- Right after a shortcut was registered, a toast returned `Submitted=True` and the inspection reported `ShortcutFound`, yet it was not seen about 4.5 minutes later; a re-send later was displayed. The cause and required wait time are unverified.
- A toast sent right after the shortcut was removed was still displayed; after a guest restart it was not. Immediate effects of unregistering are not guaranteed.
- Long or multi-line text may be truncated or collapsed by Windows; the library sets no length limit.
- Not implemented: handling the click itself, COM activator registration, action buttons, notification history, replacing or removing toasts, automatic shortcut registration, automatic repair of the OS registration.

## Verified scope

Observed on Windows 11 virtual machines, one application, one user. Not a guarantee for other applications or environments.

- Registration inspection, toast display and Notification Center retention (dedicated AUMID, VM).
- `Silent` and `LaunchUri` used by an application (AI Usage Tracker): display, retention, click restore, click start after exit, no Windows App SDK module loaded. That build included uncommitted changes, so it is not a verification of a committed release.
- A clean rebuild of that application's release ZIP (static/build result) and a minimal real-VM smoke run: toast display and click restore.

## Not verified

Audible silence of `Silent`; failure injection (shortcut or protocol conflicts, real send failures); other Windows builds; machines without the Windows App Runtime; any other application or environment. Unit tests check the generated toast XML only and do not prove behavior on a real Shell.

## Not the Windows App SDK; what your app ships

This library does not use the Windows App SDK (`AppNotificationManager`, native bridge DLLs). It uses the Windows SDK .NET projection, so an application using it ships `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` (C#/WinRT) with its own publish output. Whether this NuGet package itself contains these files is a separate question from what the consuming application's publish output contains. The redistribution terms for these Microsoft components, the publication terms of this package, and third-party notices are **not confirmed**; this README makes no legal statement.

## License

MIT License, Copyright (c) 2026 moz. See the `LICENSE` file in the package.

Adopting MIT does not grant rights held by third parties. Two questions remain open and are not resolved by this license: the permission status of COM declarations derived from Windows SDK headers (relevant to the sibling JumpList module), and rights review of code produced with AI assistance. ShellInterop was developed and is maintained by moz with substantial assistance from OpenAI ChatGPT, OpenAI Codex and Anthropic Claude Code; final decisions and responsibility remain with the maintainer.

## More documentation

Full documentation, including Japanese guides, is in the source repository: <https://github.com/moz65535/ShellInterop.Core> (the repository README's "Notifications" and "Verification status" sections, and `docs/ja/NOTIFICATIONS.md`).