I have everything I need. The pattern is clear: MediatR handlers should delegate domain concerns to injected services (Vertical Slice + Application Services). The duplication is a textbook leaked concern. Producing the architecture review now.

# Architecture Review: Consolidate Margin-Level Resolution in GetProductMarginSummaryHandler

## Skip Design: true

This is a backend-only, behavior-preserving refactor inside an Application handler. No UI components, screens, layouts, DTOs, OpenAPI surface, or visual design decisions are touched.

## Architectural Fit Assessment

The change aligns cleanly with the project's existing architectural commitments:

- **Vertical Slice + MediatR (per `docs/architecture/development_guidelines.md`).** The handler `GetProductMarginSummaryHandler` is the application service for this slice; domain calculation logic belongs in `IMarginCalculator`. Today the handler bypasses its own injected service to reimplement `GetMarginAmountForLevel` inline — a classic *leaked concern* that the guideline explicitly warns against ("business logic in controller class → should be in MediatR handlers" extends to "domain calc inline in handler → should be in domain service").
- **Dependency already wired.** `IMarginCalculator` is injected at line 15 and already used by `CalculateAsync` (line 39), `GetGroupDisplayName` (line 71), and indirectly by `MonthlyBreakdownGenerator`. The refactor introduces zero new dependencies, zero new DI registrations, zero new contracts.
- **Consistency with `MarginCalculator.CalculateAsync`.** Inside the calculator itself (`MarginCalculator.cs:63`), the per-product margin contribution is computed as `(decimal)totalSold * GetMarginAmountForLevel(product, marginLevel)` — identical to what `CalculateTotalMarginForLevel` should become. After the refactor the handler will use the *same expression shape* as the canonical service, eliminating the only place in the Analytics module where margin-level resolution is duplicated.
- **Integration points are minimal.** Only one call site (`GenerateTopProducts`, line 78) consumes `CalculateTotalMarginForLevel`; the call signature is unchanged.

`grep` confirms the duplication is exactly two sites (`GetProductMarginSummaryHandler.cs:227` and `MarginCalculator.cs:120`) — no hidden third call site to worry about.

## Proposed Architecture

### Component Overview

```
GetProductMarginSummaryRequest
        │
        ▼
GetProductMarginSummaryHandler  ──── injects ────►  IMarginCalculator
        │                                                  │
        │  Handle()                                         │
        │   ├─► _marginCalculator.CalculateAsync(...)       │
        │   ├─► GenerateTopProducts(...)                    │
        │   │     ├─► _marginCalculator.GetGroupDisplayName │
        │   │     ├─► CalculateGroupMarginData(...)         │
        │   │     └─► CalculateTotalMarginForLevel(...)     │
        │   │            │                                   │
        │   │            └─► _marginCalculator              │
        │   │                .GetMarginAmountForLevel(...)  │  ◄── single source of truth
        │   │                                                │
        │   └─► _monthlyBreakdownGenerator.Generate(...)    │
        ▼
GetProductMarginSummaryResponse
```

The only structural change: the dotted internal switch in `CalculateTotalMarginForLevel` is removed and the arrow now goes through the already-existing dependency.

### Key Design Decisions

#### Decision 1: Delegate via the existing private helper (do not inline at the call site)
**Options considered:**
- **A.** Keep the private method `CalculateTotalMarginForLevel` and have its body call `_marginCalculator.GetMarginAmountForLevel`.
- **B.** Delete the private method, inline the LINQ `Sum` at the single call site in `GenerateTopProducts`.
- **C.** Expose a new public method like `IMarginCalculator.SumTotalMarginForLevel(products, marginLevel)` and have the handler call that.

**Chosen approach:** A.

**Rationale:**
- Matches the spec's `Out of Scope` constraint ("Renaming or removing the private `CalculateTotalMarginForLevel` helper… is out of scope").
- Keeps the diff surgical (single method body changes), reducing review and regression risk.
- Avoids broadening `IMarginCalculator`'s contract for a single private call — option C would be over-engineering for one call site (YAGNI). If a future second caller appears, promoting the helper is cheap.
- Option B would collapse the readable `var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);` line in `GenerateTopProducts` into a less self-documenting LINQ block.

#### Decision 2: Preserve the silent `_ => M2` fallback
**Options considered:**
- **A.** Preserve the existing fallback semantics by delegating verbatim to `GetMarginAmountForLevel`, which already implements the same fallback.
- **B.** Tighten behavior to throw on unknown margin level.

**Chosen approach:** A.

**Rationale:**
- Spec explicitly puts fallback changes out of scope (`Out of Scope` § 3).
- `MarginCalculator.GetMarginAmountForLevel` already implements `_ => product.M2Amount`, so delegating is automatically behavior-preserving for unknown inputs.
- Any future move to strict validation should change the *canonical* method so both `CalculateAsync` and the summary handler benefit simultaneously — exactly the property this refactor enables.

#### Decision 3: Do not modify `IMarginCalculator` or its implementation
**Options considered:**
- **A.** Leave `IMarginCalculator.GetMarginAmountForLevel` and `MarginCalculator` untouched.
- **B.** Mark `GetMarginAmountForLevel` `internal` or hide it behind a more specific signature.

