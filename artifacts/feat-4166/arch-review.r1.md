# Architecture Review: Extract PackingMaterialDto mapping into a shared mapper

## Skip Design: true

This is a backend-only, internal refactor — no new or changed UI components, no API contract change, no visual surface. The designer phase produces nothing actionable here and should be skipped.

## Architectural Fit Assessment

This fits cleanly as a pure internal refactor within `Anela.Heblo.Application.Features.PackingMaterials`. It touches no module boundary: `PackingMaterial` (domain entity) and `PackingMaterialDto` (application contract) both stay exactly as they are; only the code that bridges them moves from four inline sites into one.

The codebase already has two precedents for this exact problem, and they point to different solutions depending on scope:

1. **`Features/Journal/Mapping/JournalEntryMapper.cs`** — an `internal static class` in a dedicated `Mapping/` subfolder, with `public static JournalEntryDto ToDto(JournalEntry entry)`. This mapper is called from multiple places across the Journal feature.
2. **`Features/Catalog/Inventory/UseCases/CreateLot/CreateLotHandler.cs:66`** — `internal static LotDto MapToDto(Lot lot)` defined *inside* the single handler that uses it.

The distinguishing factor is fan-in: `CreateLotHandler`'s mapper has exactly one caller (itself), so a handler-local static method is proportionate. `PackingMaterialDto` construction has **four** callers across four different handler classes — the same shape of problem `JournalEntryMapper` was extracted to solve. This review adopts the `JournalEntryMapper` precedent, not the `CreateLotHandler` one: a shared, standalone mapper class, not a method embedded in one of the four handlers (embedding it in one handler would make the other three depend on that handler's internals, which is worse coupling than the status quo).

`PackingMaterialsTextHelper` (`internal static`, in `Contracts/`) is already assembly-visible to wherever the new mapper lives, since both live in `Anela.Heblo.Application`. No visibility changes needed.

No existing test in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` references `PackingMaterialDto` field-by-field except `GetPackingMaterialsListHandlerTests.cs` — that file's assertions run against the handler's response and are unaffected by where the mapping code physically lives, since the output is unchanged (see spec FR-3).

## Proposed Architecture

### Component Overview

```
Before:
  CreatePackingMaterialHandler          ──┐
  UpdatePackingMaterialHandler          ──┤  each inlines
  UpdatePackingMaterialQuantityHandler  ──┤  new PackingMaterialDto { ... }
  GetPackingMaterialsListHandler        ──┘

After:
  CreatePackingMaterialHandler          ──┐
  UpdatePackingMaterialHandler          ──┤
  UpdatePackingMaterialQuantityHandler  ──┼──▶  PackingMaterialMapper.ToDto(material, forecastedDays)
  GetPackingMaterialsListHandler        ──┘         │
                                                     ▼
                                          PackingMaterialsTextHelper.ConsumptionTypeText(...)
                                                     │
                                                     ▼
                                             PackingMaterialDto
```

### Key Design Decisions

#### Decision 1: Standalone mapper class vs. handler-local static method
**Options considered:**
- (a) `internal static` method local to one of the four handlers, called by the other three (mirrors `CreateLotHandler`'s pattern).
- (b) Extension method `PackingMaterial.ToDto(...)` as suggested in the original brief.
- (c) Standalone `internal static class PackingMaterialMapper` in a `Mapping/` subfolder (mirrors `JournalEntryMapper`).

**Chosen approach:** (c).

**Rationale:** Four call sites across four independent handler classes is a fan-in pattern this codebase has already solved once (Journal), with a specific, discoverable shape. Option (a) would make three unrelated handlers depend on a fourth's implementation detail — worse than the current duplication in terms of coupling direction, even though it removes the duplication itself. Option (b), the extension method the brief proposed, is workable but is not this codebase's convention for this exact fan-in shape (`JournalEntryMapper.ToDto` is a plain static method, not an extension) — deviating here would introduce a second style for the same kind of problem without a reason to. Option (c) matches established convention exactly, is trivially discoverable by future maintainers (one folder pattern to learn, reused), and needs no visibility changes since `PackingMaterialsTextHelper` is already assembly-internal.

#### Decision 2: Where `forecastedDays` is computed
**Options considered:**
- Move forecast computation (the `decimal.MaxValue`/`Math.Round` guard, the recent-logs lookup) into the mapper itself, so callers just pass the entity and it does everything.
- Keep forecast computation exactly where it is today (in each handler, before the mapper call), and have the mapper accept it as a plain `decimal? forecastedDays` parameter.

**Chosen approach:** Keep forecast computation in the handlers; the mapper only accepts the already-computed value.

**Rationale:** `Create` and `Update` never compute a forecast (materially different — they intentionally pass `null`, with no repository read). `UpdatePackingMaterialQuantity` and `GetPackingMaterialsList` each compute it differently — one from a single material's recent logs fetched after an update, the other from a batch-loaded `Dictionary<int, List<PackingMaterialLog>>` fetched once for the whole list, feeding into per-item counters (`withForecast`/`withoutForecast`/`totalLogs`) used in a `LogDebug` call. Folding this into the mapper would require passing the repository (or logs, or counters) through, turning a pure, testable value mapper into something with I/O-shaped concerns and handler-specific side effects (the debug counters). The spec's FR-1 already scopes the mapper to a pure `(PackingMaterial, decimal?) → PackingMaterialDto` function — this decision confirms that scope is correct and should not be widened during implementation.

## Implementation Guidance

### Directory / Module Structure
Create:
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`

