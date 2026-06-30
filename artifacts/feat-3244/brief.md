## Module
Catalog

## Finding
`CatalogAggregate` (the Catalog domain root entity) holds a collection of `ManufactureHistoryRecord`, which is defined in the **Manufacture** module's domain:

- **File**: `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs`
- **Line 8**: `using Anela.Heblo.Domain.Features.Manufacture;`
- **Line 92**: `private IReadOnlyList<ManufactureHistoryRecord> _manufactureHistory = new List<ManufactureHistoryRecord>();`
- **Line 124**: `public IReadOnlyList<ManufactureHistoryRecord> ManufactureHistory`

`ManufactureHistoryRecord` is defined in `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureHistoryRecord.cs`.

## Why it matters
`development_guidelines.md` does not allow "Direct access to another module's entities." This is a coupling at the deepest level — two domain models from separate modules are fused in the same type system. Even if the fields are identical today, any future change to `ManufactureHistoryRecord` (adding a field, renaming a property, changing nullability) forces a coordinated change in the Catalog domain. It also prevents the two modules from being independently compiled or deployed (a stated future goal per the guidelines). The Architecture test suite (`ModuleBoundariesTests`) cannot currently catch this because both types live in the `Domain` project, making the dependency invisible to namespace-based checks.

## Suggested fix
Move the data the Catalog aggregate needs into its own type in the Catalog domain. Since `ManufactureHistoryRecord` is a simple data record (Date, Amount, PricePerPiece, PriceTotal, ProductCode, DocumentNumber) and Catalog only ever reads it as historical data, the minimal fix is:

1. Create `backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureHistory/CatalogManufactureRecord.cs` with the same fields.
2. Replace the `ManufactureHistoryRecord` usage in `CatalogAggregate` with `CatalogManufactureRecord`.
3. Update `CatalogMergeService.MergeHistoryData` to map `ManufactureHistoryRecord → CatalogManufactureRecord` at the merge boundary (Application layer — which is allowed to know about both modules).

This keeps all cross-module translation in the Application layer, where it belongs.

---
_Filed by daily arch-review routine on 2026-06-20._
