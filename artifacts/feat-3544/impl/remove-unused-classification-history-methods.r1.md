# Implementation: remove-unused-classification-history-methods

## What was implemented
Removed the two unused methods (`GetHistoryAsync(int skip, int take)` and `GetHistoryByInvoiceIdAsync(string abraInvoiceId)`) from `IClassificationHistoryRepository` and their implementations from `ClassificationHistoryRepository`, per the task plan. `AddAsync` and `GetPagedHistoryAsync` are unchanged. Independently verified via repo-wide grep that neither removed method had any caller before deleting them.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs` — dropped the two unused method declarations, now exposes only `AddAsync` and `GetPagedHistoryAsync`.
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — dropped the two unused method implementations.

## Tests
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (confirms no remaining caller references the removed methods; C# is statically typed so any lingering reference would be a compile error).
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <the two changed files>` — no formatting diffs.
- `grep -rn "GetHistoryAsync\|GetHistoryByInvoiceIdAsync" backend/ --include=*.cs` — `GetHistoryByInvoiceIdAsync` has zero remaining matches anywhere. Remaining `GetHistoryAsync` matches are all unrelated methods on other interfaces/classes (`IPurchaseHistoryClient`, `IManufactureHistoryClient`, `IPurchaseOrderRepository`, `FlexiManufactureHistoryClient`, `FlexiPurchaseHistoryQueryClient`, `PurchaseOrderRepository`) — none reference `IClassificationHistoryRepository`.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` — 90/90 passed.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (full project) — 5588 passed, 4 skipped, 64 failed. All 64 failures are pre-existing Testcontainers/Docker-dependent integration tests (Bank, Catalog stock-up, Leaflet, MeetingTasks, Photobank, Purchase SQL-shape, KnowledgeBase, GridLayouts, Smartsupp) failing with `System.ArgumentException: Docker is either not running or misconfigured` — this sandbox has no Docker daemon. None of the 64 failures are in the InvoiceClassification module or reference the removed methods; this is an environment limitation unrelated to this change.

## How to verify
1. `dotnet build Anela.Heblo.sln` — should succeed with 0 errors.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` — should show 90/90 passing.
3. `grep -rn "GetHistoryByInvoiceIdAsync" backend/` — should return no results.

## Notes
No test, mock, or fake referenced the two removed methods (confirmed by the task plan's analysis and independently by grep), so no test files needed changes. The pre-existing Docker-dependent integration test failures in the full suite run are unrelated to this change and are an artifact of this sandbox environment lacking a Docker daemon, not a regression introduced here.

## PR Summary
`IClassificationHistoryRepository` declared two methods — `GetHistoryAsync(int skip, int take)` and `GetHistoryByInvoiceIdAsync(string abraInvoiceId)` — that no handler, service, or test in the codebase ever called. This PR deletes both from the interface and their implementations from `ClassificationHistoryRepository`, leaving only the two methods actually in use: `AddAsync` and `GetPagedHistoryAsync`. This is a pure subtraction with no behavioral change to any existing consumer.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs` — removed 2 unused method declarations
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — removed 2 unused method implementations

## Status
DONE
