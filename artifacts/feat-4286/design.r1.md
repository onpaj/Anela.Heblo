# Design: Extract `MarginLevelDto` Construction Duplication in Catalog

## Component Design

### `MarginLevelDto` (modified)
`backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs`

Responsibility: unchanged (carries M0–M3 margin-level data over the wire), plus one new responsibility — owning the mapping from the domain `MarginLevel` value to itself.

New member:

```csharp
public static MarginLevelDto FromDomain(MarginLevel level) => new()
{
    Percentage = level.Percentage,
    Amount = level.Amount,
    CostLevel = level.CostLevel,
    CostTotal = level.CostTotal
};
```

Interface contract:
- Input: one non-null `Anela.Heblo.Domain.Features.Catalog.MarginLevel`. Callers always pass an already-materialized `MarginLevel` (never null in current call sites — `MarginData.M0..M3` default to `MarginLevel.Zero`, never null); the method does not need to null-check.
- Output: a new `MarginLevelDto` instance, all four fields copied verbatim, no rounding/transformation (the domain type already carries final rounded `decimal` values — see `MarginLevel.Create`).
- Pure function, no side effects, no I/O, no exceptions thrown under normal use.

### `GetProductMarginsHandler` (modified, no structural change)
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`

`MapToMarginDto(CatalogAggregate product)` keeps its existing structure and responsibility (map one `CatalogAggregate` to one `ProductMarginDto`). Its 8 inline `MarginLevelDto` constructions (4 for the M0–M3 averages block, 4 inside the `MonthlyHistory` `Select` lambda) are replaced by calls to `MarginLevelDto.FromDomain(...)`. No method signatures change; no new methods added.

### `GetCatalogDetailHandler` (modified, no structural change)
`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

`GetMarginHistoryFromMargins(CatalogAggregate catalogItem, int monthsBack)` keeps its existing structure and responsibility (project margin history to `List<MarginHistoryDto>`). Its 4 inline `MarginLevelDto` constructions inside the `Select` lambda are replaced by calls to `MarginLevelDto.FromDomain(...)`. No method signature changes; no new methods added.

### Component boundaries
```
Domain layer                     Application layer (Catalog module)
──────────────                   ───────────────────────────────────
MarginLevel  ───────────────►    MarginLevelDto.FromDomain(MarginLevel)
 (Percentage, Amount,                   │
  CostTotal, CostLevel)                 │ used by
                                         ▼
                          GetProductMarginsHandler.MapToMarginDto
                          GetCatalogDetailHandler.GetMarginHistoryFromMargins
```
No new module boundary is crossed: `Contracts/MarginLevelDto.cs` already sits in the Application layer's Catalog module and is already the shared contract both handlers depend on. The only new coupling is `MarginLevelDto.cs` gaining a `using Anela.Heblo.Domain.Features.Catalog;` for the `MarginLevel` parameter type — consistent with how the rest of the Catalog module's Application layer already references its own Domain types.

## Data Schemas

No schema changes — no database, no API request/response shape changes, no event payloads.

For reference, the JSON shape emitted by `MarginLevelDto` (via its existing `[JsonPropertyName]` attributes) is unchanged before and after this refactor:

```json
{
  "percentage": 0.0,
  "amount": 0.0,
  "costLevel": 0.0,
  "costTotal": 0.0
}
```

`GetProductMarginsResponse.Items[].M0..M3`, `GetProductMarginsResponse.Items[].MonthlyHistory[].M0..M3`, and `GetCatalogDetailResponse.HistoricalData.MarginHistory[].M0..M3` all continue to serialize to exactly this shape, byte-for-byte, since only the C#-side construction mechanism changes (object initializer → factory call), not the values or the type.
