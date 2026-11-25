# Versioning policy

ShellInterop uses **independent, per-package versioning**. Each NuGet package carries its own version, and the version numbers are not required to match.

## First-publication versions (published on NuGet.org on 2026-11-15)

| Package | Version |
|---|---|
| `ShellInterop.Core` | `0.2.0-preview.1` |
| `ShellInterop.JumpList` | `0.2.0-preview.1` |
| `ShellInterop.Notifications` | `0.1.0-preview.1` |

Core and JumpList share a version only because they were updated together in this round of development. That is a coincidence of coordinated work, not a permanent same-version constraint. `ShellInterop.Notifications` is an independent package and is deliberately **not** raised to `0.2.0` to make the numbers look alike.

These three versions are published on NuGet.org as preview releases. A published package ID and version is never overwritten (rule 8).

## Rules

1. Each package has its own independent version.
2. A package with no changes keeps its version; do not bump it just because another package changed.
3. A new package normally starts at `0.1.0-preview.1`.
4. The version of a dependency that a package needs is expressed through its NuGet dependency (for example, JumpList `0.2.0-preview.1` depends on `ShellInterop.Core >= 0.2.0-preview.1`, and the prepared JumpList `0.2.0-preview.2` on `ShellInterop.Core >= 0.2.0-preview.2`; these are minimum-version ranges, not exact pins), not by matching version numbers.
5. When a change affects compatibility, update the affected dependent packages and consumers together, in a coordinated way.
6. Use the ideas of Semantic Versioning: a new feature, a fix and a breaking change are each reflected in the version according to its impact.
7. While packages are in preview, do not over-promise compatibility. Preview versions may contain breaking changes.
8. Never overwrite an already published package ID and version. Fix problems by publishing a new version.
9. Each package can be published independently of the others.
10. If a specific combination of package versions was verified together, record it in a release note when that is useful.

## Planned next versions (not yet published)

| Package | Current published | Next planned | Reason |
|---|---|---|---|
| `ShellInterop.Core` | `0.2.0-preview.1` | `0.2.0-preview.2` | PDB/CodeView local absolute path removed (no API or feature change) |
| `ShellInterop.JumpList` | `0.2.0-preview.1` | `0.2.0-preview.2` | New features, enum/compatibility fixes, PDB path fix |
| `ShellInterop.Notifications` | `0.1.0-preview.1` | unchanged | Same PDB path issue exists; to be handled in a later, independent release |

Published versions are never re-packed or replaced. Packages built for local verification (for example by `scripts/Pack-Release.ps1`) are distinct from the packages chosen for publication; a verification pack of an unchanged package such as Notifications is never a replacement candidate. Use `-Package` to pack only what is being released; the PDB gate always runs for the selected packages.

## Git tags and GitHub Releases

Each published package version gets its own annotated Git tag, named `<package-slug>/v<PackageVersion>`:

- `core/v0.2.0-preview.1`
- `jumplist/v0.2.0-preview.1`
- `notifications/v0.1.0-preview.1`

A GitHub Release is created per updated package, pointing at that package's tag. Preview versions are marked as Prerelease. NuGet.org is the distribution channel for packages, so `.nupkg` files are not attached to GitHub Releases. Packages that did not change get no new tag or Release.

## Future packages

When a package such as `ShellInterop.Tray` is added, it does not need to be mechanically aligned with the versions of existing packages. It follows rule 3 and then evolves independently.

## Equal version numbers are not a compatibility guarantee

Having the same version number does not guarantee that packages work together, and having different version numbers does not imply that they do not. Compatibility is expressed by NuGet dependency ranges and by the verified combinations recorded in release notes.
