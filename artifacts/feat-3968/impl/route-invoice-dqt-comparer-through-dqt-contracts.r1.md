# Implementation: route-invoice-dqt-comparer-through-dqt-contracts

## What was implemented

`IInvoiceShoptetSource` and `IInvoiceErpClient` now return `DqtInvoiceSnapshot` (DataQuality-owned
contract types) instead of `Invoices`-domain types (`IssuedInvoiceDetailBatch`/`IssuedInvoiceDetail`).
`InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter` were rewired to perform the
query/response mapping via the existing `InvoiceDqtSnapshotMapper` extension methods
(`ToDqtSnapshot`/`ToDqtItem`) built in the previous task. `InvoiceDqtComparer` was rewritten to
consume only `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery` — all `.Price.*`/
`.ItemPrice.*` nested accesses became flat `.TotalWithVat`/`.TotalWithoutVat`/`.WithVat`/
`.WithoutVat`, the `SelectMany(b => b.Invoices)` batch-flatten and the `DateOnly → DateTime`
conversion moved into `InvoiceShoptetSourceAdapter`. The 15 existing `InvoiceDqtComparerTests.cs`
cases were updated to build/mock the new fixture types (all tolerance/duplicate-detection/message-
format logic and assertions unchanged). Two new adapter-level test files were added covering the
new query/response mapping in both adapters.

After this task, `Anela.Heblo.Application.Features.DataQuality.*` has zero actual `using`
references to `Anela.Heblo.Domain.Features.Invoices` (confirmed via grep — only the
architecture-test allowlist, out of scope for this task, still needs closing in a later task).

Note on the task's "14 existing test cases" framing: the actual `InvoiceDqtComparerTests.cs` file
(both before and after this change) contains 15 `[Fact]` methods — the task's own enumerated list
of test names also has 15 entries, so this is just an off-by-one in the task's prose summary, not a
discrepancy introduced by this change. All 15 were preserved and pass.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs` — now returns `Task<List<DqtInvoiceSnapshot>>`, takes `DqtInvoiceSourceQuery`; dropped the `Invoices`-domain `using`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs` — now returns `Task<List<DqtInvoiceSnapshot>>`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs` — consumes `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery` only; flat total/item price fields; no more batch-flatten or `DateOnly→DateTime` conversion in this file; dropped the `Invoices`-domain `using`.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs` — maps `DqtInvoiceSourceQuery` → `IssuedInvoiceSourceQuery` (RequestId passthrough, `DateOnly.ToDateTime(TimeOnly.MinValue)` for DateFrom/DateTo, InvoiceId/Currency left at their `IssuedInvoiceSourceQuery` defaults), calls `_inner.GetAllAsync`, flattens batches, maps each invoice via `ToDqtSnapshot()`.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs` — calls `_inner.GetAllAsync(from, to, ct)` and maps each result via `ToDqtSnapshot()`.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` — rewritten fixture helpers (`MakeInvoice`, `MakeItem`, `SetupShoptet`, `SetupFlexi`) to build/mock `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery`; all 15 `[Fact]` cases and assertions kept logically identical.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs` (new) — 2 tests.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceErpClientAdapterTests.cs` (new) — 3 tests.

## Tests

- `InvoiceShoptetSourceAdapterTests.cs`:
  - `GetAllAsync_MapsQueryFieldsIntoInnerQuery` — RequestId passthrough, `DateOnly→DateTime` conversion, `InvoiceId` null, `Currency` "CZK".
  - `GetAllAsync_FlattensBatchesAndMapsInvoicesAndItemsToSnapshots` — multiple batches flattened, invoice/item fields (Code, TotalWithVat, TotalWithoutVat, per-item Code/Amount/WithVat/WithoutVat) mapped correctly.
- `InvoiceErpClientAdapterTests.cs`:
  - `GetAllAsync_ForwardsFromToAndCancellationTokenToInnerClient` — arguments forwarded unchanged.
  - `GetAllAsync_MapsInvoicesWithMultipleItemsToSnapshots` — multi-item invoice mapped correctly.
  - `GetAllAsync_ReturnsEmptyList_WhenInnerResultIsEmpty` — empty inner result → empty list.

Actual `dotnet test` output (all run with `--filter`, `--no-restore`/`--no-build` once warm):

- Step 9 — `FullyQualifiedName~InvoiceDqtComparerTests`:
  `Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15`
- Step 12 — `FullyQualifiedName~InvoiceShoptetSourceAdapterTests|FullyQualifiedName~InvoiceErpClientAdapterTests`:
  `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`
- Step 13 — `FullyQualifiedName~Features.DataQuality|FullyQualifiedName~Features.Invoices` (regression slice):
  `Failed! - Failed: 2, Passed: 178, Skipped: 0, Total: 180`
  The 2 failures are `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.GetSyncStatsAsync_EmitsExactlyOneSqlCommand`
  and `...GetSyncStatsAsync_ReturnsCorrectStatsFromRealDatabase`, both failing with
  `System.ArgumentException: Docker is either not running or misconfigured` (Testcontainers/Postgres
  fixture requires a Docker daemon, unavailable in this sandbox). This file was not touched by this
  task and the failure is a pre-existing environment limitation, not a regression from this change.

`dotnet build` of the full solution (`Anela.Heblo.sln`) completed with 0 errors.

## How to verify

```
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtComparerTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceShoptetSourceAdapterTests|FullyQualifiedName~InvoiceErpClientAdapterTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.DataQuality|FullyQualifiedName~Features.Invoices"
```
(Expect the 2 Testcontainers/Docker failures noted above unless a Docker daemon is available.)

Also confirm zero remaining `Invoices`-domain references in DataQuality:
```
grep -r "Anela.Heblo.Domain.Features.Invoices" backend/src/Anela.Heblo.Application/Features/DataQuality
```
(returns nothing)

## Notes

- All actual member names on `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery` and the
  mapper (`InvoiceDqtSnapshotMapper.ToDqtSnapshot`/`ToDqtItem`, both extension methods) matched the
  task context's assumptions exactly — no deviation was needed there.
- `IssuedInvoiceSourceQuery.InvoiceId`/`Currency` are left at their class defaults (`null`/`"CZK"`)
  in the adapter, per the task's note that no explicit assignment is needed.
- `DI` registration in `InvoicesModule.cs` was left untouched, as specified — the interface
  signature change doesn't affect the registration/lifetime.
- The one out-of-scope file touched by other, unrelated tooling (`artifacts/feat-3968/state.json`)
  was deliberately excluded from staging/commit, per the task's file list.
- Test run turnaround in this sandbox was unusually slow (several minutes per `dotnet test`
  invocation, including full-solution builds triggered by test-project restores); this is an
  environment characteristic, not related to the change itself.

## Status
DONE
