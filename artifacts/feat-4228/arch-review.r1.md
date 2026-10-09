# Architecture Review: Remove stale IMarginCalculationService comment from AnalyticsModule

## Skip Design: true
Backend-only, single-line comment deletion in a DI registration module. No UI/UX, no new or changed components, no visual surface at all.

## Architectural Fit Assessment
This aligns trivially with existing module conventions: each vertical slice module (`AnalyticsModule.cs`, `CatalogModule.cs`, etc.) registers its own services via `IServiceCollection` extension methods, and cross-module dependencies (when they genuinely exist) are documented by inline comments such as the ones already present in `Catalog/CostProviders/*.cs` (e.g. `// CatalogRepository -> IMarginCalculationService -> ISalesCostProvider -> ICatalogRepository`). The flagged comment in `AnalyticsModule.cs` is the one place where that convention is applied incorrectly — it documents a dependency that does not exist. There is exactly one integration point: the `AnalyticsModule.AddAnalyticsModule` method, and the change touches nothing else. Verified via `grep -rn "IMarginCalculationService" backend/src` that the interface's only registration is in `CatalogModule.cs` and its only consumer is `CatalogRepository`; Analytics has zero references to it and instead owns/registers `IMarginCalculator` at line 47 of the same file.

## Proposed Architecture

### Component Overview
No architectural components are added, removed, or reshaped.

```
AnalyticsModule.AddAnalyticsModule(services, configuration)
  ├─ IAnalyticsRepository        (unchanged)
  ├─ [DELETE stale comment]      <-- line 32, no code, comment only
  ├─ IProductFilterService       (unchanged)
  ├─ IReportBuilderService       (unchanged)
  ├─ IMarginCalculator           (unchanged, line 47)
  └─ ...validators / MediatR pipeline behaviors (unchanged)
```

### Key Design Decisions

#### Decision 1: Delete rather than correct/replace the comment
**Options considered:**
1. Rewrite the comment to correctly describe `IMarginCalculator`'s self-contained registration.
2. Delete the comment entirely, per the issue's suggested fix.

**Chosen approach:** Delete the comment (option 2), exactly as specified in the issue and spec FR-1.

**Rationale:** The surrounding code (`IProductFilterService`, `IReportBuilderService` registrations) has no equivalent explanatory comment, and `IMarginCalculator`'s registration further down the same file (line 47) is already self-explanatory without a comment. Adding a replacement comment would be redundant with no informational value; the issue explicitly recommends removal as "correct and sufficient." This keeps the diff minimal and matches the spec exactly.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single-line deletion in:
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` (line 32).

### Interfaces and Contracts
None affected. `IMarginCalculationService` (Catalog) and `IMarginCalculator` (Analytics) are both unchanged — this is a comment-only edit with zero effect on any interface, DI registration, or contract.

### Data Flow
Not applicable — no runtime behavior or data flow changes. DI container composition is unaffected (the deleted line was already inert — a comment, not code).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidentally removing/editing an adjacent code line instead of just the comment | Low | Diff review before commit; acceptance criteria in spec pin the two surrounding registration lines (`IProductFilterService`, `IReportBuilderService`) as must-remain-unchanged |
| Line-number drift if the file changed since the issue was filed | Low | Match by the comment's literal text (`// Note: IMarginCalculationService is registered by CatalogModule and injected here`), not by line number |

## Specification Amendments
None. The spec (`spec.r1.md`) is complete and accurate as written; no amendments needed.

## Prerequisites
None. No migrations, config, or infrastructure changes required. Implementation can start immediately.
