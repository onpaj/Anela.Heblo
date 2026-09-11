# Implementation: wrap-create-manufacture-async-in-transaction

## What was implemented

Reordered and wrapped `GiftPackageManufactureService.CreateManufactureAsync` (FR-1) so that:

1. `GetGiftPackageDetailAsync` (the BOM/ingredient lookup, which calls out to `IManufactureClient`/`ILogisticsCatalogSource`) now runs **before** any DB transaction opens, so the transaction never holds a connection open across a cross-module read.
2. Log creation (`AddAsync` + `SaveChangesAsync`), the per-ingredient stock-down loop, and the output stock-up are now all executed inside a single `_giftPackageRepository.ExecuteInTransactionAsync(...)` call (the repository method added by the previous task in this feature), so a failure anywhere in that sequence rolls back the whole operation instead of leaving an orphaned `GiftPackageManufactureLog` row.

Also updated `GiftPackageManufactureServiceTests` to stub `ExecuteInTransactionAsync` on the repository mock for both the `GiftPackageManufactureDto` and `GiftPackageDisassemblyDto` return-type overloads (Moq does not invoke an unstubbed generic delegate parameter — it silently returns `default(TResult)` — so without the stub `CreateManufactureAsync` would return `null`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — reordered `CreateManufactureAsync` body to fetch gift package detail before opening the transaction, then wrapped log-save + ingredient loop + output stock-up in `_giftPackageRepository.ExecuteInTransactionAsync(...)`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — added `ExecuteInTransactionAsync` stubs (for both `GiftPackageManufactureDto` and `GiftPackageDisassemblyDto` delegate overloads) to the constructor so wrapped method bodies actually execute under test.

## Tests

Observed the expected RED state first (task file step 2):
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
```
```
Failed!  - Failed:     1, Passed:     9, Skipped:     0, Total:    10, Duration: 328 ms - Anela.Heblo.Tests.dll (net8.0)
```
(`CreateManufactureAsync_ShouldCreateManufactureLogWithConsumedItems` failed with `Expected result not to be <null>.`, exactly as predicted by the task file.)

After stubbing `ExecuteInTransactionAsync` in the test constructor (task file step 4), same command:
```
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 408 ms - Anela.Heblo.Tests.dll (net8.0)
```

Build gate (both before and after the test-file edit):
```
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
```
Result: `0 Error(s)` (pre-existing warning count only, unrelated to this change).

## How to verify

1. `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false` — expect `0 Error(s)`.
2. `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"` — expect `Passed: 10, Failed: 0, Total: 10`.

## Notes

No deviations from the task file — both "current contents" snippets (the `CreateManufactureAsync` method body and the test constructor) matched the real files verbatim, so both edits were applied exactly as specified, character-for-character including comments.

Domain-caution check: the ingredient loop in `CreateManufactureAsync` only calls `manufactureLog.AddConsumedItem(...)` (a domain method on the already-tracked, already-inserted `manufactureLog` aggregate) and `_stockOperationService.CreateOperationAsync(...)` — it never adds a child entity to a tracked parent's navigation collection with an explicit PK, so the known EF "UPDATE-0-rows instead of INSERT" hazard from the repo's gotcha list does not apply to this call site. No new hazard observed.

`artifacts/feat-4116/state.json` showed as modified in the working tree (pipeline-updated timestamps/status, not touched by this task) — left out of the commit since it isn't part of this task's file list and is presumably managed by the orchestrator.

## PR Summary

`GiftPackageManufactureService.CreateManufactureAsync` previously saved the `GiftPackageManufactureLog` via its own `SaveChangesAsync` immediately after creation, then made a further cross-module BOM lookup, then created one stock operation per ingredient plus one for the output product — all as separate, uncoordinated writes. If anything failed after the initial log save (the BOM lookup, or any stock operation), the log row was left orphaned with no compensating rollback.

This change moves the cross-module BOM/ingredient lookup (`GetGiftPackageDetailAsync`) to run before any transaction opens — it's a read against other modules via `IManufactureClient`/`ILogisticsCatalogSource` and must not hold a DB transaction open across it — and wraps everything that writes (log creation + save, the per-ingredient stock-down loop, and the output stock-up) inside a single `_giftPackageRepository.ExecuteInTransactionAsync(...)` call, using the transactional repository method added by the prior task in this feature. Any failure partway through now rolls back the entire operation atomically instead of leaving a partially-applied manufacture log.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — reorder + wrap `CreateManufactureAsync` in `ExecuteInTransactionAsync`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — stub `ExecuteInTransactionAsync` on the repository mock so wrapped delegates execute under test

## Status
DONE
