# Specification: Remove `GetCacheStatus()` from `IFinancialAnalysisService`

## Summary
`IFinancialAnalysisService.GetCacheStatus()` is a public interface method with no external caller — it is only ever invoked internally, as a same-class self-call, from within `FinancialAnalysisService.GetFinancialOverviewAsync`. This change removes the method from the interface and makes it a private implementation detail of `FinancialAnalysisService`, eliminating an interface-segregation violation with no behavioral impact.

## Background
`FinancialAnalysisService` implements `IFinancialAnalysisService`, which is consumed via DI by MediatR handlers (`GetFinancialOverviewAsync`, `GetFinancialComparisonAsync` request handlers) and by a registered background refresh task (`RefreshFinancialDataAsync`). `GetCacheStatus()` was added to the interface to report cache freshness/coverage (`FinancialAnalysisCacheStatus`: `LastRefresh`, `CachedMonthsCount`, `CachedStockMonthsCount`), but:

- `FinancialOverviewController` exposes only `GET /api/FinancialOverview` and `GET /api/FinancialOverview/comparison`, neither of which surfaces cache status.
- `FinancialOverviewModule.RegisterBackgroundRefreshTasks` registers only `RefreshFinancialDataAsync` as a background task.
- No test in `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` sets up or asserts against `GetCacheStatus()` on the `Mock<IFinancialAnalysisService>` (confirmed by repo-wide search — the only references outside the interface/implementation are the two internal self-calls in `FinancialAnalysisService.GetFinancialOverviewAsync`, at lines 77 and 94 of `FinancialAnalysisService.cs`).
- The implementation calls `GetCacheStatus()` on itself (`this`), never through an injected `IFinancialAnalysisService` reference, so the call sites do not rely on interface dispatch.

Keeping an unused member on a public, DI-registered interface widens the contract every implementor (real or test double) must satisfy, for zero functional benefit — a textbook Interface Segregation Principle (ISP) violation. This is a pure internal-quality cleanup filed by the repository's automated architecture-review routine (issue #4033); it changes no observable behavior.

## Functional Requirements

### FR-1: Remove `GetCacheStatus()` from `IFinancialAnalysisService`
Delete the `GetCacheStatus()` method declaration (including its XML doc comment) from `IFinancialAnalysisService` in `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs`.

**Acceptance criteria:**
- `IFinancialAnalysisService` no longer declares `GetCacheStatus()`.
- The interface retains exactly its other three members unchanged: `GetFinancialOverviewAsync`, `RefreshFinancialDataAsync`, `GetFinancialComparisonAsync`.
- The `FinancialAnalysisCacheStatus` type (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialAnalysisCacheStatus.cs`) is left in place unchanged (it remains the private method's return type).

### FR-2: Make `GetCacheStatus()` a private method on `FinancialAnalysisService`
In `FinancialAnalysisService` (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`), change the method's access modifier from `public` to `private`. Remove the (now redundant, since it's no longer implementing an interface member) XML doc comment only if it was solely copied from the interface; otherwise leave the existing doc comment on the method as-is since it still documents useful internal behavior.

**Acceptance criteria:**
- `FinancialAnalysisService.GetCacheStatus()` is declared `private` (not `public`, not `internal`).
- The method body (cache-status computation logic, lines ~342–370) is unchanged — no logic, caching keys, or estimation behavior is altered.
- The two existing self-calls inside `GetFinancialOverviewAsync` (line 77: `var hybridCacheStatus = GetCacheStatus();` and line 94: `var cacheStatus = GetCacheStatus();`) compile and behave identically, since they already call the method on `this` without going through the interface.
- No other member of `FinancialAnalysisService` that currently calls `GetCacheStatus()` is broken (repo-wide search confirms only these two call sites exist).

### FR-3: Update or remove now-invalid test/mock references
Confirm (via build) that no test project references `GetCacheStatus()` through `IFinancialAnalysisService`. Per repository-wide search, none currently do — `Mock<IFinancialAnalysisService>` usages in `GetFinancialOverviewHandlerTests.cs` and `GetFinancialComparisonHandlerTests.cs`, and the concrete resolution in `FinancialOverviewModuleTests.cs`, do not set up or call `GetCacheStatus()`. No test code changes are expected, but this must be verified by a full test-suite run, not assumed.