**Chosen approach:** A.

**Rationale:** FR-3 explicitly forbids changes to the interface or canonical implementation. Visibility tightening would also break the existing `CalculateAsync` consumer at line 63.

## Implementation Guidance

### Directory / Module Structure
No new files. No new folders. No changes to module boundaries.

Modified file:
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — replace the body of `CalculateTotalMarginForLevel` (lines 217–237).

Test file (extended, not restructured):
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — add cases for FR-2 acceptance (case-insensitive resolution, unknown-level fallback to M2).

Unchanged (per FR-3):
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`
- `IMarginCalculator` contract

### Interfaces and Contracts

No contract changes. Existing surface to preserve:

```csharp
// IMarginCalculator (unchanged)
decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel);

// Handler private helper (signature unchanged)
private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel);
```

Target body of the private helper:

```csharp
private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)
{
    return products.Sum(p =>
        (decimal)p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)
        * _marginCalculator.GetMarginAmountForLevel(p, marginLevel));
}
```

Notes for the implementer:
- The cast to `decimal` must remain on the sales total (the operand from `SalesHistory.Sum` is `double`/`int`-typed; mirroring the existing pattern at `MarginCalculator.cs:63` keeps numeric semantics identical).
- Keep the existing XML `<summary>` comment on the method.
- Do not change `GenerateTopProducts`, `CalculateGroupMarginData`, `ApplySorting`, or any other handler internals.

### Data Flow

For the single affected path `Handle → GenerateTopProducts → CalculateTotalMarginForLevel`:

```
products: List<AnalyticsProduct>           marginLevel: string
        │                                          │
        └──────────────► CalculateTotalMarginForLevel ◄─────────┐
                                  │                              │
              per product p:      ▼                              │
              totalSales = Σ(B2B + B2C)                          │
              marginPerUnit = _marginCalculator                  │
                              .GetMarginAmountForLevel(p, level) │ ◄── single source of truth
              contribution  = (decimal)totalSales * marginPerUnit│     (was duplicated; now delegated)
                                  │
                                  ▼
                       Σ contributions  →  totalMarginForLevel  →  TopProductDto.TotalMargin
```

Numerically identical to today for all `(products, marginLevel)` pairs, including the unknown-level fallback to M2.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent numerical drift from operator precedence or cast placement | Low | Mirror the exact expression shape used in `MarginCalculator.CalculateAsync` (`(decimal)totalSold * GetMarginAmountForLevel(...)`); existing `GetProductMarginSummaryHandlerTests` cover `M2` totals and will catch arithmetic regressions. |
| Unknown-level callers begin to throw (semantics tightening leaks in) | Low | FR-3 + Decision 3: do not modify `MarginCalculator`; the `_ => M2Amount` arm stays in place. Add an explicit unit test asserting fallback for unknown level (FR-2 AC). |
| Coverage regression from spec's prohibition on removing tests | Low | NFR-4 explicitly forbids reducing coverage; add the two new cases (case-insensitive, unknown-level fallback) on top of the existing suite rather than replacing any. |
| Future divergence reappearing if a developer reintroduces the inline switch | Low | NFR-3 grep guard (`grep -r "M0\" =>" backend/src/Anela.Heblo.Application/Features/Analytics/` ⇒ exactly 1 hit) can be run locally or codified later as a lightweight architecture test. Not in scope to add, but worth noting. |
| Performance regression from extra virtual call per product | Negligible | Interface dispatch on a hot path that already enumerates `SalesHistory`; NFR-1 budget (≤1% median) is comfortably met by an O(1) switch behind one indirection. No benchmark required for this change. |

## Specification Amendments

The spec is internally consistent, scoped tightly, and traceable to verified source locations (line numbers in `GetProductMarginSummaryHandler.cs` and `MarginCalculator.cs` were independently confirmed). No amendments are required.

Two minor clarifications for the implementer (not changes to the spec):

1. **Test placement.** The FR-2 acceptance tests (case-insensitive resolution, unknown-level fallback) should be added to `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — exercising the handler's full `Handle` path with `MarginLevel = "m1"` / `MarginLevel = "M9"` — rather than to `MarginCalculatorTests.cs`. The point is to verify the *handler now delegates*; testing only the calculator would not catch a regression where the handler reintroduces a local switch.
2. **`SalesHistory` enumeration parity.** Use `p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C)` (the existing expression) verbatim; do not "improve" it to a single pass or precompute outside the LINQ block. Surgical-changes rule.

## Prerequisites

None. Specifically:

- No database migrations.
- No new DI registration — `IMarginCalculator` is already registered (it's resolved by the existing handler constructor and by `GetProductMarginSummaryHandlerTests`).
- No configuration, Key Vault, or feature-flag changes.
- No OpenAPI client regeneration (no DTO or controller surface affected).
- No coordination with frontend, E2E suite, or staging environment.

The change can begin immediately on the current worktree (`feat-arch-review-analytics-getproductmarginsu`) and ship as a single small commit.