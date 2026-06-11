# Architecture Review: Remove unused `OrderIds` field from `PrintPickingListResult`

## Skip Design: true

Backend-only DTO cleanup. No UI, no visual components, no layout changes.

## Architectural Fit Assessment

This is a surgical dead-code removal that **reinforces** existing architecture rather than altering it. Verified against the codebase:

- `PrintPickingListResult` lives in `Anela.Heblo.Application.Features.Logistics.Picking` (relocated from Domain per the 2026-06-02 plan to satisfy Clean Architecture's dependency rule).
- The DTO is **internal to the application layer**: it is consumed only by `LogisticsExpeditionPickingAdapter`, which translates it to the cross-feature `ExpeditionPickingResult` contract owned by ExpeditionList. It is not on the OpenAPI surface (confirmed — no references in `frontend/src/api/`), not persisted, and not on any MediatR contract returned to a controller.
- The `OrderIds` field has exactly one producer-side path (`ShoptetApiExpeditionListSource.CreatePickingList` at line 227) which never sets it, and one consumer-side path (`LogisticsExpeditionPickingAdapter.CreatePickingListAsync` lines 32–36) which never reads it. Removal is mechanical.
- Aligns with the project's stated **YAGNI** and **surgical changes** principles in `CLAUDE.md`.

Repository-wide grep confirms the spec's claim: only three occurrences of `OrderIds` tied to `PrintPickingListResult` exist (definition, test arrange, and historical planning docs which are immutable artifacts and out of scope).

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ ExpeditionList Feature (Application)                                │
│   IExpeditionPickingSource ────► ExpeditionPickingResult            │
│              ▲                     { ExportedFiles, TotalCount }    │
│              │ implemented by                                       │
└──────────────┼──────────────────────────────────────────────────────┘
               │
┌──────────────┴──────────────────────────────────────────────────────┐
│ Logistics.Infrastructure (Application) — bridge                     │
│   LogisticsExpeditionPickingAdapter                                 │
│     ├─ delegates to IPickingListSource                              │
│     └─ maps PrintPickingListResult ──► ExpeditionPickingResult      │
│          { ExportedFiles, TotalCount[, OrderIds ✂ DELETE] }         │
└──────────────┬──────────────────────────────────────────────────────┘
               │ implemented by
┌──────────────┴──────────────────────────────────────────────────────┐
│ Adapters.ShoptetApi                                                 │
│   ShoptetApiExpeditionListSource.CreatePickingList                  │
│     returns new PrintPickingListResult { ExportedFiles, TotalCount }│
└─────────────────────────────────────────────────────────────────────┘
```

The cross-layer contract (`ExpeditionPickingResult`) never carried `OrderIds`. The deletion only narrows the inner DTO to match what producers actually populate and what the adapter actually consumes.

### Key Design Decisions

#### Decision 1: Delete the field outright rather than wire it through
**Options considered:**
1. Delete `OrderIds` from `PrintPickingListResult` and the test arrange line.
2. Populate `OrderIds` in `ShoptetApiExpeditionListSource` and add a matching field to `ExpeditionPickingResult`.
3. Mark `[Obsolete]` and defer removal.

**Chosen approach:** Option 1 — delete.

**Rationale:** No production caller requests `OrderIds`. There is no feature, telemetry need, or downstream consumer pulling for it. Option 2 builds infrastructure on speculation (violates YAGNI). Option 3 adds noise with no path to actual removal because this is an internal type with one producer and one consumer — there is no external contract to deprecate gracefully. Delete is reversible: the field can be re-added when a real consumer arrives, with the producer wired at the same time.

#### Decision 2: Do not touch `ExpeditionPickingResult`
**Options considered:** Touch the outer contract for symmetry vs. leave it alone.

**Chosen approach:** Leave `ExpeditionPickingResult` untouched.

**Rationale:** It never had `OrderIds`. The brief and spec correctly scope the change to the inner DTO. Modifying the outer contract would expand blast radius across the ExpeditionList feature for zero benefit.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify exactly two existing files:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs` | Delete line 7 (the `OrderIds` property). |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs` | Delete line 60 (the `OrderIds = new List<int> { 1, 2, 3 },` initializer in `CreatePickingListAsync_TranslatesResultFields`). |

Do **not** modify:
- `ShoptetApiExpeditionListSource.cs` — already does not reference `OrderIds`.
- `LogisticsExpeditionPickingAdapter.cs` — already does not reference `OrderIds`.
- `ExpeditionPickingResult` or any ExpeditionList contract.
- Other tests in `LogisticsExpeditionPickingAdapterTests.cs` — three tests use `new PrintPickingListResult()` with object-initializer defaults; they remain compile-clean after the property is gone.

### Interfaces and Contracts

Post-change shape of `PrintPickingListResult`:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Picking;

public class PrintPickingListResult
{
    public IList<string> ExportedFiles { get; set; } = new List<string>();
    public int TotalCount { get; set; }
}
```

`IPickingListSource.CreatePickingList` signature unchanged. `IExpeditionPickingSource.CreatePickingListAsync` unchanged. No OpenAPI client regeneration needed (the DTO is not on the API surface).

### Data Flow

Unchanged in behaviour, narrowed in shape:

```
ShoptetApiExpeditionListSource.CreatePickingList
    ├─ exportedFiles  ──┐
    └─ processedCodes.Count ──┐
                         │   │
                         ▼   ▼
        PrintPickingListResult { ExportedFiles, TotalCount }
                         │   │
                         ▼   ▼
LogisticsExpeditionPickingAdapter.CreatePickingListAsync
    └─ maps to ExpeditionPickingResult { ExportedFiles, TotalCount }
                         │
                         ▼
            ExpeditionList consumer
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A hidden consumer reads `OrderIds` somewhere not caught by grep (e.g. reflection, serialization). | LOW | Verified: no JSON/System.Text.Json/Newtonsoft attributes on the type; not exposed via controller, MediatR result, EF entity, or OpenAPI generation. Repository-wide grep confirms only the three known sites. `dotnet build` will fail-fast on any compile-time reference. |
| Other tests construct `PrintPickingListResult` with `OrderIds` set. | LOW | Verified: the other three uses in `LogisticsExpeditionPickingAdapterTests.cs` (lines 29, 89, 115) all use parameterless `new PrintPickingListResult()` — no field initializers to update. |
| A future developer reintroduces the field without a producer. | LOW | The deletion itself removes the temptation. No automated guard needed for a one-property hygiene change. |
| Concurrent in-flight branches reference `OrderIds`. | LOW | Solo developer per `CLAUDE.md`. Standard PR merge conflict resolution covers this. |

## Specification Amendments

None. The spec is correct, complete, and scoped appropriately:

- FR-1, FR-2, FR-3 acceptance criteria match the codebase reality verified above.
- NFR-2 (surgical scope) is already enforced by the file list.
- Out-of-scope list correctly excludes `ExpeditionPickingResult`, frontend, and broader dead-code sweeps.

One minor verification refinement worth recording during execution (not a spec change): when running FR-3's grep, expect to see remaining `OrderIds` matches in `docs/superpowers/plans/` (historical planning artifacts) and `frontend/src/features/grid-layout/` (unrelated `newOrderIds` callback) and an EF migration name (`ChangePurchaseOrderIdsToInt`). These are not references to `PrintPickingListResult.OrderIds` and should be ignored.

## Prerequisites

None. The change requires:

- No database migration (DTO is not persisted).
- No configuration update (no feature flag, no Key Vault secret).
- No OpenAPI client regeneration (DTO is not on the API surface; `frontend/src/api/` will be unchanged).
- No infrastructure change.
- No coordination with other in-flight work beyond standard branch hygiene.

Validation before completion (per `CLAUDE.md`):
- `dotnet build` succeeds.
- `dotnet format` produces no diff on the two edited files.
- The four tests in `LogisticsExpeditionPickingAdapterTests` all pass.