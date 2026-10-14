## Module
Analytics

## Finding
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`, line 32:

```csharp
// Note: IMarginCalculationService is registered by CatalogModule and injected here
```

No Analytics handler or service actually injects `IMarginCalculationService`. That interface is owned by `Anela.Heblo.Domain.Features.Catalog.Services` and is only used within the Catalog module (`CatalogRepository`, `MarginCalculationService`). Analytics uses its own `IMarginCalculator` (defined in `Analytics/Services/MarginCalculator.cs`) which is registered by `AnalyticsModule` itself.

## Why it matters
The comment implies Analytics has an undeclared runtime dependency on `CatalogModule` for `IMarginCalculationService`. A developer tracing the dependency chain will waste time looking for where this injection occurs, or may incorrectly assume that removing/reordering module registrations would break Analytics. It also misrepresents the module boundary — Analytics is actually self-contained for its margin calculation logic.

## Suggested fix
Remove line 32 entirely:

```csharp
// Note: IMarginCalculationService is registered by CatalogModule and injected here
```

If the cross-module wiring was intentional at some point and has since been replaced by `IMarginCalculator`, the removal is correct and sufficient. No code change needed.

---
_Filed by daily arch-review routine on 2026-09-19._
