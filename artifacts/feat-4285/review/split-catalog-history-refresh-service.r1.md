# Code Review: split-catalog-history-refresh-service

## Summary

The implementation extracts `CatalogHistoryRefreshService` exactly as specified in the task
context — same constructor shape, same five methods, same doc comments and resilience/logging
behavior — and moves the 6 named test cases verbatim into a new test file. Both the standalone
Application-project build and the new test file's own test run were verified and pass. One
necessary fix (a missing `using` in the test file) was required to make the spec's own given
code compile; it's a minimal, in-scope correction, not scope creep.

## Review Result: PASS

### task: split-catalog-history-refresh-service
**Status:** PASS

Verified against the task context:
- `CatalogHistoryRefreshService.cs` created at the exact specified path, with the exact
  specified constructor parameters (10, in the specified order, each null-checked) and the
  exact specified method bodies (`RefreshSalesData`, `RefreshSetPartsData` +
  `FetchSetPartsPerBundleAsync`, `RefreshPurchaseHistoryData`, `RefreshConsumedHistoryData`,
  `RefreshManufactureHistoryData`), matching the source content byte-for-byte against the
  task-context code block.
- `CatalogDataRefreshService.cs` was correctly left untouched — the task context explicitly
  notes this is not yet the removal step, and confirms no duplicate-registration concern
  exists at this point since DI wiring isn't touched yet either.
- Step 2 (standalone Application build) was run: `dotnet build
  src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — Build succeeded, 0 errors.
- `CatalogHistoryRefreshServiceTests.cs` created at the exact specified path, moving the 6
  named tests verbatim (`RefreshSalesData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`,
  `RefreshSetPartsData_FetchesPartsOnlyForBundleCodedProducts`,
  `RefreshSetPartsData_WhenOneBundleFails_KeepsPartsFromTheOthers`,
  `RefreshSetPartsData_FetchesEachBundleSeparately`,
  `RefreshSetPartsData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`,
  `RefreshSetPartsData_WhenNoBundleCodedProductsExist_RetainsExistingCacheAndLogsWarning`) with
  a `CreateService` helper scoped to the 7 constructor parameters this class's tests actually
  vary, matching the task context's Step 3 code block.
- The task context's Step 3 test code as given omits
  `using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;`, which `IConsumedMaterialsClient`
  (used in `CreateService`'s signature) requires — without it the file fails with
  `CS0246: The type or namespace name 'IConsumedMaterialsClient' could not be found`. The
  developer added the missing using. This is a correctness fix to the spec's own given code,
  not an unrequested change, and does not affect any other behavior. Confirmed empirically:
  the file fails to compile without the added using and compiles cleanly with it.
  - Step 4 (`dotnet test ... --filter "FullyQualifiedName~CatalogHistoryRefreshServiceTests"`)
    was run and produced `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`, matching the
    spec's expected outcome exactly.
- Step 5 (commit) is handled by the pipeline orchestrator after this review, per the
  orchestrator's two-line staging rule — not performed inside the developer step itself,
  consistent with how this pipeline is wired.

No functional requirement from the task context is unmet, nothing was added beyond what the
task context specifies (aside from the one necessary using-directive fix), and both
verification commands the spec calls for were actually run with passing results.

## Docs to Update

(none — this is an internal, same-module code extraction with no public API, CLI, environment
variable, or operational behavior change; nothing in the documentation map is affected)

## Overall Notes

This is task 1 of a multi-task split (the task-context directory also lists
`split-catalog-meta-refresh-service`, `split-catalog-reference-refresh-service`,
`split-catalog-stock-refresh-service`, `wire-catalog-repository-and-module`, and
`remove-old-refresh-service-and-verify`). `CatalogDataRefreshService.cs` and
`CatalogHistoryRefreshServiceTests.cs`'s counterpart in the old test file remain in place by
design until the removal task runs later in this feature's pipeline — that duplication is
expected at this stage, not a defect.
