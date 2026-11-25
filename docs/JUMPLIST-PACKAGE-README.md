# ShellInterop.JumpList

Windows-only Jump List Tasks publisher targeting `net10.0-windows`; depends on `ShellInterop.Core >= 0.2.0-preview.2` (a NuGet minimum-version dependency, restored transitively). This is a pre-release (`0.2.0-preview.2`), prepared together with `ShellInterop.Core` `0.2.0-preview.2`. Publishing the package is not a legal clearance, and is separate from distributing an application that uses it. Earlier internal Core builds numbered `0.1.0-preview.1` included the JumpList types and were never published on NuGet.org. Add a reference to this package and rebuild when migrating from that earlier Core code. The namespace remains `ShellInterop.Core.JumpLists`, but binary compatibility is not guaranteed.

Supports Tasks, one or more custom categories (`JumpListDefinition.FromEntriesWithCategories`, `Categories`; `Category` is the single-category view) with removed-destination protection (Fake-tested; real Shell: SHELL-JL-05 single category, SHELL-JL-07 multiple categories, conditional PASS), apply/delete results and safe publisher shutdown. Separators are supported; Recent/Frequent are not implemented. Split-assembly real Shell regression passed without separators (SHELL-SPLIT-02); separators inside `TasksFallback` are real-Shell confirmed (SHELL-JL-07). MIT license; see LICENSE and the repository's rights-review notes.

Packing the JumpList project with the old `0.1.0-preview.1` version remains blocked by a guard (the old Core lacks the split internals); the current `0.2.0-preview.2` of both projects packs. For local tests of other versions, pass the same `-p:Version=...` to both projects and do not publish it.

Use `JumpListDefinition.FromEntries(appId, [JumpListEntry.ForTask(taskA), JumpListEntry.Separator(), JumpListEntry.ForTask(taskB)])` for mixed ordered entries. The existing task-only constructor remains valid. `Entries` is authoritative and `Tasks` excludes separators; the local 100-task cap and RequestedTaskCount exclude separators. Entry-based error indices count all entries. Windows ignores leading/trailing separators and suppresses adjacent duplicates; the library preserves the supplied sequence. BeginList minimum slots exclude separators and headers only up to four, and additional ones can reduce displayed destinations. Real Shell: separators inside `TasksFallback` were confirmed with this library (SHELL-JL-07); a standalone test application using this library's public API also confirmed separator display (SHELL-JL-02). Other layouts are not verified.

## Changes in 0.2.0-preview.2

Compared with `0.2.0-preview.1` (the previously published version):

- **BeginList slot-aware planning.** `ApplyPlannedAsync` with `JumpListPlanner` / `JumpListPlanContext` (`BeginListSlots`, `Phase`: `Initial` / `PrivacyFallbackRetry`); see "Choosing items from the BeginList slot count". New `JumpListApplyStatus.PlanFailed` and `JumpListFailurePhase.PlanGeneration` report a planner that threw or returned null / an unusable definition (nothing is committed).
- **Automatic fallback separators can be turned off.** `JumpListDefinition.AutoSeparateTasksFallback` (default `true`) and `WithAutoSeparateTasksFallback(bool)`. With `false`, the publisher inserts no separator of its own between the fixed Tasks and a `TasksFallback` block (or between fallback blocks); separators you put explicitly in `Entries` or in a fallback list are always kept.
- **Same-command check removed for fallback tasks.** A `TasksFallback` task that has the same path and arguments as an ordinary Task or another fallback task was rejected by validation (`Fallback.Invalid`) in 0.2.0-preview.1; it is now accepted. Passing validation is not a statement about what Explorer displays for duplicate commands; that is separate and not guaranteed.
- **Release build path normalization.** The DLL's CodeView PDB path no longer contains a local absolute path (see the repository's `scripts/Pack-Release.ps1`).

Notes for planned publication:

- `BeginListSlots` is the value the Shell reports from `BeginList`; it does not guarantee how many items Explorer actually shows.
- The planner runs synchronously on the dedicated STA worker while the COM session is open; keep it fast and do not call the same publisher from it.
- When BeginList is called again (privacy fallback retry), the planner is called again and plans with the new session's information.

### Compatibility notes (0.2.0-preview.1 to 0.2.0-preview.2)

This is a preview and complete backward compatibility is not promised. Specifically:

- `ApplyAsync` is kept; its contract is unchanged.
- The integer values of the published `JumpListApplyStatus` members are fixed explicitly and unchanged; `PlanFailed` is appended with a new value (14). `JumpListFailurePhase.PlanGeneration` is appended after the existing members. Code that stores these enums as integers keeps working.
- `IJumpListPublisher` gained a member, `ApplyPlannedAsync`. External implementations or test doubles of `IJumpListPublisher` must add it, and binaries compiled against `0.2.0-preview.1` that implement the interface need to be rebuilt. No adapter interface or default implementation is provided.
- Fallback tasks that were rejected before (same command as another task) are now accepted.

