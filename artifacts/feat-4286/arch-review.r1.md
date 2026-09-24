# Architecture Review: Extract `MarginLevelDto` Construction Duplication in Catalog

## Skip Design: true
Pure backend refactor — no new/changed UI components, screens, layouts, or endpoints. Design phase should proceed straight to a backend-only design doc (component/data-schema sections only), per `designer.md`'s own "no UI" branch.

## Architectural Fit Assessment
This aligns cleanly with existing conventions and needs no new pattern. Confirmed in-repo:

- `docs/architecture/development_guidelines.md` §"Contracts and DTOs Rules": DTOs live in each module's `Contracts/` folder and are owned by that module. `MarginLevelDto` already lives at `Catalog/Contracts/MarginLevelDto.cs` — the correct, and only sensible, home for a mapping factory that targets it.
- The same doc lists AutoMapper as "Optional, for complex mappings." This mapping is a flat 1:1 scalar copy (4 decimals, no transformation) — not complex — so a hand-written static factory is the right weight, not an AutoMapper profile.
- **Existing precedent found in-repo**: `Purchase/Contracts/PurchaseOrderHistoryDto.cs` already has exactly this shape —
  ```csharp
  public static PurchaseOrderHistoryDto FromDomain(PurchaseOrderHistory h) =>
      new() { Id = h.Id, Action = h.Action, ... };
  ```
  A static `FromDomain(...)` factory on the DTO, taking the domain source type, returning `new() { ... }`. This is not a novel pattern for this codebase — it's the second instance of an established one. The new factory should match this naming (`FromDomain`) and shape exactly, for consistency across `Contracts/` folders.
- Project rule confirmed: DTOs are classes, never records (`CLAUDE.md`) — `MarginLevelDto` already is a class; the factory doesn't change that, and generated OpenAPI/TS clients are unaffected because no public property or attribute changes.

**Correction to the originating issue.** The issue's suggested-fix snippet types the factory parameter as `MarginData`:
```csharp
public static MarginLevelDto From(MarginData d) => new() { Percentage = d.Percentage, ... };
```
This does not compile against the actual domain model. Verified by reading `backend/src/Anela.Heblo.Domain/Features/Catalog/MarginData.cs` and `MarginLevel.cs`:
- `MarginData` is the four-level *cascade container*: `{ M0, M1, M2, M3 }`, each an `M­arginLevel`. It has no `Percentage`/`Amount`/`CostLevel`/`CostTotal` properties of its own.
- `MarginLevel` is the per-level value type that actually carries `Percentage`, `Amount`, `CostTotal`, `CostLevel` (all `decimal`).
- Every one of the 12 call sites passes a `MarginLevel` (`marginHistory.Averages.M0`, `m.Value.M1`, etc.), never a `MarginData`.

The factory must be typed `MarginLevel -> MarginLevelDto`. This is corrected in the spec (NFR-3) and is binding here.

## Proposed Architecture

### Component Overview
```
Anela.Heblo.Domain.Features.Catalog.MarginLevel   (existing, unchanged)
        │  (Percentage, Amount, CostTotal, CostLevel : decimal)
        │
        ▼  MarginLevelDto.FromDomain(MarginLevel)   <-- NEW static factory
Anela.Heblo.Application.Features.Catalog.Contracts.MarginLevelDto   (existing class, +1 method)
        ▲
        │  12 call sites, replacing inline `new MarginLevelDto { ... }`
        │
   ┌────┴─────────────────────────────┬───────────────────────────────────┐
   │ GetProductMarginsHandler.cs       │ GetCatalogDetailHandler.cs         │
   │  MapToMarginDto: M0..M3 (x4)      │  GetMarginHistoryFromMargins:      │
   │  MonthlyHistory Select: M0..M3(x4)│   M0..M3 (x4)                      │
   └────────────────────────────────────┴─────────────────────────────────┘
```
No new files, no new classes, no new dependencies. One method added to an existing contract type; two existing handlers each get their inline constructions replaced by calls to it.

### Key Design Decisions

#### Decision 1: Factory location and shape
**Options considered:**
1. Static factory method `MarginLevelDto.FromDomain(MarginLevel)` on the DTO itself.
2. A `private static` helper duplicated in each handler (no cross-file dedup — only fixes 1 of the 2 files' internal duplication, not both).
3. An `IMapper`/AutoMapper `Profile` mapping `MarginLevel -> MarginLevelDto`.

**Chosen approach:** Option 1 — static `FromDomain` on `MarginLevelDto`.

**Rationale:** Matches the existing `PurchaseOrderHistoryDto.FromDomain` precedent exactly (same naming, same shape, same "static factory on the DTO in its own `Contracts/` file" placement) — this is now the established convention for a module owning a flat domain->DTO mapping, not a new one. Option 2 doesn't fully solve the issue (duplication would remain between the two handler files' private helpers). Option 3 is unjustified weight for a 4-field scalar copy and would require registering a new AutoMapper profile / configuring it in DI for zero benefit — `GetProductMarginsHandler` doesn't even inject `IMapper` today, so option 3 would add a new constructor dependency purely to replace an expression that doesn't need one.