**Acceptance criteria:**
- `dotnet build` succeeds with no compiler errors referencing `GetCacheStatus` in any test project.
- The full existing test suite for the `FinancialOverview` module passes unchanged (no test modifications required, per current findings).
- If a build/test failure surfaces an undiscovered reference to `GetCacheStatus()` via the interface, that reference is fixed to call the concrete `FinancialAnalysisService` type internally, or (if that is not appropriate) this spec's Open Questions is revisited before removing the interface member.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a compile-time access-modifier and interface-shape change; no runtime code path, algorithm, or cache-computation logic is altered. `GetCacheStatus()`'s existing O(24) cache-key-lookup loop is preserved exactly as-is.

### NFR-2: Security
No security impact. `GetCacheStatus()` was never exposed via HTTP and carries no new data-exposure risk in either its current or proposed form. No auth/authorization changes are required.

### NFR-3: Maintainability
Reduces the public surface area of `IFinancialAnalysisService` to exactly the members real callers use, per ISP. Any future test double or alternative implementation (e.g., a mock or stub) is no longer required to implement a method it will never be asked to invoke through the interface.

### NFR-4: Backward compatibility
`IFinancialAnalysisService` is an internal application-layer interface (not a public NuGet package or external API contract) consumed only within this monorepo via DI. Removing a member is safe: `FinancialAnalysisService` is registered as the sole `IFinancialAnalysisService` implementation in `FinancialOverviewModule.AddFinancialOverviewModule`, and no other module, external consumer, or generated OpenAPI client references this interface (it is not exposed through any controller and therefore does not appear in the OpenAPI spec).

## Data Model
No data model changes. `FinancialAnalysisCacheStatus` (properties: `LastRefresh: DateTime`, `CachedMonthsCount: int`, `CachedStockMonthsCount: int`, plus any additional existing properties in `FinancialAnalysisCacheStatus.cs`) remains unchanged and continues to be used as the private method's return type and internally within `GetFinancialOverviewAsync`'s hybrid/cache-check branches.

## API / Interface Design

**Before:**
```csharp
public interface IFinancialAnalysisService
{
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(...);
    Task RefreshFinancialDataAsync(...);
    FinancialAnalysisCacheStatus GetCacheStatus();   // <-- remove
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(...);
}
```

**After:**
```csharp
public interface IFinancialAnalysisService
{
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(...);
    Task RefreshFinancialDataAsync(...);
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(...);
}
```

`FinancialAnalysisService.GetCacheStatus()` changes from `public FinancialAnalysisCacheStatus GetCacheStatus()` to `private FinancialAnalysisCacheStatus GetCacheStatus()`. No HTTP endpoint, MediatR request/response, or event contract is added, removed, or modified. No changes to `FinancialOverviewController`, `FinancialOverviewModule.cs`, or the OpenAPI-generated client are required.

## Dependencies
- None beyond the existing `FinancialOverview` module code (`IFinancialAnalysisService`, `FinancialAnalysisService`, `FinancialAnalysisCacheStatus`) and its existing test project (`Anela.Heblo.Tests`).
- No new packages, services, or infrastructure required.

## Out of Scope
- Adding any new admin/monitoring HTTP endpoint or `ICacheMonitor` interface to expose cache status externally. The finding's suggested fix explicitly defers this: "If a monitoring/admin HTTP endpoint for cache status is added in future, that is the right time to put it back on the interface (or expose it through a narrower `ICacheMonitor` interface)." This spec does not implement that future endpoint.
- Any change to the cache-status computation logic itself (the 24-month lookback estimation, cache key naming, or `IMemoryCache` usage).
- Any change to `GetFinancialOverviewAsync`'s hybrid/cache/real-time branching behavior.
- Any change to `FinancialOverviewController`'s existing two endpoints.
- Any change to `FinancialAnalysisCacheStatus`'s shape or the `FinancialAnalysisOptions` configuration.

## Open Questions

None.

## Status: COMPLETE
