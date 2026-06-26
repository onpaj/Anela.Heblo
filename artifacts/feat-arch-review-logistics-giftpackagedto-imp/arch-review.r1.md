I have sufficient context. Writing the architecture review now.

# Architecture Review: Remove Logistics → Purchase Module Coupling in GiftPackageDto

## Skip Design: true

No new UI surface area. Existing components keep rendering identically; only the imported enum symbol changes (and possibly a small cleanup of dead filter buttons — see Specification Amendments).

## Architectural Fit Assessment

The proposal aligns with the codebase's established Vertical Slice + module-isolation pattern documented in `docs/architecture/development_guidelines.md` and proven by the recent Leaflet → KnowledgeBase decoupling. Each module owns its contracts; cross-module type leakage is treated as a defect.

The fix is a textbook "owned-contract" remediation: relocate the enum into the consuming module and break the `using` edge. There is no shared-abstraction temptation here — the type is genuinely module-local (severity classification rules differ per module: Purchase has six buckets, Manufacture has its own five-bucket `ManufacturingStockSeverity`, Logistics needs three). The spec correctly rejects a "shared severity" common namespace.

**Two material gaps in the spec that this review must address:**

1. **Wire/serialization claim is wrong but harmless.** The spec says ordinal integer values must match Purchase's `StockSeverity` for "in-flight client" compatibility. They don't: Purchase has `Critical=0, Severe=1, Low=2, Optimal=3, ...` while the proposed Logistics enum `{Optimal, Severe, Critical}` yields `Optimal=0, Severe=1, Critical=2`. **However**, `JsonStringEnumConverter` is globally registered (`backend/src/Anela.Heblo.API/Program.cs:118`) and the generated TS client confirms string-valued enums (`Critical = "Critical"`). Wire format depends on enum **member names**, not ordinals. As long as names (`Critical`, `Severe`, `Optimal`) are preserved, the wire is stable. Severity is also computed on the fly and never persisted to the DB (`GiftPackageManufactureLog` does not store it), so ordinals are entirely a no-op.
2. **Frontend scope is understated.** Spec mentions only `CriticalGiftPackagesTile` (which is actually a **backend** tile at `backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs:54`, not a frontend component). The real frontend consumers are `frontend/src/components/pages/GiftPackageManufacturing/GiftPackageManufacturingList.tsx` and `GiftPackageManufacturingSummary.tsx`, which currently reference six `StockSeverity` values (`Critical`, `Severe`, `Low`, `Optimal`, `Overstocked`, `NotConfigured`) for filter buttons — even though the backend only ever emits three. See Specification Amendments.

## Proposed Architecture

### Component Overview

```
Before (violation):
  ┌──────────────────────────────────────┐
  │  Features/Logistics                  │
  │   GiftPackageDto.Severity ─────────┐ │
  │   GiftPackageManufactureService ───┼─┼──► Features/Purchase
  │   CriticalGiftPackagesTile ────────┘ │       StockSeverity (6 values)
  └──────────────────────────────────────┘

After (decoupled):
  ┌──────────────────────────────────────┐    ┌────────────────────────┐
  │  Features/Logistics                  │    │  Features/Purchase     │
  │   .../GiftPackageManufacture/        │    │   StockSeverity        │
  │     Contracts/                       │    │   (unchanged, 6 values)│
  │       GiftPackageDto                 │    └────────────────────────┘
  │       GiftPackageSeverity ◄──┐       │
  │   .../Services/              │       │
  │     GiftPackageManufactureService    │
  │   DashboardTiles/                    │
  │     CriticalGiftPackagesTile         │
  └──────────────────────────────────────┘
  + backend/test/.../Architecture/ModuleBoundariesTests
    extended with Logistics→Purchase forbidden-namespace assertion.
```

### Key Design Decisions