#### Decision 2: Method name
**Options considered:** `From(MarginLevel)`, `FromDomain(MarginLevel)`, `Map(MarginLevel)`.

**Chosen approach:** `FromDomain(MarginLevel level)`.

**Rationale:** Exact naming match with the only existing precedent (`PurchaseOrderHistoryDto.FromDomain`) in this codebase. Consistent naming across `Contracts/` factories makes the pattern recognizable/greppable project-wide. (The issue's own suggested name, `From`, is overridden here in favor of matching the codebase's actual established convention — `git grep "FromDomain("` is how a future engineer will discover all such factories.)

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Modify exactly these three existing files:
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs` — add the `FromDomain` static method.
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — replace 8 inline constructions (4 in `MapToMarginDto`'s M0–M3 block, 4 in the `MonthlyHistory` `Select` lambda) with `MarginLevelDto.FromDomain(...)` calls.
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — replace 4 inline constructions in `GetMarginHistoryFromMargins`'s `Select` lambda with `MarginLevelDto.FromDomain(...)` calls.

### Interfaces and Contracts
```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs
public class MarginLevelDto
{
    // ... existing four properties, unchanged, including [JsonPropertyName] attributes ...

    public static MarginLevelDto FromDomain(MarginLevel level) => new()
    {
        Percentage = level.Percentage,
        Amount = level.Amount,
        CostLevel = level.CostLevel,
        CostTotal = level.CostTotal
    };
}
```
Requires `using Anela.Heblo.Domain.Features.Catalog;` in `MarginLevelDto.cs` (the `Contracts/` file does not currently reference the domain namespace — this is a new, narrow, one-directional dependency: `Contracts` (Application layer) depending on `Domain` is already the norm elsewhere in this module, e.g. handlers already reference `Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`).

Call-site replacement pattern (identical at all 12 sites — only the source expression changes):
```csharp
// Before:
M0 = new MarginLevelDto
{
    Percentage = marginHistory.Averages.M0.Percentage,
    Amount = marginHistory.Averages.M0.Amount,
    CostLevel = marginHistory.Averages.M0.CostLevel,
    CostTotal = marginHistory.Averages.M0.CostTotal
},

// After:
M0 = MarginLevelDto.FromDomain(marginHistory.Averages.M0),
```
Same substitution for `M1`/`M2`/`M3`, for both handlers' `Select` lambdas (`m.Value.M0` etc.), and for `GetProductMarginsHandler`'s top-level averages block.

### Data Flow
Unchanged. `CatalogAggregate.Margins` (a `MonthlyMarginHistory` containing `Averages: MarginData` and `MonthlyData: IDictionary<DateTime, MarginData>`) is read by both handlers exactly as today; only the final `MarginLevel -> MarginLevelDto` translation step is now routed through one shared method instead of being re-typed at each of the 12 sites.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent behavior change if `FromDomain` doesn't copy a field, or copies from the wrong nested value (e.g. `M1` accidentally sourced from `.M0`) at one of the 12 sites during the mechanical replacement | Low | All 12 sites are structurally identical 4-field copies (see FR-3 acceptance criteria in spec); existing test coverage for `GetProductMarginsHandler`/`GetCatalogDetailHandler` response DTOs catches any field/level mismatch. No new tests are strictly required beyond running the existing suite, but a focused unit test on `MarginLevelDto.FromDomain` itself is cheap and recommended (see planner). |
| Circular/unwanted dependency from adding a `Domain` `using` to a `Contracts/` file | Very Low | Not a new dependency direction — handlers in the same module already reference `Anela.Heblo.Domain.Features.Catalog.*` types directly; `Contracts` classes referencing `Domain` types in their mapping factories has direct precedent (`PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)`). |
| Scope creep — refactoring the *other* DTO constructions in these two files (e.g. `ManufactureCostDto`, `MonthlyMarginDto.Month`, `CatalogItemDto` via AutoMapper) while in the area | Low | Explicitly out of scope per spec; planner should scope tasks to only the 12 named call sites. |

## Specification Amendments
- NFR-3 in `spec.r1.md` already captures the `MarginData` → `MarginLevel` type correction; this review is the authoritative confirmation referenced there.
- Method name is fixed to `FromDomain` (not `From`, as the issue's snippet suggested) to match the one existing precedent in this codebase (`PurchaseOrderHistoryDto.FromDomain`). Planner and implementers must use `FromDomain`.

## Prerequisites
None. No migrations, config, or infrastructure changes. Implementation can start immediately against the current `main`/feature branch state.
