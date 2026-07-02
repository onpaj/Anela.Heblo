# Implementation: dqt-job-runner-interface-and-di

## What was implemented

Introduced a shared `IDqtJobRunner` interface implemented by both `InvoiceDqtJobRunner` and `DriftDqtJobRunner`, added `CanHandle(DqtTestType)` methods to each, registered `IDqtJobRunner` additively in `DataQualityModule` (alongside the existing narrow-interface registrations), and added a new `ErrorCodes.DqtUnsupportedTestType = 2204` entry. Purely additive — no existing method bodies, handlers, or existing tests were changed.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs` — new interface with `bool CanHandle(DqtTestType testType)` and `Task RunAsync(Guid runId, CancellationToken ct = default)`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` — now also implements `IDqtJobRunner`; added `CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` — now also implements `IDqtJobRunner`; added `CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);` (delegates to the already-injected `IEnumerable<IDriftDqtComparer>`).
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — added two additive lines: `services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();` and `services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();`, directly beneath the pre-existing narrow-interface registrations (which were left untouched).
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `[HttpStatusCode(HttpStatusCode.InternalServerError)] DqtUnsupportedTestType = 2204,` immediately after `DqtExternalServiceError = 2203,` in the DataQuality (22XX) block.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityModuleTests.cs` — new test file (content exactly as specified in the task context) verifying DI registrations via `ServiceDescriptor` inspection, without building the provider.

## Tests

- `DataQualityModuleTests.AddDataQualityModule_RegistersBothRunnersUnderIDqtJobRunner` — asserts exactly 2 `IDqtJobRunner` descriptors, implemented by `InvoiceDqtJobRunner` and `DriftDqtJobRunner`.
- `DataQualityModuleTests.AddDataQualityModule_RetainsExistingNarrowInterfaceRegistrations` — asserts the pre-existing `IInvoiceDqtJobRunner`/`IDriftDqtJobRunner` registrations are still present.
- All pre-existing tests under `backend/test/Anela.Heblo.Tests/Features/DataQuality/` (67 tests total in that namespace, including `InvoiceDqtJobRunnerTests`, `DriftDqtJobRunnerTests`, `RunDqtHandlerTests`, `GetDqtRunDetailHandlerTests`, etc.) were run unmodified and continue to pass.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
cd test/Anela.Heblo.Tests
dotnet test --filter "FullyQualifiedName~Features.DataQuality" --no-build
```

Expected: build succeeds with 0 errors; test run reports `Passed! - Failed: 0, Passed: 67, ...`.

Also verified `dotnet format Anela.Heblo.sln --verify-no-changes --include <touched files>` exits 0 (no formatting changes needed).

## Notes

No deviations from the task spec. `artifacts/` directory changes (state.json) were intentionally left out of the commit per the task constraints (out of scope, orchestrator-managed).

## Status
DONE
