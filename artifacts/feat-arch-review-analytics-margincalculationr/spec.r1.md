I have enough context. Now I'll write the spec.

# Specification: Move `MarginCalculationResult` from Domain to Application layer

## Summary
Relocate `MarginCalculationResult` from `Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` to the Application layer. This is a structural refactor to restore Clean Architecture dependency direction — the type is an Application-layer use-case helper, not a domain concept, and currently leaks the Application layer's data shape into the Domain layer.

## Background
The Domain layer should model business concepts (entities, value objects, domain services, repository interfaces). `MarginCalculationResult` is the return shape of `IMarginCalculator.CalculateAsync()` — an Application-layer service — and is consumed only inside the Application layer (`GetProductMarginSummaryHandler`, `MonthlyBreakdownGenerator`). Its shape (`Dictionary<string, decimal>` group totals + `Dictionary<string, List<AnalyticsProduct>>` group products + `decimal` total) is dictated by what the `GetProductMarginSummary` use case needs to render; it has no standalone domain meaning.

By living in `Domain/Features/Analytics/AnalyticsProduct.cs` it:
- Inverts the dependency rule by encoding Application-layer organization (grouped dictionaries) in the Domain layer.
- Couples `AnalyticsProduct.cs` to a specific use case's intermediate result shape.
- Forces any future, differently shaped margin calculation result to either bend to this dictionary form or live in another layer — creating asymmetry.

A prior plan (`docs/superpowers/plans/2026-05-27-margin-calculator-abstractions.md`, line 7) explicitly chose to leave the type in Domain on the grounds that "Application returning Domain-defined types is allowed." That justification is correct for true domain types; it does not apply here because `MarginCalculationResult` is not one. This spec supersedes that decision for this type only.

Sibling types in the same Domain file remain in place: `AnalyticsProduct`, `SalesDataPoint`, `DateRange` are legitimate domain types and are out of scope.

There is an unrelated, identically named `MarginCalculationResult` class at `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs:53`. It is a different type in a different namespace (`Anela.Heblo.Application.Features.Catalog.Services`) and must not be conflated, renamed, or merged with the Analytics type.

## Functional Requirements

### FR-1: Relocate `MarginCalculationResult` to the Application layer
Move the type definition out of `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` and into a new file under the Application layer's Analytics services folder.

**Target location:** `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculationResult.cs`
**Target namespace:** `Anela.Heblo.Application.Features.Analytics.Services`

The class shape stays byte-identical:

```csharp
public class MarginCalculationResult
{
    public required Dictionary<string, decimal> GroupTotals { get; init; }
    public required Dictionary<string, List<AnalyticsProduct>> GroupProducts { get; init; }
    public required decimal TotalMargin { get; init; }
}
```

The XML doc comment (`/// <summary> Result object for margin calculations </summary>`) moves with it.

**Acceptance criteria:**
- A single file `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculationResult.cs` exists containing the class in namespace `Anela.Heblo.Application.Features.Analytics.Services`.
- `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs` no longer contains a `MarginCalculationResult` declaration; the file ends after `DateRange`.
- The class signature, property names, types, modifiers (`required`, `init`), and access level are unchanged.
- No new file is created in `Anela.Heblo.Domain`; no other Domain types are touched.
- The Catalog-layer `MarginCalculationResult` at `Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs` is not modified.

### FR-2: Update all references to the new namespace
Every consumer currently importing `Anela.Heblo.Domain.Features.Analytics` for the purpose of resolving `MarginCalculationResult` must instead resolve it from `Anela.Heblo.Application.Features.Analytics.Services`.

