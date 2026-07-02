# Implementation: create-dqt-resilience-contract-and-adapter

## What was implemented
Created the new DataQuality-owned `IDqtResilienceService` contract (identical method shape to `ICatalogResilienceService`), a Catalog-owned `DataQualityResilienceAdapter` that delegates 1:1 to the existing `ICatalogResilienceService` singleton, registered the adapter in `CatalogModule.AddCatalogModule()` as `Scoped`, and added a delegation unit test for the adapter mirroring the two existing sibling adapter tests in the same test folder.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs` — new DataQuality-owned contract with `ExecuteWithResilienceAsync<T>`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs` — new internal sealed adapter implementing `IDqtResilienceService` by delegating to `ICatalogResilienceService`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added `services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();` alongside the existing `IStockOperationQuery`/`IStockTakingQuery` adapter registrations
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs` — new test file with 2 tests

## Tests
- `DataQualityResilienceAdapterTests.ExecuteWithResilienceAsync_DelegatesToUnderlyingService_WithSameArgumentsAndReturnValue` — verifies the adapter forwards operation, operationName, and cancellation token unchanged and returns the underlying result
- `DataQualityResilienceAdapterTests.ExecuteWithResilienceAsync_PropagatesException_WhenUnderlyingServiceThrows` — verifies exceptions from the underlying service propagate unchanged

Both tests pass: `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~DataQualityResilienceAdapterTests"` → 2/2 passed.

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~DataQualityResilienceAdapterTests"
```
Build succeeds (0 errors); both adapter tests pass. `ProductPairingDqtComparer` still uses `ICatalogResilienceService` at this point (unchanged) — that switch is task 2.

## Notes
No deviations from the task context. `CatalogModule.cs` already had the `using Anela.Heblo.Application.Features.DataQuality.Contracts;` directive from the sibling adapters, so no new `using` was needed, as anticipated in the task context.

## PR Summary
Added a `IDqtResilienceService` contract owned by the DataQuality module and a `DataQualityResilienceAdapter` owned by the Catalog module that delegates to the existing `ICatalogResilienceService` singleton, registered in Catalog's DI module. This is the first step of removing `ProductPairingDqtComparer`'s direct dependency on Catalog's Application-layer resilience service — the comparer itself is switched over in a follow-up task.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs` — new contract
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs` — new adapter
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — DI registration
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityResilienceAdapterTests.cs` — new tests

## Status
DONE
