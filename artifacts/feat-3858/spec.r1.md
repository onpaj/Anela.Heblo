# Specification: Reduce FlexiBee ucetni-denik / sarze-expirace tail latency by targeting the slow calls

## Summary
Two earlier dependency-wide interventions (#2987 timeout tuning, #3759 circuit breaker) did not move FlexiBee p95/p99 because the tail is concentrated in two endpoints: `POST ucetni-denik/query` (p95 ~23-24 s, ~2,950 calls/week) and `GET sarze-expirace.json` (p95 ~19 s, ~171 calls/week). This feature makes those two call paths observable per call, removes avoidable calls (cache stampedes, duplicate and overlapping pulls, over-wide windows), and, for whatever remains FlexiBee-side, records an explicit accepted latency ceiling so the signal is not re-filed a fourth time.

## Background
- `LedgerService.GetLedgerItems` (Adapters.Flexi) is the single live path to `ucetni-denik/query`. It has a 15-minute `IMemoryCache` entry keyed by `ledger_{from}_{to}_{debit}_{credit}_{dept}` and no single-flight protection, so concurrent identical requests each hit FlexiBee.
- Callers: `FinancialAnalysisService` (two calls per month, debit and credit, in parallel, for live/partial cells and cache refresh), `CostPoolService.ComputeAsync` (whole-month range), `SalesCostProvider` and `FlatManufactureCostProvider` (margin calculation, `GetCosts`/`GetDirectCosts`). Each distinct (range, prefix, department) combination is a separate cache key and a separate FlexiBee query.
- `sarze-expirace.json` is served by `FlexiLotsClient` (SDK `ILotsClient.GetAsync`), called with no product code and `limit = 0` from `CatalogMetaRefreshService.RefreshLotsData` (full pull of all lots, registered as a catalog refresh task in `CatalogModule`). 171 calls/week matches a scheduled refresh, not user traffic. `uzivatelsky-dotaz/21/call.json` (171 calls/week, p95 10 s) shows the same cadence.
- `LedgerSyncService` (nightly analytics sync) also queries `ucetni-denik` but is a batch job on its own schedule; it is included in telemetry attribution but not re-engineered here.
- The telemetry is per-URL dependency data from Application Insights; the repo has no per-call context (date range, prefixes, row count) attached to these dependency calls today, which is why the earlier fixes were blind.

## Functional Requirements

### FR-1: Per-call profiling of ucetni-denik queries
Log (structured, `LogInformation`) every uncached `ucetni-denik/query` execution in `LedgerService` with: date from/to, span in days, debit prefixes, credit prefixes, department, rows returned, elapsed ms, and the caller-provided cancellation outcome (ok / timeout / caller-canceled). Do not change existing timeout/cancel handling semantics.
**Acceptance criteria:**
- A log event `FlexiLedger.QueryCompleted` is emitted on success with all fields above.
- Timeout and caller-cancel paths still log as today and additionally include elapsed ms.
- Unit tests assert the event on success and that the exceptions are still rethrown.

### FR-2: Per-call profiling of lot (sarze-expirace) pulls
`CatalogMetaRefreshService.RefreshLotsData` logs elapsed ms and row count (`Catalog.LotsRefreshCompleted`).
**Acceptance criteria:** event emitted once per refresh with `rows` and `elapsedMs`; failures propagate unchanged.

### FR-3: Single-flight for identical ledger queries
Concurrent `GetLedgerItems` calls with the same cache key must share one in-flight FlexiBee request (per-key async lock or `Lazy<Task>` pattern) and populate the cache once.
**Acceptance criteria:**
- N parallel calls with an identical key result in exactly one `ILedgerClient.GetAsync` call.
- If the shared request fails or is canceled, the failure is not cached and a later call retries.
- One caller canceling its own token must not cancel the shared request for the others (the shared request uses its own lifetime bounded by the existing HttpClient timeout), while the canceling caller still observes `OperationCanceledException`.

### FR-4: Longer cache lifetime for closed periods
Ledger results for a window that ends before the start of the current month are immutable for practical purposes (accounting periods closed) and are cached for 6 hours instead of 15 minutes. Windows touching the current month keep 15 minutes. TTLs are configuration (`FlexiLedger:ClosedPeriodCacheMinutes`, `FlexiLedger:OpenPeriodCacheMinutes`) with those defaults.
**Acceptance criteria:**
- Window ending before the current month start uses the closed-period TTL; any other window uses the open-period TTL (tested with a fake `TimeProvider`).
- Defaults preserve today's behavior for the open period.

### FR-5: Cache warm path for the heaviest consumers is not duplicated
Verify and, where a duplicate exists, remove calls that fetch the same data twice within one request: e.g. `FinancialAnalysisService` live path issues debit and credit pulls for prefixes `5` and `6` separately from the cache-refresh path. Where the same window and prefix set was already fetched within the TTL, the cache (FR-3/FR-4) must satisfy it; no behavioral change to results.
**Acceptance criteria:** an integration-style test over `FinancialAnalysisService` with a counting fake `ILedgerClient` shows no more FlexiBee calls than distinct (window, prefix, department) tuples.

### FR-6: Accepted latency ceiling documented
Add a section to `docs/integrations/flexi-api.md` ("Known slow endpoints") recording the measured per-endpoint latency (table from the brief), what was done (FR-1..FR-5), what is FlexiBee-side and not fixable, and a re-measurement procedure (Application Insights query by endpoint). State the decision rule: if after one release cycle `ucetni-denik/query` p95 is still above 10 s, the signal is closed as an accepted third-party ceiling rather than re-filed.
**Acceptance criteria:** doc section present; `docs/processes/calc-margins.md` and `docs/processes/calc-financial-overview.md` updated (cache semantics, single-flight, TTLs) with `verified_at` bumped; `python3 scripts/process-docs/check.py index` passes.

## Non-Functional Requirements

### NFR-1: Performance
Target: number of `ucetni-denik/query` dependency calls per week falls measurably (goal: at least 30% fewer than the 2,954 baseline) and concurrent duplicates drop to zero. Per-call FlexiBee latency is not promised to improve; it is FlexiBee-side.

### NFR-2: Security
No new endpoints, secrets or PII in logs. Account prefixes and dates are not sensitive; contact names and amounts are not logged.

### NFR-3: Compatibility
`ILedgerService` signature unchanged. No DTO or OpenAPI changes. No DB migrations.

## Data Model
No persistent data changes. In-memory only: cache entries (existing key format retained) and an in-flight map `ConcurrentDictionary<string, Lazy<Task<IList<LedgerItem>>>>`.

## API / Interface Design
No HTTP API changes. New configuration section `FlexiLedger` (`ClosedPeriodCacheMinutes` = 360, `OpenPeriodCacheMinutes` = 15) bound via `IOptions<FlexiLedgerOptions>`. New log events listed in FR-1 and FR-2.

## Dependencies
Rem.FlexiBeeSDK `ILedgerClient`/`ILotsClient` (unchanged), `IMemoryCache`, `TimeProvider`, Application Insights for post-release verification.

## Out of Scope
- Changing the SDK or FlexiBee server-side filters/indexes.
- Timeout or circuit-breaker tuning (explicitly rejected by the brief).
- Moving margin/financial calculations to the `flexi_raw` analytics store (larger architectural change; recorded as a follow-up option in the doc).
- Re-engineering `LedgerSyncService` or `stav-skladu-k-datu/query`.
- Changing the lots refresh cadence (documented, not altered).

## Open Questions
None.

Assumptions made: (1) the 171 weekly `sarze-expirace`/`uzivatelsky-dotaz` calls come from the scheduled catalog refresh, so they are measured, not reduced; (2) closed accounting periods are stable enough for a 6 h cache; late postings into a closed month are visible after at most 6 h, which is acceptable for the margin and overview use cases.

## Status: COMPLETE