## Icon location

The library sets each task's icon with `IShellLink::SetIconLocation` (`IconResourcePath` / `IconResourceIndex`). A success HRESULT only means the API accepted the value: it does not guarantee that the resource exists, that the index is right, or that the intended icon is displayed (in verification, `SetIconLocation` returned `S_OK` for a nonexistent path and an empty string). If it returns a failure HRESULT, the current implementation stops creating that link; `ApplyAsync` normally returns `TaskCreationFailed` with `FailurePhase = CreateTask` and the HRESULT, and the update is aborted with `AbortList`. The library does not publish the link without an icon, re-create it, or choose a substitute icon; no evidence was collected that continuing with the failed COM object is safe, so this is a design limitation of the current release. The calling application chooses the icon resource, handles icon-retrieval failures (for example by picking its own substitute), and checks the apply result. By default (`RequireExistingFiles = true`) an empty or nonexistent `IconResourcePath` is rejected before any COM call. See the repository README ("Icon location: what the library does and does not guarantee") for details and Microsoft links.

## Custom category and removed-destination tracking (SHELL-JL-04)

**Real Shell status.** The behaviour below is covered by Fake tests; real-Shell checks cover a single category (SHELL-JL-05) and multiple categories with removed-destination tracking and the privacy-setting `E_ACCESSDENIED` fallback (SHELL-JL-07, conditional PASS). SHELL-JL-08 (conditional PASS) added real-Shell checks of removed destinations after Explorer restart and a later-category `E_ACCESSDENIED` induced by changing the privacy setting mid-transaction. Not verified: a naturally occurring later-category `E_ACCESSDENIED`, empty-Tasks clearing, OS restart, other Windows builds. Fake-test results are not real-Shell results.

Registration success does not mean the Shell displays every destination: it limits the displayed count, and the `JumpListItems_Maximum` registry value changes that count. Put important destinations in the first categories; see the repository README ("Displayed destination count") for the observed behavior and its limits.

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

AppendCategory: only `E_ACCESSDENIED` (privacy setting/policy) is tolerated. With no `TasksFallback` the category is omitted (`CategorySkipped`); with one, the caller's explicit static entries are added to Tasks (`TasksFallback`). Destinations are never converted automatically, keyed fallback entries (`JumpListEntry.ForKeyedTask`) are dropped while excluded, fallback tasks with the same path and arguments as an ordinary or another fallback task are not rejected by the library (Windows display of identical commands is not guaranteed for every Shell version; the separate destination, key, and removal-history validations remain), and separators are trimmed/merged inside the block (one separator is inserted after ordinary tasks). Do not put history-derived items in the fallback; the Windows privacy setting is never changed. Any other failing HRESULT aborts (`CategoryAppendFailed`). Each Apply tries AppendCategory again.

Result fields: `EffectiveDisplayMode` (set on success only: `TasksOnly`, `CategoryAndTasks`, `CategoryEmpty`, `TasksFallback`, `CategorySkipped`), `FallbackReason`, `RemovedDestinations`. They describe COM results, not what Explorer shows.

`JumpListEntry` is an API input model, not a persistence format; persist application data separately and reconstruct entries with the factories.

### Choosing items from the BeginList slot count

`ApplyPlannedAsync(candidates, planner)` runs the usual BeginList..CommitList transaction, but calls `planner(JumpListPlanContext)` after BeginList succeeded, so the caller can choose what to publish from `context.BeginListSlots` (the `pcMinSlots` value returned by `ICustomDestinationList::BeginList`, passed through unchanged; 0 stays 0). The value is reported by the Shell; it is not a guaranteed number of displayed rows (pins, Tasks, categories, display state and settings also matter) and it can differ between sessions. The library subtracts or reserves nothing; reserving control tasks, ordering and selection are the caller's decisions.

`candidates` supplies the AppUserModelId, the removed-destination tracking and all identities that may show up in the removed list (include every destination the planner may choose from). The planner returns the definition to publish, or null / throws to abort without committing (`PlanFailed`). If AppendCategory fails with E_ACCESSDENIED after an earlier category succeeded, the session is aborted, BeginList is called again and the planner is called again (`Phase = PrivacyFallbackRetry`) with the new session's slot count. The planner runs on the STA worker while the COM session is open: keep it synchronous and fast, and do not call the same publisher from it. `JumpListApplyResult.BeginListSlots` records the value of the last successful BeginList (null if none).

```csharp
var result = await publisher.ApplyPlannedAsync(allCandidates, ctx =>
{
    int budget = (int)Math.Max(0, (long)ctx.BeginListSlots - reservedByCaller);
    return BuildDefinitionFor(budget, ctx.Phase);   // caller-owned selection logic
});
```