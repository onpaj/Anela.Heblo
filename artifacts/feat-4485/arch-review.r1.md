# Architecture Review: Remove decimal.MaxValue sentinel from CalculateForecastedDays

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Aligned with Clean Architecture: the domain should express absence of a value in its type rather than via a magic number the application layer must decode. Verified in code: only two callers (`GetPackingMaterialsListHandler`, `UpdatePackingMaterialQuantityHandler`); `PackingMaterialMapper.ToDto` and `PackingMaterialDto.ForecastedDays` are already `decimal?`; existing tests cover mapper null handling and list handler null/zero/numeric cases. No other usage of the method in the repo.

## Proposed Architecture

### Component Overview
```
PackingMaterial (Domain)
  CalculateForecastedDays(logs) -> decimal?   (null = no consumption history)
        |
        v
GetPackingMaterialsListHandler / UpdatePackingMaterialQuantityHandler (Application)
  round to 1dp if HasValue
        |
        v
PackingMaterialMapper.ToDto(material, decimal? forecast) -> PackingMaterialDto
```

### Key Design Decisions

#### Decision 1: Nullable return vs. dedicated result type
**Options considered:** (a) `decimal?`; (b) `TryCalculate(out decimal)`; (c) a value object.
**Chosen approach:** `decimal?`, as proposed in the issue.
**Rationale:** Smallest change, matches DTO/mapper types, no new concepts.

#### Decision 2: Where rounding lives
**Options considered:** (a) keep rounding in handlers; (b) move to domain; (c) shared Application helper.
**Chosen approach:** Keep rounding in the handlers (presentation concern, 1 dp), as a simple `forecast.HasValue ? Math.Round(forecast.Value, 1) : (decimal?)null` expression at both sites. Do NOT drop rounding as the issue's snippet implies (would change API output).
**Rationale:** Surgical change; domain keeps unrounded precision; avoids new abstractions for a one-liner. Moving rounding is out of scope.

## Implementation Guidance

### Directory / Module Structure
No new files except new tests:
- Edit `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/PackingMaterial.cs`
- Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`
- Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- Add domain tests under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` (e.g. `PackingMaterialForecastTests.cs`); extend update-quantity handler tests (see `PackingMaterialCrudHandlerTests.cs` for conventions and `MockPackingMaterialRepository`).

### Interfaces and Contracts
`public decimal? CalculateForecastedDays(List<PackingMaterialLog> recentLogs)`. Order of checks preserved: `CurrentQuantity <= 0` -> 0 first, then null when no non-zero consumption, else quotient. Update the inline comment to say null means no history.

### Data Flow
Repository logs (last month) -> entity computes `decimal?` -> handler rounds -> mapper -> DTO -> JSON `forecastedDays` null or number. Unchanged externally.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Rounding accidentally dropped (issue snippet) | Medium | Explicit FR-2; handler test asserting rounded value (e.g. 10.04 -> 10.0) |
| Zero-quantity precedence regress (0 vs null) | Low | Domain test: zero quantity with empty logs returns 0 |
| Process doc staleness CI gate | Low | Check `docs/processes/module-packing-materials.md`, `calc-packing-material-consumption.md`; bump `verified_at` / update, run index check |
| Missed caller | Low | grep confirmed two callers; compiler flags any nullable misuse |

## Specification Amendments
None beyond spec; spec already requires preserving rounding.

## Prerequisites
None. No migration, no config, no OpenAPI regeneration change.
