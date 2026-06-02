# Architecture Review: Remove Unused `GetGroupMarginTotalsAsync` from `IAnalyticsRepository`

## Skip Design: true

This is a pure backend dead-code deletion. No UI components, screens, layouts, or visual design decisions are involved. No frontend code or generated TypeScript client is affected (the interface is internal to `Anela.Heblo.Application.Features.Analytics.Infrastructure`).

## Architectural Fit Assessment

The change aligns cleanly with the project's Clean Architecture + Vertical Slice conventions described in `docs/architecture/development_guidelines.md` and `docs/architecture/filesystem.md`. Three structural observations confirm the spec's premises:

1. **The method is genuinely unused.** A repository-wide grep for `GetGroupMarginTotalsAsync` returns hits only in the interface (`IAnalyticsRepository.cs` line 26) and the implementation (`AnalyticsRepository.cs` line 41). No handler, MediatR request, controller, or test invokes it. `backend/test/.../AnalyticsRepositoryTests.cs` does not reference it.
2. **`MarginCalculator.GetGroupKey` is exposed on `IMarginCalculator` (line 14)** — it is already a first-class, public contract method. The private duplicate in `AnalyticsRepository` is strictly inferior: callers cannot reach it, and it can drift from the canonical implementation undetected.
3. **No call sites depend on the removed surface.** The repository's other responsibilities — `StreamProductsWithSalesAsync`, `GetProductAnalysisDataAsync`, `GetInvoiceImportStatisticsAsync`, `GetBankStatementImportStatisticsAsync` — are independent and untouched.

The deletion strengthens Interface Segregation (no mock or future implementation must satisfy a method nobody calls) without changing any observable behavior. It is the right call.

## Proposed Architecture

### Component Overview

```
Before:
  IAnalyticsRepository
    ├── StreamProductsWithSalesAsync
    ├── GetGroupMarginTotalsAsync   ← DEAD
    ├── GetProductAnalysisDataAsync
    ├── GetInvoiceImportStatisticsAsync
    └── GetBankStatementImportStatisticsAsync

  AnalyticsRepository : IAnalyticsRepository
    ├── StreamProductsWithSalesAsync       → delegates to IAnalyticsProductSource
    ├── GetGroupMarginTotalsAsync          ← DEAD (TODO: optimized SQL)
    ├── GetProductAnalysisDataAsync        → delegates to IAnalyticsProductSource
    ├── private GetGroupKey                ← DUPLICATE of MarginCalculator.GetGroupKey
    ├── GetInvoiceImportStatisticsAsync    → EF Core query
    └── GetBankStatementImportStatisticsAsync → EF Core query

  IMarginCalculator (Services/MarginCalculator.cs)
    └── public GetGroupKey                 ← CANONICAL

After:
  IAnalyticsRepository
    ├── StreamProductsWithSalesAsync
    ├── GetProductAnalysisDataAsync
    ├── GetInvoiceImportStatisticsAsync
    └── GetBankStatementImportStatisticsAsync

  AnalyticsRepository : IAnalyticsRepository
    ├── StreamProductsWithSalesAsync
    ├── GetProductAnalysisDataAsync
    ├── GetInvoiceImportStatisticsAsync
    └── GetBankStatementImportStatisticsAsync

  IMarginCalculator
    └── public GetGroupKey                 ← SOLE SOURCE OF TRUTH
```

### Key Design Decisions

#### Decision 1: Pure deletion vs. extraction-then-deletion
**Options considered:**
- (a) Delete `GetGroupMarginTotalsAsync`, its implementation, and the private duplicate helper. Leave `IMarginCalculator.GetGroupKey` as the canonical implementation.
- (b) Extract `GetGroupKey` into a shared static utility (`ProductGroupingKey.For(...)`) and have both `MarginCalculator` and any future repository implementation reference it.
- (c) Keep the method and add a `[Obsolete]` attribute pending a real caller.

**Chosen approach:** (a) — pure deletion.

**Rationale:** `IMarginCalculator.GetGroupKey` is already a public contract method on a DI-registered service. There is exactly one remaining implementation after the deletion, so introducing an abstraction (b) is premature per YAGNI and the project's "no abstractions beyond what the task requires" rule in CLAUDE.md. (c) preserves the ISP violation and the dead code without buying anything — `git` is the history mechanism, not `[Obsolete]`. The spec correctly chose (a).

#### Decision 2: Do not touch `MarginCalculator.GetGroupKey`
**Options considered:**
- (a) Leave `MarginCalculator.GetGroupKey` exactly as-is.
- (b) Add an XML doc comment proclaiming it canonical / the only implementation.

**Chosen approach:** (a).

**Rationale:** NFR-1 mandates surgical scope. Adding a "canonical" comment is editorialising and would be the kind of incidental change CLAUDE.md explicitly warns against. The fact that it is now the sole implementation is self-evident from the codebase after deletion.

#### Decision 3: Using-directive hygiene
**Options considered:**
- (a) Remove `using` directives only if they become genuinely unused after the deletion.
- (b) Leave all using directives alone.

**Chosen approach:** (a), per FR-2 acceptance criteria.

