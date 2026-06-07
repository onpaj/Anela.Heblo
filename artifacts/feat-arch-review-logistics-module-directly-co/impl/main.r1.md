All 5 implementation commits are on the branch. The implementation is complete.

---

# Implementation: Decouple Logistics Module From Catalog-Owned Interfaces (TransportBoxCompletionService)

## What was implemented

Removed all direct Catalog dependencies from `TransportBoxCompletionService` by introducing a Logistics-owned query contract and a Catalog-side adapter. Applied the same consumer-owns-the-contract pattern that PR #2201 established for the write side.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs` — new enum mirroring `StockUpOperationState` with explicit integer values
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs` — new DTO class (DocumentNumber + State only)
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs` — new Logistics-owned query contract
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs` — new `internal sealed` adapter in Catalog that implements the contract, delegates to `IStockUpOperationRepository`, exhaustive switch mappers for both source type and state
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added `AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>()` next to existing write-side adapter
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — replaced `IStockUpOperationRepository` dependency with `ILogisticsStockOperationQueryService`; removed all Catalog imports; updated call site and state comparisons
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapterTests.cs` — new tests: source mapping, state mapping theory, enum-parity guard, DocumentNumber projection, cancellation token propagation
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — swapped mock from `IStockUpOperationRepository` to `ILogisticsStockOperationQueryService`; replaced `CreateOperation` with `CreateStatus` helper
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — emptied `LogisticsCatalogAllowlist`; added `CatalogLogisticsAllowlist` entries for the new adapter's Logistics type references

## Tests

- `LogisticsStockOperationQueryAdapterTests` — 11 tests: source type mapping (×2), unknown source throws, state mapping theory (×4), DocumentNumber projection, empty result, enum-parity guard, CancellationToken propagation
- `TransportBoxCompletionServiceTests` — 7 tests: same scenarios as before, now using `LogisticsStockOperationStatus` DTOs
- `ModuleBoundariesTests` — 14/14 architecture boundary rules pass; `Logistics → Catalog` is now actively enforced with empty allowlist

## How to verify

```bash
cd backend && dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
grep -rn "IStockUpOperationRepository\|StockUpSourceType\|StockUpOperationState" src/Anela.Heblo.Application/Features/Logistics
# expected: zero matches
```

## Notes

All 4 architecture-review amendments incorporated:
1. `AddTransient` (not `AddScoped`) — matches existing sibling registration
2. Emptied existing allowlist entries rather than adding a new test method
3. Enum-parity guard test in `LogisticsStockOperationQueryAdapterTests`
4. Pre-existing reverse-direction references (`LogisticsCatalogTransportSourceAdapter`, `LogisticsModule.cs`) are expected and architecture-test-allowlisted

The 32 pre-existing test failures are Docker-dependent integration tests unrelated to this change.

## PR Summary

Finishes the Logistics–Catalog module boundary decoupling started by PR #2201. That PR covered `GiftPackageManufactureService`, `ChangeTransportBoxStateHandler`, and `GetTransportBoxByCodeHandler`; this change addresses the residual leak in `TransportBoxCompletionService` which still injected Catalog-owned `IStockUpOperationRepository` directly.

Introduces a Logistics-owned query contract (`ILogisticsStockOperationQueryService`) and a Catalog-side read-only adapter (`LogisticsStockOperationQueryAdapter`) following the same consumer-owns-the-contract pattern. The `LogisticsCatalogAllowlist` in the architecture test is now empty, meaning the reflection-based boundary check actively enforces that no Logistics type references Catalog-owned namespaces.

### Changes
- `Logistics/Contracts/` — 3 new files: `ILogisticsStockOperationQueryService`, `LogisticsStockOperationState`, `Contracts/Models/LogisticsStockOperationStatus`
- `Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs` — new `internal sealed` provider-side adapter with exhaustive enum mappers
- `Catalog/CatalogModule.cs` — one `AddTransient` line for the new adapter
- `Logistics/Services/TransportBoxCompletionService.cs` — swap `IStockUpOperationRepository` for `ILogisticsStockOperationQueryService`; remove all Catalog imports
- `Tests/Architecture/ModuleBoundariesTests.cs` — empty `LogisticsCatalogAllowlist`; extend `CatalogLogisticsAllowlist` for new adapter

## Status
DONE