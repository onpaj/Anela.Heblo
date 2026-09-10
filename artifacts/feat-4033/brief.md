## Module
FinancialOverview

## Finding
`IFinancialAnalysisService.GetCacheStatus()` (line 30) is declared on the public interface but has no external consumer:
- `FinancialOverviewController` does not expose it via any HTTP endpoint.
- The background refresh task registered in `FinancialOverviewModule.cs` does not call it.
- No test references `GetCacheStatus()` through the `IFinancialAnalysisService` interface.
- The implementation class calls `GetCacheStatus()` on `this` — a self-call, not polymorphic dispatch through the interface.

File: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs:30`

## Why it matters
A method on a public interface is a contract every implementor must fulfil and every test-double must stub. When no caller ever invokes `GetCacheStatus()` through the interface, the contract is pure noise. Any future mock or alternative implementation is forced to implement a method that serves only internal book-keeping of `FinancialAnalysisService`. This is an ISP breach (interface segregation): the interface is wider than any single caller needs.

## Suggested fix
Remove `GetCacheStatus()` from the interface. Make it a `private` method (or an `internal` one if the class is tested directly). The self-calls inside `GetFinancialOverviewAsync` require no interface dispatch — they already call `this.GetCacheStatus()` — so removing it from the interface does not change any runtime behaviour.

If a monitoring/admin HTTP endpoint for cache status is added in future, that is the right time to put it back on the interface (or expose it through a narrower `ICacheMonitor` interface).

---
_Filed by daily arch-review routine on 2026-09-01._