**Rationale:** After verifying the file, none of the existing `using` directives in `AnalyticsRepository.cs` will become unused — `Microsoft.EntityFrameworkCore`, `Anela.Heblo.Persistence`, the Analytics Contracts, and the UseCase namespaces are all referenced by the remaining methods (`GetInvoiceImportStatisticsAsync`, `GetBankStatementImportStatisticsAsync`, `GetProductAnalysisDataAsync`). Expected outcome: no usings to remove. If `dotnet format` or the compiler disagrees, follow it.

## Implementation Guidance

### Directory / Module Structure

No structural changes. Two files are edited in place; no files are added, renamed, or moved:

```
backend/src/Anela.Heblo.Application/Features/Analytics/
  Infrastructure/
    IAnalyticsRepository.cs    ← delete lines 23–31 (XML doc + method signature)
    AnalyticsRepository.cs     ← delete lines 38–68 (XML doc + method body),
                                  delete lines 79–88 (private GetGroupKey),
                                  collapse the resulting blank line(s) per existing style
  Services/
    MarginCalculator.cs        ← UNCHANGED
```

Note that the XML doc summary `/// Gets aggregated margin data ...` immediately above each declaration is part of the dead surface and must be removed along with the method.

### Interfaces and Contracts

**Modified contract — internal only:**
```csharp
public interface IAnalyticsRepository
{
    IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(...);
    Task<AnalyticsProduct?> GetProductAnalysisDataAsync(...);
    Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(...);
    Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(...);
}
```

**Unchanged canonical contract:**
```csharp
public interface IMarginCalculator
{
    // ... other members ...
    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);
}
```

**No public-surface impact:**
- No controller endpoint changes.
- No MediatR request/response/handler shapes change.
- No OpenAPI document changes → no TypeScript client regeneration.
- No DI registration changes (`AnalyticsRepository` is still registered as `IAnalyticsRepository`).

### Data Flow

No data flow changes. The deleted method was never wired into a request pipeline. Existing margin calculation continues to flow:

```
Controller → MediatR handler → IMarginCalculator.CalculateAsync(
    IAnalyticsRepository.StreamProductsWithSalesAsync(...),
    dateRange, groupingMode, marginLevel, ct)
  → MarginCalculator iterates the async stream, calls its own GetGroupKey per product,
    accumulates GroupTotals + GroupProducts + TotalMargin
  → returns MarginCalculationResult
```

This path is untouched by the deletion.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden caller via reflection, DI scanning, or dynamic dispatch | Low | Grep already shows zero references outside the two target files. `dotnet build` will fail if any consumer exists. No `Type.GetMethod`/`MakeGenericMethod` patterns are used against this interface in the project. |
| A test double / mock of `IAnalyticsRepository` (e.g. NSubstitute, Moq) implicitly relies on the removed member | Low | Mock libraries auto-implement missing members; removing a member from the contract cannot break a mock that doesn't reference it. `AnalyticsRepositoryTests.cs` confirmed to contain no reference to `GetGroupMarginTotalsAsync`. |
| Stale plan/design docs reference the removed method | Low (informational only) | Two markdown plans under `docs/superpowers/plans/` mention the method name. These are historical planning artifacts, not runtime contracts; do not edit them as part of this surgical change. NFR-1 forbids it. |
| `dotnet format` rewrites surrounding whitespace/blank-line style | Low | Run `dotnet format` after deletion as the spec requires; accept its output. If it produces noisy reformatting in unrelated regions, investigate before committing — but expected impact is just collapsing blank lines around the deletion sites. |
| Future need for an optimized SQL aggregation re-introduces the method shape inconsistently | Low | If/when reintroduced, the new method must come with a real caller and tests. Document the intent at that point, not pre-emptively here. |

## Specification Amendments

No substantive amendments. Two small clarifications worth recording so the implementer doesn't have to re-derive them:

1. **Delete the XML doc comments that accompany each removed member.** The spec lists line ranges for the method bodies but does not explicitly call out the `/// <summary>...</summary>` blocks immediately above each signature. Those blocks describe the dead members and must be deleted with them. Concretely:
   - `IAnalyticsRepository.cs` lines 23–25 (`/// <summary>` … `/// </summary>`) plus the signature at 26–31.
   - `AnalyticsRepository.cs` lines 38–40 plus the method body at 41–68. The private `GetGroupKey` (lines 79–88) has no doc comment.
2. **Expect zero `using`-directive removals.** Confirmed by inspection: every using in `AnalyticsRepository.cs` is still needed by the remaining methods. FR-2's "remove unused usings if they appear" clause is unlikely to trigger.

These are clarifications, not changes to scope or acceptance criteria.

## Prerequisites

None. This is a pure code-deletion change with no migrations, configuration, infrastructure, or upstream/downstream coordination required.

Verification commands (per `CLAUDE.md` "Validation before completion"):
- `dotnet build` — must succeed with no new errors or warnings.
- `dotnet format` — must report a clean tree after edits.
- Backend test suite — must pass; no test references the removed method, so no test changes are expected.
- A final repository-wide grep for `GetGroupMarginTotalsAsync` should return zero `.cs` matches.