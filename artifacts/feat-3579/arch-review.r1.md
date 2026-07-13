# Architecture Review: Relocate Purchase stock-analysis enums to `Contracts/`

## Skip Design: true

No new or changed UI component, screen, layout, or visual decision is involved. This is a
compile-time C# namespace/file reorganization confined to `Anela.Heblo.Application` (three enum
declarations moved, six production `using` directives updated, three test files' `using`
directives updated). Verified: `StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy`
are serialized by NSwag purely by type name, not namespace — confirmed by inspecting the
generated client (`frontend/src/api/generated/api-client.ts:37810` `export enum StockSeverity`,
`:37938` `StockStatusFilter`, `:37947` `StockAnalysisSortBy`). The generated TypeScript is
therefore unaffected and no frontend file needs to change. Design review adds nothing here.

## Architectural Fit Assessment

This is not a new architectural pattern — it is a correction that brings the Purchase module back
into compliance with a convention the codebase already documents and already follows elsewhere.
`docs/architecture/filesystem.md` explicitly defines `Features/{Feature}/Contracts/` as the home
for "Shared DTOs across use cases," and `docs/architecture/development_guidelines.md` states
"Feature autonomy: Each feature manages its own contracts, services, and infrastructure." The
existing `Features/Purchase/Contracts/` folder already holds 12 files, each with exactly one
public type and a file-scoped namespace (e.g. `MaterialProductType.cs`, `SupplierDto.cs`,
`MaterialInfo.cs`) — a clean precedent for the three new enum files.

The bug being fixed: `Services/IStockSeverityCalculator.cs`, `Services/StockSeverityCalculator.cs`,
and `DashboardTiles/LowStockEfficiencyTile.cs` currently `using
Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` solely to reach
`StockSeverity` / `StockStatusFilter`. `Services/` and `DashboardTiles/` sit architecturally above
(and are shared by) individual use cases — a `UseCases/{X}/` folder should never be a dependency
target for its siblings, only `Contracts/` should. `GetPurchaseStockAnalysisHandler.cs` already
has `using Anela.Heblo.Application.Features.Purchase.Contracts;` for other Purchase DTOs, so
`Contracts/` is already the established, load-bearing namespace for this use case — the enums are
the only holdouts.

**Precedent worth flagging, not fixing here:** the Manufacture module has the identical
anti-pattern — `ManufacturingStockSeverity` is defined inside
`Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisResponse.cs` and
`Services/IManufactureSeverityCalculator.cs` reaches it via `using
Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;`. The spec explicitly
scopes this out, and this review agrees that stays out of scope — Manufacture's `Contracts/`
folder does not yet exist and creating it is a larger, separate unit of work. Recording it here so
a future arch-review pass doesn't need to rediscover it.

**No compiler/test enforcement gap introduced or closed:** `ModuleBoundariesTests.cs` (the
existing reflection-based boundary test) only checks *inter-module* namespace references (e.g.
Purchase → Catalog), not *intra-module* layering (Services/DashboardTiles → sibling UseCases). This
refactor fixes the current instance by convention, not by adding a new enforced rule — see Risks.

## Proposed Architecture

### Component Overview

```
Features/Purchase/
├── Contracts/                              (existing folder, gains 3 files)
│   ├── StockSeverity.cs            [NEW]   enum, namespace ...Purchase.Contracts
│   ├── StockStatusFilter.cs        [NEW]   enum, namespace ...Purchase.Contracts
│   ├── StockAnalysisSortBy.cs      [NEW]   enum, namespace ...Purchase.Contracts
│   └── ... (12 existing files, unchanged)
├── UseCases/GetPurchaseStockAnalysis/
│   ├── GetPurchaseStockAnalysisRequest.cs   enum bodies removed, adds using Contracts
│   ├── GetPurchaseStockAnalysisResponse.cs  enum body removed, adds using Contracts
│   └── GetPurchaseStockAnalysisHandler.cs   unchanged (already uses Contracts)
├── Services/
│   ├── IStockSeverityCalculator.cs          using UseCases.* → using Contracts
│   └── StockSeverityCalculator.cs           using UseCases.* → using Contracts
└── DashboardTiles/
    └── LowStockEfficiencyTile.cs            keeps using UseCases.* (needs
                                              GetPurchaseStockAnalysisRequest) AND
                                              adds using Contracts (needs StockStatusFilter)

Dependency direction after the change:
  UseCases/GetPurchaseStockAnalysis  ──depends on──▶  Contracts
  Services                           ──depends on──▶  Contracts
  DashboardTiles                     ──depends on──▶  Contracts  (+ still depends on the
                                                        specific UseCase it drives, which is
                                                        fine: a dashboard tile invoking a
                                                        MediatR request legitimately depends
                                                        on that request's shape)
```

