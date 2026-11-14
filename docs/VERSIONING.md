# Versioning policy

ShellInterop uses **independent, per-package versioning**. Each NuGet package carries its own version, and the version numbers are not required to match.

## Current planned first-publication versions

| Package | Version |
|---|---|
| `ShellInterop.Core` | `0.2.0-preview.1` |
| `ShellInterop.JumpList` | `0.2.0-preview.1` |
| `ShellInterop.Notifications` | `0.1.0-preview.1` |

Core and JumpList share a version only because they were updated together in this round of development. That is a coincidence of coordinated work, not a permanent same-version constraint. `ShellInterop.Notifications` is an independent package and is deliberately **not** raised to `0.2.0` to make the numbers look alike.

These are planned versions. Nothing is published on NuGet.org until the maintainer explicitly decides to publish.

## Rules

1. Each package has its own independent version.
2. A package with no changes keeps its version; do not bump it just because another package changed.
3. A new package normally starts at `0.1.0-preview.1`.
4. The version of a dependency that a package needs is expressed through its NuGet dependency (for example, JumpList depends on `ShellInterop.Core >= 0.2.0-preview.1`), not by matching version numbers.
5. When a change affects compatibility, update the affected dependent packages and consumers together, in a coordinated way.
6. Use the ideas of Semantic Versioning: a new feature, a fix and a breaking change are each reflected in the version according to its impact.
7. While packages are in preview, do not over-promise compatibility. Preview versions may contain breaking changes.
8. Never overwrite an already published package ID and version. Fix problems by publishing a new version.
9. Each package can be published independently of the others.
10. If a specific combination of package versions was verified together, record it in a release note when that is useful.

## Future packages

When a package such as `ShellInterop.Tray` is added, it does not need to be mechanically aligned with the versions of existing packages. It follows rule 3 and then evolves independently.

## Equal version numbers are not a compatibility guarantee

Having the same version number does not guarantee that packages work together, and having different version numbers does not imply that they do not. Compatibility is expressed by NuGet dependency ranges and by the verified combinations recorded in release notes.
