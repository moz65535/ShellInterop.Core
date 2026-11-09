# ShellInterop.JumpList

Windows-only Jump List Tasks publisher targeting `net10.0-windows`; depends on `ShellInterop.Core`. This split is unpublished and must not replace the old Core 0.1.0-preview.1 package. Add a reference to this package and rebuild when migrating from the old Core package. The namespace remains `ShellInterop.Core.JumpLists`, but binary compatibility is not guaranteed; the old package can still be used.

Supports Tasks, one or more custom categories (`JumpListDefinition.FromEntriesWithCategories`, `Categories`; `Category` is the single-category view) with removed-destination protection (Fake-tested; real Shell: SHELL-JL-05 single category, SHELL-JL-07 multiple categories, conditional PASS), apply/delete results and safe publisher shutdown. Separators are supported; Recent/Frequent are not implemented. Split-assembly real Shell regression passed without separators; separator display and coordinated package version/dependency review remain outstanding. MIT license; see LICENSE and the repository's rights-review notes.

Packing the JumpList project with the unchanged 0.1.0-preview.1 version is blocked: the old Core package lacks required split internals. For local packaging tests only, pass a coordinated version to both projects (for example `-p:Version=0.2.0-local.1`); do not publish this test version.

Use `JumpListDefinition.FromEntries(appId, [JumpListEntry.ForTask(taskA), JumpListEntry.Separator(), JumpListEntry.ForTask(taskB)])` for mixed ordered entries. The existing task-only constructor remains valid. `Entries` is authoritative and `Tasks` excludes separators; the local 100-task cap and RequestedTaskCount exclude separators. Entry-based error indices count all entries. Windows ignores leading/trailing separators and suppresses adjacent duplicates; the library preserves the supplied sequence. BeginList minimum slots exclude separators and headers only up to four, and additional ones can reduce displayed destinations. Real Shell separator display has not yet been tested.

## Custom category and removed-destination tracking (SHELL-JL-04)

**Real Shell behaviour is NOT verified.** Everything below is covered by Fake tests only; the removed-list element types, the HRESULT of re-adding a removed item and the privacy-setting behaviour must be checked on a real Windows Shell (SHELL-JL-05).

`CommitList` discards the Shell's removed-destinations list, even for a Tasks-only Apply. To avoid silently losing a user's "remove from this list", use `JumpListDefinition.FromEntries(appId, entries, category, tracking)`:

- `JumpRemovedDestinationTracking` (opt-in, independent of the category): `ExcludedDestinationKeys` and `MatchOnly` (identities recognised but not displayed, e.g. hidden groups or previously registered items). With tracking on, the removed list is read on every Apply, with or without a category.
- `JumpCategoryDefinition`: `Name`, `Destinations` (`JumpDestinationDefinition`: stable `Key` + link) and optional `TasksFallback`. **A category without tracking is a validation error (`Tracking.Required`)**; once a category has ever been used, Tasks-only Applies must keep passing tracking (with an empty category or `MatchOnly`), otherwise removals are consumed unnoticed.
- Matching uses only Arguments (Ordinal) and path (OrdinalIgnoreCase), never display names or order. Arguments and Keys must be unique across destinations and `MatchOnly`. A link whose Arguments match but whose path differs is `Ambiguous` (Apply stops) unless the definition sets `PathPolicy = AllowArgumentsOnly`, i.e. the caller asserts the Arguments are a stable identifier unique to its app. The library hard-codes no argument format.
- Classification in `RemovedDestinations`: `Matched`, `Foreign` / `ForeignItem` (reported, do not block), `Unreadable` / `Ambiguous` (block). A readable link that matches nothing is `Foreign`, so register former items in `MatchOnly` if they must be protected.

Flow ("detect -> abort -> caller persists -> apply again"):

1. If a `Matched` removal is not in `ExcludedDestinationKeys`, the library calls `AbortList` and returns `RemovedDestinationsPending`. **Nothing is committed** (`ShellState = Unchanged`, `Succeeded = false`, `Error = null`). Pending is not Success.
2. **Persisting the keys is the caller's responsibility** (the library stores nothing). Save every `Matched` key with `AlreadyExcluded == false`, and only then add them to `ExcludedDestinationKeys`. Never pass unsaved keys.
3. **Apply again** after saving. Excluded destinations are never appended again.
4. `RemovedDestinationUnidentifiable`: an element was unreadable or ambiguous (or the list could not be read); aborted, nothing committed, the removed list is kept.

AppendCategory: only `E_ACCESSDENIED` (privacy setting/policy) is tolerated. With no `TasksFallback` the category is omitted (`CategorySkipped`); with one, the caller's explicit static entries are added to Tasks (`TasksFallback`). Destinations are never converted automatically, keyed fallback entries (`JumpListEntry.ForKeyedTask`) are dropped while excluded, duplicates of ordinary tasks are rejected, and separators are trimmed/merged inside the block (one separator is inserted after ordinary tasks). Do not put history-derived items in the fallback; the Windows privacy setting is never changed. Any other failing HRESULT aborts (`CategoryAppendFailed`). Each Apply tries AppendCategory again.

Result fields: `EffectiveDisplayMode` (set on success only: `TasksOnly`, `CategoryAndTasks`, `CategoryEmpty`, `TasksFallback`, `CategorySkipped`), `FallbackReason`, `RemovedDestinations`. They describe COM results, not what Explorer shows.

`JumpListEntry` is an API input model, not a persistence format; persist application data separately and reconstruct entries with the factories.
