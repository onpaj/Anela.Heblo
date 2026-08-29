# Implementation: dqt-erp-stock-contract

## What was implemented
Added the DataQuality-owned read contract over ERP stock data, following the same
inbound-port pattern already established for `IDqtEshopStockSource` /
`DqtEshopStockItem`. This is a pure contract task: no adapter, no wiring, no
callers yet — it defines the DTO and interface that a later task
(`catalog-erp-stock-source-adapter`) will implement.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs` — DataQuality-owned snapshot DTO with `ProductCode`, `ProductName`, and `IsSellable` (a pre-derived boolean, deliberately replacing the raw `ProductTypeId` so that Catalog-domain `ProductType` enum knowledge stays out of the DataQuality module and lives only in the future adapter).
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs` — read-only contract with a single `ListAsync(CancellationToken)` method returning `IReadOnlyList<DqtErpStockItem>`, mirroring `IDqtEshopStockSource`.

## Tests
None required or written — this task only introduces a DTO and an interface (no
behavior to unit test). Verification is by successful compilation, matching the
precedent set by the sibling `dqt-eshop-stock-contract` task.

## How to verify
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded.` (0 errors). Ran locally — build succeeded with only
pre-existing warnings plus the same class of nullable-reference warnings already
present on the sibling `DqtEshopStockItem.cs` (non-nullable `string` properties
without `required`/initializers), consistent with existing project conventions.

## Notes
- No deviations from the task-context spec. Property names, types, and namespace
  match exactly what was specified.
- `IsSellable` is a pre-computed boolean rather than the raw `ProductTypeId`, per
  the explicit note in the task context — this keeps the `ProductType` enum
  comparison (Catalog domain knowledge) out of the DataQuality module.
- No other files were touched — no DI registration, no consumer wiring, and no
  adapter implementation, since those are out of scope for this contract-only task.

## PR Summary
Added the DataQuality-owned `IDqtErpStockSource` contract and its `DqtErpStockItem`
snapshot DTO, mirroring the existing `IDqtEshopStockSource` pattern. This defines
the inbound port DataQuality will use to read ERP stock data without depending on
Catalog's domain types; a later task wires up the adapter implementation.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtErpStockItem.cs` — new DTO
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs` — new contract interface

## Status
DONE
