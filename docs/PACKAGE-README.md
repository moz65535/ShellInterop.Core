# ShellInterop.Core

Shared Windows Shell foundation: `ProcessAppIdentity` provides the process AppUserModelID helpers. The Jump List publisher now lives in the separate `ShellInterop.JumpList` module, which depends on Core. Both target `net10.0-windows`.

This source tree is an unpublished breaking split. Earlier Core packages included JumpList types; existing users can continue using the old package. New JumpList consumers must reference `ShellInterop.JumpList` and rebuild. The existing namespace remains `ShellInterop.Core.JumpLists`; binary compatibility is not guaranteed. Do not replace 0.1.0-preview.1 with this build. Review a coordinated new preview version and package dependencies before publishing.

Tasks are supported by the new module. Separators are supported; custom categories (single and multiple) with removed-destination tracking are supported in `ShellInterop.JumpList`; Recent/Frequent are not implemented. The split without separators passed real Shell regression; separator display remains unverified.

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

Packing the JumpList project with the unchanged 0.1.0-preview.1 version is blocked: the old Core package lacks required split internals. For local packaging tests only, pass a coordinated version to both projects (for example `-p:Version=0.2.0-local.1`); do not publish this test version.