Modify (replace the `new PackingMaterialDto { ... }` block in each with a call to the mapper):
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`

No changes to `Domain`, `Persistence`, `API`, or `frontend` layers. No new project references — the mapper lives in `Anela.Heblo.Application`, the same assembly as every current call site and as `PackingMaterialsTextHelper`.

### Interfaces and Contracts

```csharp
// Features/PackingMaterials/Mapping/PackingMaterialMapper.cs
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Application.Features.PackingMaterials.Mapping;

internal static class PackingMaterialMapper
{
    public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays) => new()
    {
        Id = material.Id,
        Name = material.Name,
        ConsumptionRate = material.ConsumptionRate,
        ConsumptionType = material.ConsumptionType,
        ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
        CurrentQuantity = material.CurrentQuantity,
        ForecastedDays = forecastedDays,
        CreatedAt = material.CreatedAt,
        UpdatedAt = material.UpdatedAt
    };
}
```

Each handler's call site becomes a one-line substitution, e.g. in `GetPackingMaterialsListHandler.Handle`:

```csharp
return PackingMaterialMapper.ToDto(material, displayForecast);
```

`CreatePackingMaterialHandler` and `UpdatePackingMaterialHandler` pass `null` explicitly (`PackingMaterialMapper.ToDto(createdMaterial, null)` / `PackingMaterialMapper.ToDto(material, null)`) — do not give the `forecastedDays` parameter a default value that would let these call sites omit it silently; an explicit `null` at the call site keeps the "no forecast for Create/Update" decision visible in a diff of those handlers, rather than hidden behind a default.

### Data Flow
Unchanged end-to-end: repository read/write → entity mutation (where applicable) → forecast computed inline in the handler exactly as today → `PackingMaterialMapper.ToDto(entity, forecast)` → `PackingMaterialDto` → MediatR response → controller → client. The only change is that the last mapping step is now a single named function instead of four independent literals.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future handler needs a `PackingMaterialDto` shaped differently (e.g. a lighter summary) and is tempted to bypass the mapper, reintroducing drift | Low | Not a concern for this change (out of scope per spec); if/when it happens, resolve with an overload, not a fifth inline copy |
| Refactor accidentally changes a field's value (e.g. swaps `CreatedAt`/`UpdatedAt`, or drops the `Math.Round`) | Low | Existing `GetPackingMaterialsListHandlerTests.cs` plus any Create/Update handler tests asserting response field values catch this; the planner should require a diff-only code review pass for the four handler edits given how easy an off-by-one field swap is to miss by eye |
| `PackingMaterialMapper` becomes a dumping ground for unrelated PackingMaterials mapping logic over time | Low | Out of scope to prevent now; naming it specifically `PackingMaterialMapper` (not a generic `Mappers` or `Extensions` class) keeps its single responsibility explicit |

## Specification Amendments

None. The spec's FR-1/FR-2/FR-3 are implementable as written; this review only resolves *how* (standalone mapper class per Decision 1) rather than changing *what*.

## Prerequisites

None — no migration, no config, no new infrastructure. Implementation can start immediately against the current `main`/feature branch state.
