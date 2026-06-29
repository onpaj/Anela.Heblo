## Module
Catalog

## Finding
`backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` lines 244–270 contain two large commented-out `RegisterRefreshTask` blocks for `ISalesCostCalculationService` and `IManufactureCostCalculationService` that were replaced by the new cost-source architecture:

```csharp
// COMMENTED OUT - Old services replaced by new cost source architecture
// services.RegisterRefreshTask(
//     ...  (25 lines)
// );

// COMMENTED OUT - Old services replaced by new cost source architecture
// services.RegisterRefreshTask(
//     ...  (15 lines)
// );
```

The comment itself states these are dead — &#34;replaced by new cost source architecture.&#34;

## Why it matters
`CatalogModule.cs` is already a dense 321-line registration file with 18+ `RegisterRefreshTask` calls and 30+ `AddScoped`/`AddTransient` registrations. Forty lines of commented-out dead code in the middle of this file:
- Increases the cognitive load when reading or modifying the registration sequence.
- References removed interfaces (`ISalesCostCalculationService`, `IManufactureCostCalculationService`) that no longer exist, which confuses readers about what services are actually active.
- If git history is the source of truth for &#34;what was here before,&#34; comments are redundant; the diff already records the removal.

## Suggested fix
Delete lines 244–270. The git history preserves the old registration code if it ever needs to be referenced. No test or runtime change is needed — the code is already unreachable.

---
_Filed by daily arch-review routine on 2026-06-28._
