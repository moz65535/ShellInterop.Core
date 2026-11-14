# COM declaration provenance

This document records where the hand-written COM declarations in this repository come from. It is a transparency note. It is **not** a license grant, a legal opinion, or a statement that the rights questions below are resolved.

## Scope

COM interfaces are re-declared in C# in two places:

- `src/ShellInterop.JumpList/Interop/ShellComInterfaces.cs`
- `src/ShellInterop.Notifications/StartMenuShortcutScanner.cs` (a separate, smaller set of declarations; the two files do not share code)

No Windows SDK header, IDL, or type library is included in this repository, and no header text is copied into the source (see "Header text in this repository").

## Source material

- Windows SDK 10.0.26100.0 headers: `ShObjIdl_core.h`, `ObjectArray.h`, `propsys.h`, `propkey.h`, `objidl.h`.
- Microsoft Learn documentation for the Shell interfaces involved (used for behavior contracts such as call order; see `API-CONTRACT-REVIEW.md`, written in Japanese).

The SDK version is the one named in the source comments and in `API-CONTRACT-REVIEW.md`. During SHELL-DOC-03 the identifiers in the table below were looked up again by text search in a local installation of SDK 10.0.26100.0 (static, read-only). Vtable order is covered by the repository tests described in `API-CONTRACT-REVIEW.md`; it was not re-measured in SHELL-DOC-03.

## What comes from the SDK

| Declaration | Identifier (as used here) | SDK header |
|---|---|---|
| `ICustomDestinationList` | IID `6332debf-87b5-4670-90c0-5e57b408a49e` | `ShObjIdl_core.h` |
| CLSID `DestinationList` | `77f10cf0-3db5-4966-b520-b7c54fd35ed6` | `ShObjIdl_core.h` |
| CLSID `EnumerableObjectCollection` | `2d3468c1-36a7-43b6-ac24-d3f02fd9607a` | `ShObjIdl_core.h` |
| `IObjectArray` | IID `92CA9DCD-5622-4bba-A805-5E9F541BD8C9` | `ObjectArray.h` |
| `IObjectCollection` | IID `5632b1a4-e38a-400a-928a-d4cd63230295` | `ObjectArray.h` |
| `IShellLinkW` | IID `000214F9-0000-0000-C000-000000000046` | `ShObjIdl_core.h` |
| CLSID `ShellLink` | `00021401-0000-0000-C000-000000000046` | `ShObjIdl_core.h` |
| `IShellItem` (QueryInterface probe only; no managed declaration) | IID `43826D1E-E718-42EE-BC55-A1E261C37BFE` | `ShObjIdl_core.h` |
| `IPropertyStore` | IID `886d8eeb-8cf2-4446-8d02-cdba1dbdcf99` | `propsys.h` |
| `IPersistFile` (declared as `IPersistFileNative`, Notifications only) | IID `0000010B-0000-0000-C000-000000000046` | `objidl.h` |
| `PKEY_Title` (`System.Title`) | `{F29F85E0-4FF9-1068-AB91-08002B27B3D9}`, 2 | `propkey.h` |
| `PKEY_AppUserModel_ID` (Notifications only) | `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}`, 5 | `propkey.h` |
| `PKEY_AppUserModel_IsDestListSeparator` | `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}`, 6 | `propkey.h` |

## Why the declarations look like the SDK

These parts must match the SDK for the code to work with Windows. They are fixed by compatibility, not by stylistic choice:

- IIDs, CLSIDs and PROPERTYKEY values.
- Method order (vtable layout). A different order calls the wrong method.
- Method names. These do not affect the binary contract, but they are kept so the declarations stay recognizable and checkable against the headers.

Some parameter names (for example `pszFile`, `cch`, `pfd`, `fFlags`) are the same as in the SDK declarations. Parameter names could be changed without affecting behavior; they were kept for traceability.

`IObjectCollection` redeclares the two inherited `IObjectArray` slots because classic `[ComImport]` interop does not flatten base-interface methods into the derived vtable. This is an interop requirement, not something copied from the header.

## What is original to this repository

C# syntax and attributes (`[ComImport]`, `[PreserveSig]`, `[MarshalAs]`, type mapping), the minimal `PropVariant`/`PropertyKey` structs, the STA worker, COM lifetime handling, result and error models, and all call logic. AI-assisted development is disclosed in the README.

## Header text in this repository

- No header body, doc comment, or copyright notice is copied.
- `ShellComInterfaces.cs` previously carried one comment line naming a `propkey.h` macro for `PKEY_Title`. That line was a paraphrase, not a verbatim header line (the header writes the GUID as hex byte literals). In SHELL-DOC-03 it was replaced by a comment stating only that the format ID and property ID equal the header's. No identifier or code changed.
- Other comments name header files only to say what the declaration was checked against.

## What has been confirmed

- Which header each identifier above comes from, and that the values match the headers of SDK 10.0.26100.0.
- The repository contains no Microsoft DLLs, headers, IDL files, or `.winmd` files, and no third-party source files were found.
- The implementation is original to this repository.

## What is not settled

- Whether GUIDs, PROPERTYKEYs, vtable order, method names and parameter names carry rights that call for a license or permission, and if so what the terms are. No official statement covering the Windows SDK headers for this use was found. The MIT license of the win32metadata project is not evidence about the SDK headers.
- Adding this note does not by itself create any permission.
- The MIT license of this repository covers only code the maintainer can license. It does not grant rights held by third parties.

Asking Microsoft is optional and is not a precondition for any step in `PUBLICATION-CHECKLIST.md`. It is one way to gain more certainty if you want it.
