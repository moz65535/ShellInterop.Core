# Publication checklist

A working checklist for three separate publication actions. Nothing here is a legal clearance, and completing a list does not settle the open rights questions. Nothing has been published yet.

Three questions are kept apart on purpose:

- **A.** Is a separate inquiry to Microsoft required? Not as a blanket precondition. It is an optional step when evidence is unclear or more certainty is wanted.
- **B.** Do Microsoft's license terms apply? Yes, wherever Microsoft files are redistributed. Microsoft's Windows SDK redistribution list (<https://learn.microsoft.com/en-us/legal/windows-sdk/redist>) names `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` and allows redistribution of unmodified files under conditions.
- **C.** Are those conditions actually met? That is decided per action below. For the Tracker ZIP it is still open.

## A. GitHub source publication

**Confirmed technical facts**
- The tracked tree contains no Microsoft DLLs, nupkg, ZIP, exe, SDK headers, IDL or winmd files.
- `LICENSE` is MIT, "Copyright (c) 2026 moz". The names moz and moz65535 are the same developer (maintainer statement).
- COM declaration sources are recorded in [COM-PROVENANCE.md](COM-PROVENANCE.md).
- AI-assisted development is disclosed in the README.

**Not done**
- Push to a public remote, repository visibility change, release or tag.

**Final checks before publishing**
- `git ls-files` still shows no `dll`, `nupkg`, `zip`, `exe`, `h`, `idl`, `winmd`.
- Do not attach Microsoft DLLs, nupkg or ZIP files to GitHub Releases.
- README and README.ja.md agree with COM-PROVENANCE.md.
- No machine-specific paths or secrets in tracked files.

**Remaining license notes**
- The rights status of the COM declarations (identifiers, vtable order, names) is not settled; see COM-PROVENANCE.md.
- Rights review of AI-assisted code is not settled.
- The DLL redistribution conditions are not triggered by source publication, since no Microsoft DLL is included. That is a reading of the redistribution list, not a confirmed legal conclusion.

## B. NuGet package publication

**Confirmed technical facts**
- `ShellInterop.Core` and `ShellInterop.JumpList` pack as `0.2.0-preview.1`; `ShellInterop.Notifications` packs as `0.1.0-preview.1` (SHELL-PACK-01 to 03, local isolated pack).
- The packages do not contain `Microsoft.Windows.SDK.NET.dll` or `WinRT.Runtime.dll` and have no NuGet dependency on them. A consuming application's publish adds both files through the .NET SDK (`Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 in the verified consumer).
- A minimal consumer restored and built the packages from a local feed.
- No `ShellInterop.*` package ID was found on NuGet.org as of SHELL-PACK-02.

**Not done**
- `dotnet nuget push`, or any upload to NuGet.org.

**Final checks before publishing**
- Re-check the package IDs and versions on NuGet.org immediately before pushing; do not overwrite or reuse an existing version.
- Re-pack from the committed HEAD that will be published and inspect the nupkg contents (no Microsoft DLLs, LICENSE and README present).
- Check package metadata (authors, license expression, repository URL, README).
- Decide whether `0.1.0-preview.1` of Core should stay unpublished or untouched.

**Remaining license notes**
- COM declaration and AI-code questions are the same as in section A.
- A successful pack is a technical result only.
- The DLL redistribution conditions fall on the application that ships those DLLs (section C), not on these packages. Package notes should keep telling consumers this.

## C. Tracker ZIP distribution

The AI Usage Tracker release ZIP is the one place where Microsoft DLLs are actually redistributed.

**Confirmed technical facts**
- The 19-file ZIP contains `Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`, `ShellInterop.Notifications.dll` and the app's own files, and no Windows App SDK component.
- The two Microsoft DLLs are byte-identical to those in `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 and are named in Microsoft's redistribution list.
- The ZIP ships `THIRD-PARTY-NOTICES.md` and the Microsoft Windows 10 SDK license text.

**Not done**
- Public distribution of the ZIP.

**Final checks before distributing**
- Confirm the redistribution conditions in the license version that applies to the SDK version in use (which version applies is not yet decided).
- Decide how recipients agree to the terms that must flow downstream (consent method), and whether the indemnification condition is satisfied.
- Check where the required copyright notices appear.
- Decide whether a separate `WinRT.Runtime` MIT notice is needed.
- Make sure the old 43-file ZIP is not distributed; re-check the SDK license text hash in the ZIP.
- Alternatively, change the design so the DLLs are not bundled.

**Remaining license notes**
- All of the points above are open. ShellInterop's MIT license does not resolve them.
