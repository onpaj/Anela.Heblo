# Architecture Review: Remove application-layer concern from `DailyInvoiceCount` Domain type

## Skip Design: true

## Architectural Fit Assessment
The spec is architecturally sound and directly aligned with this repo's documented conventions. `docs/architecture/development_guidelines.md` is explicit that DTOs live in `Contracts/`, are never shared/global, and (per `CLAUDE.md`) are always classes, never records — exactly the shape proposed for `DailyInvoiceCountDto`. The existing `Contracts/` folder in `Anela.Heblo.Application/Features/Analytics/` already holds five sibling DTOs (`TopProductDto.cs`, `MonthlyProductMarginDto.cs`, etc.), all plain classes with public getter/setter properties — `DailyInvoiceCountDto` slots in as a sixth, matching the folder's existing style exactly (verified against `TopProductDto.cs`).

Verified against the actual code:
- `DailyInvoiceCount.cs` (Domain) is a 3-property mutable class; `IsBelowThreshold` is indeed set only by `GetInvoiceImportStatisticsHandler.Handle` (lines 50–52) after the repository call, and independently defaulted to `false` in all three `DailyInvoiceCount` construction sites inside `InvoiceImportStatisticsSourceAdapter.cs` (lines 47–52, 71–76, 92–97) — a class the handler doesn't even use (the handler goes through `IAnalyticsRepository`, not `IInvoiceImportStatisticsSource`). This confirms the spec's framing: the field is dead weight on the one path (`IInvoiceImportStatisticsSource`) and an application-layer stamp on the other (`IAnalyticsRepository` → handler).
- `GetInvoiceImportStatisticsResponse.Data` is currently `List<DailyInvoiceCount>` (Domain type leaking directly into the Application response) — confirms FR-1's claim and gives an extra, unstated rationale for the fix: the Domain type is currently serialized straight to the wire, which is itself a layering violation independent of the mutation issue.
- Three test files reference `IsBelowThreshold` on `DailyInvoiceCount` exactly as the spec describes (`GetInvoiceImportStatisticsHandlerTests.cs` lines 40–41, 61–62; `InvoiceImportStatisticsTileTests.cs` lines 41, 79; `InvoiceImportStatisticsSourceAdapterTests.cs` line 73).
- `InvoiceImportStatisticsTile.cs` and its tests only ever read `.Count` from `DailyInvoiceCount`, never `.IsBelowThreshold` — unaffected by this change, confirming the spec's Out of Scope note.
- Frontend: `useInvoiceImportStatistics.ts` re-exports the generated `DailyInvoiceCount` type verbatim (no aliasing), and `InvoiceImportChart.tsx` imports it from that hook file for `InvoiceImportChartProps.data`. `InvoiceImportStatistics.tsx` does **not** import the type name anywhere — it only destructures fields off the inferred `data` object — so it needs zero code change, only re-generation/behavioral re-verification. The generated `api-client.ts` (confirmed by direct inspection) names its TS class after the C# type used in `GetInvoiceImportStatisticsResponse.Data` (`export class DailyInvoiceCount implements IDailyInvoiceCount { ... }`, line 13777), so renaming the response's backing DTO to `DailyInvoiceCountDto` will indeed flow through to the generated export name as the spec predicts.
- `useInvoiceImportStatistics.test.ts` mocks the API client's return value as a plain object literal (`{ date, count, isBelowThreshold }`), never referencing the class name — confirms it needs no changes and will keep passing.

No architectural pushback: this is a straightforward Domain/Application boundary fix using an already-established pattern in the same module (5 other `Contracts/` DTOs). No new abstractions, no module boundary changes, no DI changes.

## Proposed Architecture
### Component Overview
No new components. One Domain type shrinks by one property; one new DTO class is added to an existing `Contracts/` folder; one handler switches from in-place mutation to a `Select` projection; one response DTO's list element type changes; three call sites drop a dead initializer; three test files are updated to match; two frontend files get a mechanical type-name rename; the generated TS client picks up the rename automatically on next build.

### Key Design Decisions

#### Decision 1: Where the threshold-aware DTO lives
**Options considered:**
- (a) New `DailyInvoiceCountDto` in `Application/Features/Analytics/Contracts/`.
- (b) Inline anonymous/tuple projection in the handler, no new class.
- (c) Keep `IsBelowThreshold` on the Domain type but make it computed (e.g. pass threshold into a method) rather than a mutable setter.

**Chosen approach:** (a), as specified.

**Rationale:** (b) would break OpenAPI/NSwag codegen, which needs a named class to generate a stable TS type — the entire point of FR-4 depends on a named response element type. (c) still leaves an application concern (the configured threshold) conceptually attached to a Domain entity, even if immutable — it doesn't fix the layering violation, only the mutability symptom. (a) matches the existing convention in this exact folder (`TopProductDto`, `MonthlyProductMarginDto`, etc. are all Application-layer read-model DTOs distinct from any Domain entity) and is the only option consistent with `development_guidelines.md`'s DTO rules.

