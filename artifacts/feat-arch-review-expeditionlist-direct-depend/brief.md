## Module
ExpeditionList

## Finding
`ExpeditionListService` and `PrintPickingListJob` directly import types from the **Logistics module's** `Picking` namespace:

```csharp
// ExpeditionListService.cs:1
using Anela.Heblo.Application.Features.Logistics.Picking;
// uses: IPickingListSource, PrintPickingListRequest, PrintPickingListResult

// PrintPickingListJob.cs:3
using Anela.Heblo.Application.Features.Logistics.Picking;
// uses: PrintPickingListRequest
```

`IPickingListSource` is defined in `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs` — **owned by the Logistics module**, not by ExpeditionList. `PrintPickingListRequest` and `PrintPickingListResult` are also Logistics-owned types consumed directly by ExpeditionList.

Per `docs/architecture/development_guidelines.md` (cross-module communication pattern):
> "When module A needs read-only access to data in module B, the dependency must **invert**: the consumer owns the contract, the provider implements an adapter."

Other module boundaries in the codebase follow this pattern correctly (e.g. `ILeafletKnowledgeSource` owned by Leaflet, `ILogisticsStockOperationQueryService` owned by the consumer). ExpeditionList does not.

Additionally, `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` contains no rule for `ExpeditionList → Logistics`, so this boundary violation is invisible to CI. (The reverse direction `ExpeditionListArchive → ExpeditionList` IS enforced at line 327.)

## Why it matters
- **Module coupling**: ExpeditionList can only be tested or deployed alongside Logistics. Renaming or reshaping Logistics picking types is a change that implicitly affects ExpeditionList — with no compile-time warning.
- **Boundary drift**: Without a test rule, future additions to ExpeditionList may add more Logistics imports without anyone noticing.
- **Inconsistency**: Every other enforced module pair in the codebase follows the consumer-owns-contract pattern; this is the one unchecked crossing.

## Suggested fix
1. Create `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListSource.cs` with a minimal interface owned by ExpeditionList, and matching request/result types (`ExpeditionPickingRequest`, `ExpeditionPickingResult`) with only the fields ExpeditionList actually reads.
2. Add a Logistics-side adapter (`LogisticsExpeditionListSourceAdapter`) that implements the ExpeditionList-owned interface and delegates to the existing Logistics picking internals.
3. Register the adapter in `LogisticsModule` (the provider registers the binding, per the pattern).
4. Add a `ModuleBoundaryRule` in `ModuleBoundariesTests.cs` for `ExpeditionList → Logistics` with an empty allowlist.

If the request/result types are too large to duplicate immediately, a minimal first step is just adding the boundary test with a documented allowlist — that at least makes the violations visible and stops them from growing.

---
_Filed by daily arch-review routine on 2026-06-03._