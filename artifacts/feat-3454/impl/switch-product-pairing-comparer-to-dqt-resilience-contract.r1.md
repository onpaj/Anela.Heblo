# Implementation: switch-product-pairing-comparer-to-dqt-resilience-contract

## What was implemented
Switched `ProductPairingDqtComparer`'s constructor dependency from the Catalog-owned `ICatalogResilienceService` to the new DataQuality-owned `IDqtResilienceService`, and updated the existing unit test's mock type to match. This is a pure type substitution — no method body or test assertion changes, since `IDqtResilienceService.ExecuteWithResilienceAsync<T>` has an identical signature to `ICatalogResilienceService.ExecuteWithResilienceAsync<T>`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` — replaced the `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` import with `using Anela.Heblo.Application.Features.DataQuality.Contracts;`, and changed the `_resilienceService` field type and constructor parameter type from `ICatalogResilienceService` to `IDqtResilienceService`. `CompareAsync` and `IsSellable` were left unchanged.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` — replaced the same `using` directive and changed `_resilienceMock` from `Mock<ICatalogResilienceService>` to `Mock<IDqtResilienceService>`. Constructor call in `CreateSut()`, all 5 test methods, and all assertions were left unchanged.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` — covers: `CompareAsync_ReturnsEmpty_WhenAllProductsPaired`, `CompareAsync_ReturnsMissingInErp_WhenShoptetProductNotInErp`, `CompareAsync_ReturnsMissingInErpAndPairCodeUnresolved_WhenPairCodeNotInErp`, `CompareAsync_ReturnsMissingInShoptet_OnlyForSellableErpProducts`, `CompareAsync_WrapsBothListCalls_WithResilience`. All 5 passed after the change.

## How to verify
1. `cd /home/user/worktrees/feature-3454-Arch-Review-Dataquality-Productpairingdqtcomparer`
2. `dotnet build Anela.Heblo.sln` — builds with 0 errors (253 pre-existing nullable-reference warnings unrelated to this change; no warning about an unused Catalog.Infrastructure using in either modified file).
3. `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ProductPairingDqtComparerTests" --no-build` — result: Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5.
4. `grep -rn "using Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/` — returns no matches, confirming the DataQuality Services folder no longer references the Catalog Application-layer namespace.

## Notes
No deviations from the task spec. All 8 steps executed exactly as specified. `IEshopStockClient`/`IErpStockClient` remain Domain-layer types and are unaffected by this change, consistent with the spec's boundary claim.

## PR Summary
This change completes the migration of `ProductPairingDqtComparer` off the Catalog-owned `ICatalogResilienceService` onto the new DataQuality-owned `IDqtResilienceService` contract (introduced in a prior task in this series). Since the two interfaces share an identical `ExecuteWithResilienceAsync<T>` signature, this is a mechanical type substitution: the `using` directive for `Anela.Heblo.Application.Features.Catalog.Infrastructure` was replaced with one for `Anela.Heblo.Application.Features.DataQuality.Contracts` in both the comparer and its test, the field/constructor parameter/mock type were retyped to `IDqtResilienceService`, and no other logic changed. The build is clean (0 errors) and all 5 existing unit tests pass unmodified. A grep confirms the DataQuality `Services/` folder no longer imports the Catalog Application-layer namespace, reinforcing the module boundary this refactor establishes.

### Changes
- `ProductPairingDqtComparer.cs`: swapped resilience service dependency type.
- `ProductPairingDqtComparerTests.cs`: swapped mock type to match.

## Status
DONE
