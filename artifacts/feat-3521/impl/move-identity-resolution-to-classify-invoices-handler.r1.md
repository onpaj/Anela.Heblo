# Implementation: move-identity-resolution-to-classify-invoices-handler

## What was implemented
Moved `ICurrentUserService` resolution out of `InvoiceClassificationService` and into `ClassifyInvoicesHandler`, per ADR-005 (identity resolution belongs in MediatR handlers, not application services). `IInvoiceClassificationService.ClassifyInvoiceAsync` now takes an explicit `string processedBy` parameter instead of resolving the current user internally. `ClassifyInvoicesHandler` now injects `ICurrentUserService`, resolves identity exactly once per `Handle` invocation before the `foreach` loop, applies the fallback rule (`IsAuthenticated` false, or `Name` null/empty, → `"system"`; otherwise `currentUser.Name`), and passes that single `processedBy` value into every `ClassifyInvoiceAsync` call in the batch. This fixes the latent bug where the hourly scheduled `InvoiceClassificationJob` (no HTTP context) previously wrote `ProcessedBy = "Anonymous"` into classification history.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs` — changed `ClassifyInvoiceAsync(ReceivedInvoice invoice)` signature to `ClassifyInvoiceAsync(ReceivedInvoice invoice, string processedBy)`.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — removed `ICurrentUserService _currentUserService` field, constructor parameter, and the `using Anela.Heblo.Domain.Features.Users;` import; removed the internal `GetCurrentUser()` call; all four `RecordClassificationHistory` call sites now use the `processedBy` parameter instead of `currentUser.Name`.
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` — added `ICurrentUserService` field/constructor parameter (after `IClassificationRuleRepository`, before `logger`) and `using Anela.Heblo.Domain.Features.Users;`; resolves `currentUser`/`processedBy` once before the `foreach` loop using the specified fallback rule; passes `processedBy` into `ClassifyInvoiceAsync`.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs` — removed `Mock<ICurrentUserService>` field/usage and the `Anela.Heblo.Domain.Features.Users` import; each of the four tests now defines an explicit `processedBy` literal, calls `ClassifyInvoiceAsync(invoice, processedBy)`, and asserts `capturedHistory.ProcessedBy.Should().Be(processedBy)`.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` — added `Mock<ICurrentUserService>` field wired into the handler constructor (with a default authenticated `"test-user"` setup applied in the test constructor so the three pre-existing tests keep passing unmodified in behavior); updated the three existing `ClassifyInvoiceAsync` mock setups to the two-argument overload; added two new tests: `Handle_WhenCurrentUserIsUnauthenticated_PassesSystemAsProcessedBy` and `Handle_WhenCurrentUserIsAuthenticated_PassesUserNameAsProcessedBy`, verifying the `"system"` fallback and the authenticated `"jane.doe"` pass-through respectively.

`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs` was verified (not modified) — it registers `IInvoiceClassificationService`, `IRuleEvaluationEngine`, repositories, and rule implementations only; no `ICurrentUserService` registration existed or was needed there.

## Tests
- `InvoiceClassificationServiceTests.cs` — all 4 tests updated and passing (no-match, ABRA success, ABRA failure, exception-thrown paths), asserting `ProcessedBy` equals the explicitly passed-in value.
- `ClassifyInvoicesHandlerTests.cs` — 3 pre-existing tests (parallel fetch, missing-invoice error counting, batch-mode fetch) updated to the new two-arg mock signature; 2 new tests added covering the unauthenticated (`"system"`) and authenticated (`currentUser.Name`) identity-resolution paths.

## How to verify
```
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"
dotnet test Anela.Heblo.sln   # full suite
dotnet format Anela.Heblo.sln --verify-no-changes
```
Build: 0 errors (250 pre-existing warnings, unrelated to this change).
Scoped test run: 88/88 InvoiceClassification tests passed.
Full suite: 5416 passed, 64 failed, 4 skipped — all 64 failures are pre-existing `Docker is either not running or misconfigured` Testcontainers errors (Leaflet/Article persistence integration tests requiring a Postgres container), unrelated to this change and present regardless of it; no InvoiceClassification test failed.
`dotnet format --verify-no-changes` on the touched files: clean, no formatting changes needed.

## Notes
- Kept a default `GetCurrentUser()` setup in the `ClassifyInvoicesHandlerTests` constructor (returns an authenticated `"test-user"`) rather than duplicating an identical setup into each of the three pre-existing tests individually — functionally equivalent to what the task context describes, just centralized to avoid repetition, and it does not weaken the new dedicated tests which each override the setup explicitly for their scenario.
- `artifacts/feat-3521/state.json` had an unstaged modification (pipeline bookkeeping, updated by the orchestrator to `in_progress`) that was present in the working tree but was deliberately left out of the commit per the instruction not to touch anything under `artifacts/`.

## Status
DONE