#### Decision 2: Scope of the Domain type change
**Options considered:**
- (a) Remove only `IsBelowThreshold`, leave `Date`/`Count` untouched (as specified).
- (b) Also wire `IInvoiceImportStatisticsSource`/`InvoiceImportStatisticsSourceAdapter` into the handler in place of `IAnalyticsRepository`, since the adapter's dummy `IsBelowThreshold = false` is itself evidence of the same smell.

**Chosen approach:** (a).

**Rationale:** (b) is explicitly named in the spec as Out of Scope, tracked separately (`docs/superpowers/plans/2026-06-04-decouple-analytics-repository-from-invoices-and-bank.md`). Confirmed by reading the handler: it depends on `IAnalyticsRepository`, not `IInvoiceImportStatisticsSource` — the adapter is currently unused by this code path (dead code, pending that separate refactor). Folding (b) into this change would turn a small, mechanical cleanup into a repository-swap with its own risk surface (DI registration, different query semantics between `AnalyticsRepository` and the adapter's gap-filling logic). Keep them separate.

## Implementation Guidance

### Directory / Module Structure
No new folders. One new file:
```
backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs   (new)
```
Modified:
```
backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs                                            (remove IsBelowThreshold)
backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs                               (XML doc edit only)
backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs
backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsResponse.cs
backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs      (drop 3 dead initializers)
backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs
backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs
backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs
frontend/src/api/hooks/useInvoiceImportStatistics.ts
frontend/src/components/charts/InvoiceImportChart.tsx
frontend/src/api/generated/api-client.ts                                                                          (auto-regenerated, do not hand-edit)
```

### Interfaces and Contracts
```csharp
// backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs
namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class DailyInvoiceCountDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public bool IsBelowThreshold { get; set; }
}
```
`GetInvoiceImportStatisticsResponse.Data` becomes `List<DailyInvoiceCountDto>` (add `using Anela.Heblo.Application.Features.Analytics.Contracts;`, keep or drop `using Anela.Heblo.Domain.Features.Analytics;` depending on whether the file still needs `DailyInvoiceCount` — it won't after this change unless referenced elsewhere in the file).

`InvoiceImportStatisticsHandler.Handle`: replace the mutation loop with a projection, matching the finding's suggested fix verbatim:
```csharp
var dailyCounts = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
    startDate, endDate, request.DateType, cancellationToken);

return new GetInvoiceImportStatisticsResponse
{
    Data = dailyCounts.Select(c => new DailyInvoiceCountDto
    {
        Date = c.Date,
        Count = c.Count,
        IsBelowThreshold = c.Count < minimumThreshold
    }).ToList(),
    MinimumThreshold = minimumThreshold
};
```

`DailyInvoiceCount` (Domain) after the change:
```csharp
public class DailyInvoiceCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
```

`IInvoiceImportStatisticsSource.GetDailyCountsAsync` XML doc: delete the trailing clause `and <c>IsBelowThreshold</c> is always <c>false</c> (the consumer decides thresholds)` — the sentence has no referent left once the property is gone.

### Data Flow
Unchanged shape, changed backing type: `IAnalyticsRepository.GetInvoiceImportStatisticsAsync` → `List<DailyInvoiceCount>` (Domain, 2 fields) → handler projects to `List<DailyInvoiceCountDto>` (Application, 3 fields, threshold computed here) → `GetInvoiceImportStatisticsResponse` → controller → JSON (`date`/`count`/`isBelowThreshold`/`minimumThreshold`, byte-for-byte identical) → NSwag regenerates `api-client.ts` with the response element class renamed to `DailyInvoiceCountDto` → `useInvoiceImportStatistics.ts` re-exports the renamed type → `InvoiceImportChart.tsx` imports the renamed type for its props → `InvoiceImportStatistics.tsx` unaffected (no type-name reference, field access only).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend build breaks if codegen isn't re-run before `tsc` sees the old `DailyInvoiceCount` import | Low | Standard build already runs codegen as a prebuild step per `docs/development/api-client-generation.md`; no manual step needed beyond the normal `npm run build` |
| Missed call site outside the 3 backend production files / 3 test files enumerated in the spec | Low | `dotnet build` will fail loudly on any remaining `DailyInvoiceCount.IsBelowThreshold` reference (property removed = compile error, not silent runtime issue) |
| `InvoiceImportStatisticsSourceAdapterTests.cs` line 73 assertion removal leaves that test with no threshold-related coverage | Negligible | Correct outcome per spec — the assertion only ever verified a dummy value; nothing of value is lost |
| Someone later reintroduces a threshold-like field on the Domain type without going through `Contracts/` | Low | No automated guard proposed here (out of proportion for this fix); rely on code review. If this pattern repeats, consider an architecture test similar to `ModuleBoundariesTests` |

## Specification Amendments
None. The spec is precise, its acceptance criteria are directly checkable against the code as read, and its Out of Scope section correctly excludes the larger repository-swap refactor. No changes needed before implementation.

## Prerequisites
None. All referenced types, files, and conventions already exist in the codebase; no upstream work is required before starting.
