# Implementation: dqt-eshop-stock-contract

## What was implemented
Added a DataQuality-owned contract and snapshot DTO for eshop stock data, so the DataQuality module can later depend on its own abstraction instead of Catalog domain types directly. This is the first of several tasks that together eliminate the cross-module dependency in `ProductPairingDqtComparer`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs` — plain DTO class (not a record, per project convention) with `Code`, `PairCode`, `Name` properties.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs` — interface with a single `ListAsync(CancellationToken)` method returning `Task<IReadOnlyList<DqtEshopStockItem>>`.

## Tests
None. This task only adds a DTO and an interface with no logic; no test files were created or required.

## How to verify
1. `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — build succeeds with 0 errors (137 pre-existing warnings unrelated to these files, plus 3 new `CS8618` nullability warnings on the new DTO's non-nullable properties, consistent with the pattern already used by sibling DTOs such as `IngredientDto` in the Catalog module).
2. Confirm the two files exist under `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/` and contain only a `using`-free namespace declaration plus the class/interface — no references to `Anela.Heblo.Domain.Features.Catalog*`.
3. `git show --stat HEAD` on this worktree's branch shows exactly these two files added.

## Notes
- No deviations from the task spec. File contents match the task's Step 1 and Step 2 code blocks verbatim.
- Only these two files were staged and committed; an unrelated pre-existing modification to `artifacts/feat-3967/state.json` in the working tree was left untouched and unstaged, as instructed.
- Did not implement the ERP-side contract, Catalog-side adapters, DI registration, or the `ProductPairingDqtComparer` rewiring — those are separate, later tasks per the wider context.

## PR Summary
Introduces the DataQuality module's own outbound contract for eshop stock data (`IDqtEshopStockSource`) and its snapshot DTO (`DqtEshopStockItem`), both plain classes with no dependency on Catalog domain types. This lays the groundwork for a later task to rewire `ProductPairingDqtComparer` away from directly importing Catalog domain types, fixing a cross-module dependency violation.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtEshopStockItem.cs` — new DTO class (`Code`, `PairCode`, `Name`).
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs` — new interface (`ListAsync`) returning a list of `DqtEshopStockItem`.

## Status
DONE