The important structural change is that `Services` and `DashboardTiles` no longer point *into* a
`UseCases/{X}/` folder for a type; they point at `Contracts/`, same as every other cross-cutting
Purchase DTO.

### Key Design Decisions

#### Decision 1: One enum per file vs. one grouped file
**Options considered:**
(a) A single `Contracts/StockAnalysisEnums.cs` holding all three enums.
(b) One file per enum: `StockSeverity.cs`, `StockStatusFilter.cs`, `StockAnalysisSortBy.cs`.

**Chosen approach:** (b), matching the spec.

**Rationale:** Every existing file in `Features/Purchase/Contracts/` holds exactly one public
type (verified by inspection: `MaterialProductType.cs` → one enum, `SupplierDto.cs` → one class,
etc.). Grouping would be a new, unrequested convention introduced in the same PR as a
"pure relocation, no logic changes" — inconsistent with the surgical-change instruction. One file
per enum costs nothing and keeps `git blame`/history clean per type.

#### Decision 2: Whether to keep the use-case `using` in `LowStockEfficiencyTile.cs`
**Options considered:**
(a) Replace the `UseCases.GetPurchaseStockAnalysis` using entirely with `Contracts`.
(b) Keep both usings.

**Chosen approach:** (b) — verified necessary by reading the file: `LowStockEfficiencyTile.cs`
constructs `new GetPurchaseStockAnalysisRequest { StockStatus = StockStatusFilter.All, ... }`.
`GetPurchaseStockAnalysisRequest` stays in the use-case namespace (it is not being relocated —
only the enums move); only `StockStatusFilter` moves to `Contracts`. Dropping the use-case using
would break the build on `GetPurchaseStockAnalysisRequest`.

**Rationale:** A dashboard tile invoking a specific MediatR request legitimately needs to name
that request's type — that dependency is intentional and out of scope for this refactor. Only the
enum reference was the misplaced dependency; the request-type reference is correct as-is.

#### Decision 3: Scope boundary against the Manufacture module's identical issue
**Options considered:**
(a) Fix `ManufacturingStockSeverity` in the same PR for consistency.
(b) Leave Manufacture untouched, as the spec states.

**Chosen approach:** (b).

**Rationale:** Manufacture has no `Contracts/` folder today (confirmed: `Features/Manufacture/`
has no `Contracts` directory), so fixing it is not a same-shaped follow-up — it requires first
deciding whether/how to introduce one, a separate design decision. Bundling it here would violate
the "surgical changes" rule and inflate a zero-risk PR into a mixed-risk one. Flagged for a future
arch-review finding instead (see Architectural Fit Assessment above).

## Implementation Guidance

### Directory / Module Structure

New files, each with file-scoped namespace `Anela.Heblo.Application.Features.Purchase.Contracts`,
matching the exact style of `MaterialProductType.cs`:

- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs`

No new folders. No `PurchaseModule.cs` change (enums are not DI-registered types).

### Interfaces and Contracts

No interface signatures change. Enum member names, order, and implicit `int` values are
byte-for-byte preserved:

```csharp
// Contracts/StockSeverity.cs
public enum StockSeverity { Critical, Low, Optimal, Overstocked, NotConfigured }

// Contracts/StockStatusFilter.cs
public enum StockStatusFilter { All, Critical, Low, Optimal, Overstocked, NotConfigured }

