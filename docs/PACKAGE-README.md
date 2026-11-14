# ShellInterop.Core

Shared Windows Shell foundation: `ProcessAppIdentity` provides the process AppUserModelID helpers. The Jump List publisher now lives in the separate `ShellInterop.JumpList` module, which depends on Core. Both target `net10.0-windows`.

This is a pre-release. Version `0.2.0-preview.1` is a breaking split of the earlier Core code and is released together with `ShellInterop.JumpList` `0.2.0-preview.1`. Earlier internal Core builds numbered `0.1.0-preview.1` included JumpList types and were never published on NuGet.org. New JumpList consumers must reference `ShellInterop.JumpList` (which depends on `ShellInterop.Core >= 0.2.0-preview.1`) and rebuild. The existing namespace remains `ShellInterop.Core.JumpLists`; binary compatibility is not guaranteed.

Publishing a NuGet package is not a legal clearance, and is separate from distributing an application that uses it: the application's own publish output and its redistribution conditions (including any Microsoft components) must be reviewed separately.

Tasks are supported by the new module. Separators are supported; custom categories (single and multiple) with removed-destination tracking are supported in `ShellInterop.JumpList`; Recent/Frequent are not implemented. Real Shell: the split passed regression without separators (SHELL-SPLIT-02); separators inside the `TasksFallback` block were confirmed with this library (SHELL-JL-07, conditional PASS), and a standalone test application using this library's public API confirmed separator display (SHELL-JL-02). Other layouts are unverified; see the repository README, including its notes on how many destinations the Shell displays.

## Development and AI Assistance

ShellInterop.Core was developed and is maintained by moz, with substantial assistance from OpenAI ChatGPT, OpenAI Codex,
and Anthropic Claude Code.

AI assistance included architecture and API design discussions, implementation planning, code generation, debugging,
testing, and code review.

Final project decisions and responsibility remain with the maintainer.

## License

MIT License. Copyright (c) 2026 moz. See the `LICENSE` file in the package.

Adopting MIT does not grant rights held by third parties. Two questions are still open and are not resolved by this
license: the permission status of COM declarations (GUIDs, vtable layouts) derived from Windows SDK headers, and rights
review of code produced with AI assistance.

Packing the JumpList project with the old `0.1.0-preview.1` version remains blocked by a guard (the old Core lacks the split internals); the current `0.2.0-preview.1` of both projects packs. For local tests of other versions, pass the same `-p:Version=...` to both projects and do not publish it.
