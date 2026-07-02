# Code Review: dqt-job-runner-interface-and-di

## Summary
The implementation matches the task spec verbatim: `IDqtJobRunner` was created exactly as specified, both `InvoiceDqtJobRunner` and `DriftDqtJobRunner` now implement it with the exact `CanHandle` bodies given, DI registrations are additive, the `ErrorCodes.DqtUnsupportedTestType = 2204` entry was added with the correct `[HttpStatusCode(HttpStatusCode.InternalServerError)]` attribute, and the new `DataQualityModuleTests.cs` file matches the spec's exact test content. Verified independently by inspecting `git show 6341f82` (diff touches exactly the 6 files listed, no others), building the solution, and running the DataQuality test suite.

## Review Result: PASS

### task: dqt-job-runner-interface-and-di
**Status:** PASS

## Verification performed
- `git show 6341f82` — diff is scoped to exactly the six files named in the task (`DataQualityModule.cs`, `DriftDqtJobRunner.cs`, `IDqtJobRunner.cs` [new], `InvoiceDqtJobRunner.cs`, `ErrorCodes.cs`, `DataQualityModuleTests.cs` [new]). No changes to `RunDqtHandler.cs`, `GetDqtRunDetailHandler.cs`, or their tests, satisfying the "purely additive" and "no existing handler changes" constraints.
- `IDqtJobRunner.cs` content matches the spec's exact required code (`bool CanHandle(DqtTestType testType)` + `Task RunAsync(Guid runId, CancellationToken ct = default)`).
- `InvoiceDqtJobRunner` — class now declares `: IInvoiceDqtJobRunner, IDqtJobRunner`; `CanHandle` returns `testType == DqtTestType.IssuedInvoiceComparison` exactly as specified. Confirmed against `DqtTestType` enum (`IssuedInvoiceComparison = 1, ProductPairing = 2, StockWriteBackReconciliation = 3`) that this correctly returns `true` only for invoice comparison and `false` for the other two, satisfying the acceptance criterion.
- `DriftDqtJobRunner` — class now declares `: IDriftDqtJobRunner, IDqtJobRunner`; `CanHandle` delegates to `_comparers.Any(c => c.TestType == testType)`, reusing the already-injected `IEnumerable<IDriftDqtComparer>` with no new dependency, exactly as specified. Since `DataQualityModule` registers `IDriftDqtComparer` for `ProductPairing` and `StockWriteBackReconciliation` (via `ProductPairingDqtComparer`/`StockWriteBackDqtComparer`), this correctly returns `true` for those two test types and `false` for `IssuedInvoiceComparison`.
- `DataQualityModule.cs` — two new `AddScoped<IDqtJobRunner, ...>()` lines added directly beneath the pre-existing narrow-interface registrations; the two pre-existing lines are untouched, confirming additive-only DI change.
- `ErrorCodes.cs` — `DqtUnsupportedTestType = 2204` inserted immediately after `DqtExternalServiceError = 2203,` inside the `22XX` block, before the Marketing Calendar comment, with `[HttpStatusCode(HttpStatusCode.InternalServerError)]` as specified.
- `DataQualityModuleTests.cs` — file content is byte-for-byte consistent with the spec's exact required test code (two `[Fact]` tests: registration count/type check for `IDqtJobRunner`, and retention check for the narrow interfaces).
- Ran `dotnet build Anela.Heblo.sln` from the worktree root — succeeded with 0 errors (253 pre-existing warnings, none related to this change).
- Ran `dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build` — `Passed! - Failed: 0, Passed: 67, Skipped: 0, Total: 67`, consistent with the impl summary's claim.
- `git status --short` after build/test — clean, confirming no stray file changes or artifacts leaked from the run.

All acceptance criteria in the task spec are met.

## Docs to Update
None — this is an internal-interface/DI change with no external-facing behavior; no doc references to `IDqtJobRunner` exist elsewhere in the docs tree that would need updating for this additive step.

## Overall Notes
Clean, minimal, spec-compliant change. The implementer correctly avoided the temptation to also wire `RunDqtHandler`/`GetDqtRunDetailHandler` to the new interface, respecting the task's explicit "purely additive, no handler changes" boundary — that wiring is presumably scoped to a later task in this feature.