// Contracts/StockAnalysisSortBy.cs
public enum StockAnalysisSortBy { ProductCode, ProductName, AvailableStock, Consumption, StockEfficiency, LastPurchaseDate }
```

`using` directive changes required (verified against current file contents):

| File | Change |
|---|---|
| `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` | remove `enum StockSeverity` body (currently lines 96–103); add `using Anela.Heblo.Application.Features.Purchase.Contracts;` |
| `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs` | remove `enum StockStatusFilter` and `enum StockAnalysisSortBy` bodies (currently lines 30–48); add `using Anela.Heblo.Application.Features.Purchase.Contracts;` |
| `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` | no change — already has `using Anela.Heblo.Application.Features.Purchase.Contracts;` at line 1 |
| `Services/IStockSeverityCalculator.cs` | replace `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` with `using Anela.Heblo.Application.Features.Purchase.Contracts;` (sole using-target was `StockSeverity`) |
| `Services/StockSeverityCalculator.cs` | same replacement as above |
| `DashboardTiles/LowStockEfficiencyTile.cs` | **keep** `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;` (needed for `GetPurchaseStockAnalysisRequest`) and **add** `using Anela.Heblo.Application.Features.Purchase.Contracts;` (needed for `StockStatusFilter`) |
| `test/.../StockSeverityCalculatorTests.cs` | verified: the only symbol used from the use-case namespace is `StockSeverity` (23 references, no other type). Replace line 3's using with `using Anela.Heblo.Application.Features.Purchase.Contracts;` |
| `test/.../GetPurchaseStockAnalysisHandlerTests.cs` | already has `using Anela.Heblo.Application.Features.Purchase.Contracts;` (line 1) alongside the use-case using (line 3, still needed for `GetPurchaseStockAnalysisRequest`/`Response`/`Handler`) — verify no unused-using warning, no removal needed |
| `test/.../GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` | same shape as above — already has both usings; verify, don't remove the use-case one |

### Data Flow

No data flow changes — this is a pure symbol-table move. Request → Handler → Response shapes,
MediatR routing, JSON serialization, and dashboard-tile polling are all unaffected. The only
"flow" that changes is compile-time symbol resolution: `Services`/`DashboardTiles` now resolve
`StockSeverity`/`StockStatusFilter` from `Contracts` instead of transitively from `UseCases/{X}`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing an unqualified reference to one of the three enums in a file not yet identified, causing a build break | Low | Full-repo grep for `StockSeverity`, `StockStatusFilter`, `StockAnalysisSortBy` after the move (spec's FR-3 acceptance criterion); `dotnet build` must show zero errors before considering the task done |
| `LowStockEfficiencyTile.cs` accidentally loses the use-case using while gaining the Contracts using (since both target changes look similar) | Low | Called out explicitly above as Decision 2 — this file needs **both** usings, not a swap |
| Nothing prevents this anti-pattern from recurring (no architecture test enforces intra-module `Services`/`DashboardTiles` → `Contracts`-only dependency) | Low | Out of scope for this PR per "surgical changes." Noted for a future arch-review finding: consider extending `ModuleBoundariesTests.cs` (or a new lightweight reflection test) to also flag `Services`/`DashboardTiles` namespaces referencing sibling `UseCases.*` namespaces within the same module |
| OpenAPI/TypeScript client regeneration surfaces an unexpected diff despite the "type name, not namespace" assumption | Low | Already spot-checked: `frontend/src/api/generated/api-client.ts` currently emits `export enum StockSeverity`, `StockStatusFilter`, `StockAnalysisSortBy` with no namespace qualifier — NSwag has no C#-namespace awareness in the emitted name. Still, re-run `npm run build` after the move per NFR-3 to confirm the generated file is byte-identical (or diffs only in unrelated churn) |

## Specification Amendments

None required — the spec (`spec.r1.md`) already correctly identifies all six production files and
three test files, correctly anticipates the `LowStockEfficiencyTile.cs` dual-using requirement,
and correctly scopes out the Manufacture module's analogous issue. This review's independent
source inspection confirms every acceptance criterion in FR-1 through FR-4 and NFR-3 as written is
accurate against the current codebase state. One clarification worth stating explicitly for the
implementer (not a spec change): in `StockSeverityCalculatorTests.cs`, the use-case using
directive becomes fully unused and must be dropped (not just supplemented), whereas in the other
two test files it must be kept — this asymmetry is implied but not spelled out line-by-line in the
spec's FR-3.

## Prerequisites

None. `Features/Purchase/Contracts/` already exists with the exact conventions this change follows
(file-scoped namespace, one type per file). No migrations, config, or infrastructure changes are
needed. Implementation can start immediately.
