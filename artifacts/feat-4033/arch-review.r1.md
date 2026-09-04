# Architecture Review: Remove `GetCacheStatus()` from `IFinancialAnalysisService`

## Skip Design: true

## Architectural Fit Assessment

This is a same-module, internal-quality cleanup with zero surface-area change. `IFinancialAnalysisService` lives in `Application/Features/FinancialOverview/Services/`, not in a `Contracts/` folder used for cross-module boundaries per `docs/architecture/development_guidelines.md` — it is a module-internal service abstraction, registered and consumed entirely within `FinancialOverviewModule`. Verified directly against source:

- `FinancialOverviewController` (`backend/src/Anela.Heblo.API/Controllers/FinancialOverviewController.cs`) exposes exactly two MediatR-backed endpoints (`GET /api/FinancialOverview`, `GET /api/FinancialOverview/comparison`); neither touches cache status.
- `FinancialOverviewModule.RegisterBackgroundRefreshTasks` (`FinancialOverviewModule.cs`) registers only `RefreshFinancialDataAsync` via `IFinancialAnalysisService`.
- `FinancialAnalysisService.GetCacheStatus()` (line 342) is called only twice, both as unqualified self-calls on `this` inside `GetFinancialOverviewAsync` (lines 77, 94) — never through an injected `IFinancialAnalysisService` reference.
- Repo-wide `grep` for `GetCacheStatus` confirms exactly these four occurrences (interface declaration, implementation, two call sites) and nothing in `backend/test/`.
- Test files touching this service (`FinancialAnalysisServiceTests.cs`, `GetFinancialOverviewHandlerTests.cs`, `GetFinancialComparisonHandlerTests.cs`, `FinancialOverviewModuleTests.cs`) contain no reference to `GetCacheStatus`.

The guidelines document explicitly states contracts should expose "only the operations it actually consumes (no speculative methods)" (development_guidelines.md, line 231) — this change is a direct, low-risk application of that stated principle to a method that has no consumer through the interface at all. There is no UI, no new endpoint, no data model, and no cross-module contract involved, so `Skip Design: true` is unambiguous.

## Proposed Architecture

No new components. This is a signature/visibility change on one existing interface and one existing class within the `FinancialOverview` module.

### Component Overview

```
FinancialOverviewController --(MediatR)--> Handlers --> IFinancialAnalysisService (3 members, was 4)
                                                              |
                                                              +-- FinancialAnalysisService (impl)
                                                                    - GetFinancialOverviewAsync (public, unchanged)
                                                                    - RefreshFinancialDataAsync (public, unchanged)
                                                                    - GetFinancialComparisonAsync (public, unchanged)
                                                                    - GetCacheStatus (private, was public+interface member)
                                                                        called only from GetFinancialOverviewAsync (this.*)

BackgroundRefreshScheduler --(via IFinancialAnalysisService)--> RefreshFinancialDataAsync   [unchanged]
```

No wiring changes: `FinancialOverviewModule.AddFinancialOverviewModule` continues to register `FinancialAnalysisService` as the sole `IFinancialAnalysisService` (line: `services.AddScoped<IFinancialAnalysisService, FinancialAnalysisService>();`), unaffected by shrinking the interface.

### Key Design Decisions

#### Decision 1: Remove vs. keep-but-mark-obsolete
**Options considered:**
- Keep `GetCacheStatus()` on the interface, mark `[Obsolete]`.
- Remove it from the interface entirely; keep the method as `private` on the concrete class.

**Chosen approach:** Remove entirely, per the spec.

**Rationale:** The interface is module-internal (not a published contract, not exposed via OpenAPI, no external consumer). `[Obsolete]` exists to give external consumers a migration window — there is none here, so it would add permanent noise with no offsetting benefit. Deleting the member is the correct move for an internal contract with zero interface-level callers.

#### Decision 2: `private` vs. `internal` access on `FinancialAnalysisService.GetCacheStatus()`
**Options considered:** `private`, `internal`.

**Chosen approach:** `private`, as specified in FR-2.

**Rationale:** Both call sites are inside `FinancialAnalysisService` itself; no other type in the module needs access. `internal` would only be justified if a test needed direct access to the method without going through the public interface — confirmed by inspection that `FinancialAnalysisServiceTests.cs` does not reference `GetCacheStatus` at all, so `private` is the tighter, correct choice. If the build/test step (see FR-3) turns up a hidden reflection-based test dependency, `internal` + `InternalsVisibleTo` is the fallback, not `public`.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edits confined to two existing files:
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs` — delete the `GetCacheStatus()` declaration and its XML doc comment (lines ~28–31).
- `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` — change `public FinancialAnalysisCacheStatus GetCacheStatus()` (line 342) to `private FinancialAnalysisCacheStatus GetCacheStatus()`. Leave its existing doc comment and body untouched.

`FinancialAnalysisCacheStatus.cs` is not touched — it remains a public type, still the return type of the now-private method.

### Interfaces and Contracts

```csharp
// After
public interface IFinancialAnalysisService
{
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months, bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
        bool includeCurrentMonth = false,
        CancellationToken cancellationToken = default);

    Task RefreshFinancialDataAsync(
        DateTime? startDate, DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years, bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default);
}
```

No other consumer of this interface exists anywhere in the codebase (controllers, other modules, generated OpenAPI client) — confirmed by grep — so this is not a breaking contract change from any external caller's perspective.

### Data Flow
Unchanged. `GetFinancialOverviewAsync` still computes `hybridCacheStatus`/`cacheStatus` via the same unqualified `GetCacheStatus()` call, which C# resolves identically whether the method is public+interface-declared or private — no vtable/interface dispatch was ever involved since the call is on `this`, not on an `IFinancialAnalysisService`-typed reference.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A hidden reflection-based test or a `Mock<IFinancialAnalysisService>` setup somewhere outside the grepped paths references `GetCacheStatus` | Low | `dotnet build` will fail loudly on any interface-level reference; run full test suite for the module before merging (FR-3). Grep already confirms zero hits repo-wide. |
| A future developer wants cache status exposed for monitoring and re-adds it broadly to this interface again | Low | Spec's "Out of Scope" already flags this; when it happens, prefer a narrow `ICacheMonitor` interface (as the original finding suggests) rather than widening `IFinancialAnalysisService` again. |

No other risks — this is a compile-time-checked, behavior-preserving visibility change.

## Specification Amendments

None. The spec is architecturally sound as written and matches the verified source exactly (line numbers, call sites, test file list). One addition for completeness: the spec's FR-3 test-file list omits `FinancialAnalysisServiceTests.cs`, which also exists under `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` — verified it likewise contains no `GetCacheStatus` reference, so this does not change the spec's conclusion, but the full test run in FR-3 should be understood to cover that file too.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed. Implementation can start immediately.