#### Decision 1: Where the new enum lives
**Options considered:**
- (a) `Features/Logistics/Contracts/GiftPackageSeverity.cs` (spec's proposal — module-root).
- (b) `Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageSeverity.cs` (co-located with the DTO that owns it).
- (c) Inside `GiftPackageDto.cs` as a peer type (Purchase's pattern — single file).

**Chosen approach:** (b) — co-locate with `GiftPackageDto`.

**Rationale:** The enum is used exclusively by the `GiftPackageManufacture` use case (DTO, service, dashboard tile). Vertical Slice cohesion says "all code for a feature in one place" (`development_guidelines.md`). The module-root `Features/Logistics/Contracts/` folder today holds **only** TransportBox DTOs that are shared across multiple Logistics use cases; gift-package severity is not in that league. (c) is rejected because Purchase's "everything-in-the-response-file" pattern is recognized in the brief as the *thing being fixed* elsewhere, and a standalone file makes the enum easier to find and grep for.

#### Decision 2: Enum membership — three values or six
**Options considered:**
- (a) Three values `{Optimal, Severe, Critical}` matching what `CalculateSeverity` currently emits (spec's proposal).
- (b) Six values mirroring Purchase's `StockSeverity` `{Critical, Severe, Low, Optimal, Overstocked, NotConfigured}` to keep frontend filter UI unchanged.

**Chosen approach:** (a) — three values, **and** also clean up the now-dead filter buttons in `GiftPackageManufacturingSummary.tsx`.

**Rationale:** The backend service has only ever emitted three severities for gift packages. The extra filter buttons for `Low`, `Overstocked`, `NotConfigured` in `GiftPackageManufacturingSummary.tsx` filter against values the backend never produces — they are dead UI. Carrying them forward "for compatibility" propagates the leaked Purchase model into the Logistics module conceptually, which defeats the point of decoupling. Truth-in-typing wins. This adds frontend cleanup scope; see Specification Amendments FR-4'.

#### Decision 3: Enforce the boundary in CI
**Options considered:**
- (a) Rely on code review and discipline.
- (b) Extend `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a Logistics→Purchase forbidden-namespace assertion mirroring the existing Leaflet→KnowledgeBase test.

**Chosen approach:** (b).

**Rationale:** The codebase already invested in a reflection-based architecture test for the Leaflet decoupling; reusing the same pattern here is cheap, prevents the next regression, and signals that module boundaries are first-class. Without it, this exact violation can reappear at the next "just import the type" PR.

#### Decision 4: Wire/JSON compatibility strategy
**Chosen approach:** Rely on the globally registered `JsonStringEnumConverter` (verified at `Program.cs:118`). Preserve member **names** (`Optimal`, `Severe`, `Critical`). Ordinals do not matter for the wire and are not persisted.

**Rationale:** Members `Critical`/`Severe`/`Optimal` exist verbatim in both old and new enums, so any in-flight client deserializing `"severity":"Critical"` still works against the regenerated client. The spec's ordinal-matching requirement (FR-1) should be reworded to a name-matching requirement (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure

**Create:**
```
backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/
  GiftPackageSeverity.cs                  (new — 3-member enum)
```

**Modify:**
```
backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/
  Contracts/GiftPackageDto.cs             (drop Purchase using; retype Severity)
  Services/GiftPackageManufactureService.cs   (drop Purchase using; lines 338, 343, 349, 352)
backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/
  CriticalGiftPackagesTile.cs             (drop Purchase using; line 54)
backend/test/Anela.Heblo.Tests/Architecture/
  ModuleBoundariesTests.cs                (add Logistics→Purchase test)
frontend/src/components/pages/GiftPackageManufacturing/
  GiftPackageManufacturingList.tsx        (re-target StockSeverity → GiftPackageSeverity)
  GiftPackageManufacturingSummary.tsx     (re-target + remove dead Low/Overstocked/NotConfigured buttons)
```

**Do not touch:** any file under `Features/Purchase/**`, any file under `Features/Manufacture/**`, `usePurchaseStockAnalysis.ts`, `PurchaseStockAnalysis.tsx`, `ManufacturingStockAnalysis.tsx`.

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

public enum GiftPackageSeverity
{
    Critical,
    Severe,
    Optimal,
}
```

- File name matches the type (per C# coding style).
- No `[JsonConverter]` attribute needed — global `JsonStringEnumConverter` covers all enums.
- DTO stays a `class` (per project rule). Its `Severity` property becomes `public GiftPackageSeverity Severity { get; set; }`.

### Data Flow

For both `GET available gift packages` and `GET gift package detail`:

```
Catalog data ──► GiftPackageManufactureService.CalculateSeverity
                   ├─ availableStock < overstockMinimal  → GiftPackageSeverity.Critical
                   ├─ availableStock < suggestedQuantity → GiftPackageSeverity.Severe
                   └─ otherwise                          → GiftPackageSeverity.Optimal
                 │
                 ▼
              GiftPackageDto.Severity (GiftPackageSeverity)
                 │
                 ▼ JsonStringEnumConverter
              HTTP response: { ..., "severity": "Critical" }
                 │
                 ▼ regenerated OpenAPI client (string enum)
              frontend GiftPackageSeverity.Critical
                 │
                 ▼
              GiftPackageManufacturingList / Summary / Tile UI
```

For the dashboard tile:
```
CriticalGiftPackagesTile.LoadDataAsync
  → MediatR Send(GetAvailableGiftPackagesRequest)
  → response.GiftPackages.Count(p => p.Severity == GiftPackageSeverity.Critical)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated TS schema renames `StockSeverity` → `GiftPackageSeverity`, but if NSwag is keyed by C# type name, a *different* unrelated DTO with a `StockSeverity` property could see the schema collapse. | Low | Quickly grep generated `api-client.ts` after regen: confirm `StockSeverity` enum still exists for Purchase, and a new `GiftPackageSeverity` enum appears. The fact that `ManufacturingStockSeverity` already coexists with `StockSeverity` confirms NSwag handles parallel enums fine. |
| Frontend filter buttons for the three dropped values (`Low`, `Overstocked`, `NotConfigured`) are referenced by tests, deep links, or URL query params. | Low | Grep `frontend/` for those literal strings before deleting. If a saved URL uses `?severity=Low`, the filter just defaults to "All" — acceptable. |
| The new architecture test catches an existing, unrelated Logistics→Purchase reference (besides `StockSeverity`) and turns red. | Medium | Run the test once after writing it but **before** the cleanup commit. If it flags other violations, surface them in the PR and either fix in scope or add to an explicit `Allowlist` with justification (mirror the Leaflet allowlist pattern). |
| Stale `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` left in untouched Logistics files. | Low | `dotnet format` will remove unused usings; the new architecture test will fail loudly if a reference remains. |
| `dotnet format` on the new enum file produces a stylistic difference from existing enums. | Low | Run `dotnet format` as part of the PR per CLAUDE.md validation step. |

## Specification Amendments

1. **FR-1 (member ordering):** Replace the requirement *"The member ordering and underlying integer values match Purchase's `StockSeverity` so that any existing persisted/serialized values remain numerically equivalent."* with: *"Members are named `Critical`, `Severe`, `Optimal`. Names match the strings emitted on the wire by Purchase's enum for the same values, ensuring JSON wire compatibility under the globally registered `JsonStringEnumConverter`. Underlying ordinal values are unconstrained because severity is computed on-the-fly and never persisted."* Member order in the file should be `Critical, Severe, Optimal` (severity-descending), matching Purchase's stylistic ordering and `CalculateSeverity`'s return order.
2. **FR-1 (location):** Change file path from `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/GiftPackageSeverity.cs` to `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageSeverity.cs`. Co-located with `GiftPackageDto`, matching vertical-slice ownership of the use case. Update the namespace accordingly to `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts`.
3. **FR-4 (frontend scope):** Expand from "`CriticalGiftPackagesTile`" (which is a backend file) to the actual frontend touch list: `GiftPackageManufacturingList.tsx`, `GiftPackageManufacturingSummary.tsx`. Add an explicit subtask: **remove the dead filter buttons for `Low`, `Overstocked`, `NotConfigured` in `GiftPackageManufacturingSummary.tsx`** — these reference Purchase-only severity buckets the gift-package backend never emits. Acceptance criterion: the filter UI shows exactly `All`, `Critical`, `Severe`, `Optimal`.
4. **New FR-8 (architectural test):** Add the following requirement: *"Extend `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a new `[Fact]` `Logistics_types_should_not_reference_Purchase_owned_namespaces` that asserts no type whose namespace starts with `Anela.Heblo.Application.Features.Logistics` references any type in `Anela.Heblo.Application.Features.Purchase`, `Anela.Heblo.Domain.Features.Purchase`, or `Anela.Heblo.Persistence.Purchase`. Mirror the existing Leaflet test's structure including the `Allowlist` mechanism (initially empty). The test must pass once the refactor is complete."*
5. **NFR-3 clarification:** Reword to reference string-enum wire format, not ordinal compatibility (see amendment 1).
6. **Backend tile correction (background section):** The bullet describing "a frontend tile component (`CriticalGiftPackagesTile`)" is incorrect — that class is a backend `ITile` registered for the dashboard MediatR pipeline, not a React component. Update the wording.

## Prerequisites

- None blocking. No infrastructure, no migrations, no config, no new packages.
- Confirm OpenAPI client auto-regenerates on `npm run build` (per `docs/development/api-client-generation.md`) — already standing project capability.
- Existing `ModuleBoundariesTests.cs` is the template for the new assertion — no additional test harness work needed.