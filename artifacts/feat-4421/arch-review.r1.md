# Architecture Review: Correct misleading streaming comments in Analytics

## Skip Design: true
Backend comment-only change; no UI.

## Architectural Fit Assessment
Comment/documentation fix inside `Features/Analytics`. No pattern, module-boundary or contract change. Verified in code: `MarginCalculationResult.GroupProducts` is consumed by `GetProductMarginSummaryHandler.GenerateTopProducts` and `MonthlyBreakdownGenerator`, and asserted in `GetProductMarginSummaryHandlerTests`, so retention is intentional design.

## Proposed Architecture

### Component Overview
Unchanged: `IAnalyticsRepository.StreamProductsWithSalesAsync` -> `MarginCalculator.CalculateAsync` (retains per-group product lists) -> handler / `MonthlyBreakdownGenerator`.

### Key Design Decisions

#### Decision 1: Comment fix only, not the O(n_groups) refactor
**Options considered:** (a) correct comments; (b) store only `GroupMarginData` + display name per group.
**Chosen approach:** (a).
**Rationale:** (b) changes a public result type used by two consumers plus tests, with no demonstrated memory problem (solo project, "surgical changes"). Note (b) as a follow-up in the new comment.

#### Decision 2: Also fix sibling claims
**Chosen approach:** Correct `GetProductMarginSummaryHandler` class summary and step 3 comment, and `AnalyticsRepository` summaries, since they repeat the false "reduces memory usage" claim and would keep misleading readers.

## Implementation Guidance

### Directory / Module Structure
Edit only:
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` (class summary above `MarginCalculator`, summary on `CalculateAsync`; keep the `IMarginCalculator` contract text)
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsRepository.cs`
- `docs/processes/calc-product-margin-summary.md` (wording + `verified_at`)

### Interfaces and Contracts
None change. Do not alter code lines, only comments/XML docs.

### Data Flow
Unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidental code change | Low | Diff must contain comment lines only; `dotnet build` + `dotnet format` + Analytics tests |
| Process-doc CI gate fails | Low | Update doc in same PR, run `python3 scripts/process-docs/check.py index` |

## Specification Amendments
None.

## Prerequisites
None.