Files known to reference the type (verified by grep):
1. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` — interface declaration + implementation; already in the target namespace, so no `using` change needed but the type is now in the same namespace.
2. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MonthlyBreakdownGenerator.cs` — interface + implementation; same target namespace, no `using` change needed.
3. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — method parameter type; ensure `using Anela.Heblo.Application.Features.Analytics.Services;` is present (already is for `IMarginCalculator`).
4. `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — instantiates `MarginCalculationResult` in arrange blocks; add `using Anela.Heblo.Application.Features.Analytics.Services;` if not already present.

**Acceptance criteria:**
- A solution-wide search for `Anela.Heblo.Domain.Features.Analytics.MarginCalculationResult` returns zero results.
- A solution-wide search for `MarginCalculationResult` (excluding the Catalog `SafeMarginCalculator` file and its tests) resolves only to `Anela.Heblo.Application.Features.Analytics.Services.MarginCalculationResult`.
- No unused `using` directives are left behind; if removing the Application-layer Analytics services type from Domain causes a file to no longer need `using Anela.Heblo.Domain.Features.Analytics;`, that using is removed only if no other Domain.Analytics type (`AnalyticsProduct`, `SalesDataPoint`, `DateRange`, `AnalyticsProductType`, `ProductGroupingMode`) is still referenced in that file.

### FR-3: Behavior preservation
This is a pure structural move. No property is added, removed, renamed, retyped, or reordered. No constructor, method, or factory is introduced. No DI registration changes. No serialization, no API contract, no DB schema is affected.

**Acceptance criteria:**
- `dotnet build` succeeds for the entire solution.
- `dotnet format` produces no changes.
- All existing tests pass without modification beyond `using` adjustments.
- `GetProductMarginSummaryHandlerTests` continues to pass with its existing `MarginCalculationResult { ... }` object initializers.
- No production behavior change is observable from outside the Application layer.

### FR-4: Update plan documentation
The plan `docs/superpowers/plans/2026-05-27-margin-calculator-abstractions.md` explicitly states (line 7) that `MarginCalculationResult` "remains in Domain" and (line 1076) that no task moves it. Update those two statements to reflect the new location so the plan stays a faithful record. Do not rewrite the plan's other content.

**Acceptance criteria:**
- Line 7 of the plan no longer asserts the type stays in Domain.
- Line 1076's verification item is removed or rewritten to reflect the new location.
- No other plan files are edited.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. Type identity, layout, and access patterns are unchanged.

### NFR-2: Security
No security surface affected.

### NFR-3: Backward compatibility
None required. The type is internal to the backend (not in any public API contract or OpenAPI surface). No client code, no migration, no feature flag.

### NFR-4: Architectural integrity
After the move, the dependency direction in `Anela.Heblo.Domain.Features.Analytics` flows only outward — no Domain type references Application-layer concepts. This is the entire point of the change and must hold.

## Data Model
The persisted schema and DTO contracts are unaffected. `MarginCalculationResult` is an in-memory computation result, never serialized or persisted. The internal shape is unchanged:

| Property | Type | Notes |
|---|---|---|
| `GroupTotals` | `Dictionary<string, decimal>` | required init |
| `GroupProducts` | `Dictionary<string, List<AnalyticsProduct>>` | required init; `AnalyticsProduct` stays in Domain |
| `TotalMargin` | `decimal` | required init |

`AnalyticsProduct` (Domain) being referenced from an Application-layer type is the correct direction under Clean Architecture — Application depends on Domain.

## API / Interface Design
No HTTP, MediatR, or DI surface change. `IMarginCalculator` and `IMonthlyBreakdownGenerator` interfaces keep the same signatures; only the namespace from which `MarginCalculationResult` is imported changes (and within the `Services` folder, no `using` is needed because the type is now in the same namespace as the interface).

## Dependencies
None. Self-contained refactor inside the backend solution. No NuGet package, no infrastructure, no external service.

## Out of Scope
- Moving, renaming, or refactoring `AnalyticsProduct`, `SalesDataPoint`, `DateRange`, `AnalyticsProductType`, or `ProductGroupingMode`.
- Any change to `Anela.Heblo.Application.Features.Catalog.Services.MarginCalculationResult` (the unrelated, identically named Catalog type).
- Restructuring the `MarginCalculator` / `MonthlyBreakdownGenerator` interfaces (e.g. splitting `IMarginCalculator` into smaller interfaces).
- Replacing the dictionary-based result shape with a richer type (e.g. `IReadOnlyList<MarginGroup>`). Worth doing later but not in this change.
- Touching frontend code, OpenAPI generation, migrations, or any deployment artifact.
- Adding tests beyond those needed to keep existing tests green.

## Open Questions
None.

## Status: COMPLETE