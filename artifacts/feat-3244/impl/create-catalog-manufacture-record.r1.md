# Implementation: create-catalog-manufacture-record

## What was implemented

Created a new domain class `CatalogManufactureRecord` in the `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory` namespace. This is a plain class (not a record) with six properties mirroring the `ManufactureHistoryRecord` field set: `Date`, `Amount`, `PricePerPiece`, `PriceTotal`, `ProductCode`, and `DocumentNumber`. No `SupplierId`/`SupplierName` fields (manufacture-specific, not purchase-specific).

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs` — new domain type representing a single manufacture history record for a catalog product

## Tests

No tests required for this task — it is a pure data-holder class with no logic. The build verification confirmed the type compiles cleanly within the Domain project.

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj -v quiet
# Expect: Build succeeded. 0 Warning(s). 0 Error(s).
```

## Notes

- The directory `ManufactureHistory/` did not previously exist and was created as part of this task.
- The class is structured exactly as specified, using `class` rather than `record` per the project rule about types that cross API/client generation boundaries.
- The Domain project itself has 0 warnings on build; the warnings visible in output came from a referenced project (`Anela.Heblo.Xcc`) and are pre-existing.

## PR Summary

Adds `CatalogManufactureRecord`, the domain-layer DTO for manufacture history entries within the Catalog feature. It follows the same pattern as `CatalogPurchaseRecord` in the adjacent `PurchaseHistory/` directory and exposes the six fields present on `ManufactureHistoryRecord` (excluding supplier fields that are purchase-specific).

### Changes

- `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs` — new file; domain class for catalog manufacture history records

## Status
DONE
