# Implementation Plan: Targeted FlexiBee ledger/lots latency work

Spec: `spec.r1.md`; architecture: `arch-review.r1.md`; design: `design.r1.md`. Backend only; no DTO/OpenAPI/migration changes. `TimeProvider.System` is already registered in `FlexiAdapterServiceCollectionExtensions.cs` (line ~61). Existing LedgerService consumers are tested in `backend/test/Anela.Heblo.Tests/**` via `ILedgerService` fakes; there is no existing direct `LedgerService` test, so new tests go in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Ledger/`.

### task: ledger-options-and-di
Add `FlexiLedgerOptions` and wire it in DI.
- Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/FlexiLedgerOptions.cs`: class (not record) with `const string SectionName = "FlexiLedger"`, `int ClosedPeriodCacheMinutes = 360`, `int OpenPeriodCacheMinutes = 15`.
- In `FlexiAdapterServiceCollectionExtensions.cs` add `services.Configure<FlexiLedgerOptions>(configuration.GetSection(FlexiLedgerOptions.SectionName))` next to the `ILedgerService` registration (use whatever `IConfiguration` handle that method already has; keep defaults in code, no appsettings change required).
- Verify: `dotnet build backend` succeeds.

### task: ledger-service-singleflight-ttl-logging
Rework `LedgerService.GetLedgerItems` per `design.r1.md` (FR-1, FR-3, FR-4).
- Constructor gains `IOptions<FlexiLedgerOptions>` and `TimeProvider` (keep null checks). Update DI (singleton already resolves them) and any test constructors found via `grep -rn "new LedgerService" backend/test`.
- Add `ConcurrentDictionary<string, Lazy<Task<IList<LedgerItem>>>> _inflight` (`LazyThreadSafetyMode.ExecutionAndPublication`).
- Flow: cache hit returns; else `GetOrAdd` the lazy; `await lazy.Value.WaitAsync(cancellationToken)`. A caller's `OperationCanceledException` with its token canceled logs the existing "canceled by the caller" info message (add elapsedMs) and rethrows.
- `FetchAsync`: Stopwatch; call `_ledgerClient.GetAsync(..., cancellationToken: CancellationToken.None)`; map; `_cache.Set` with TTL = Closed minutes when `dateTo.Date < first day of month of _timeProvider.GetLocalNow()` else Open minutes; emit `LogInformation("FlexiLedger.QueryCompleted from={From} to={To} spanDays={SpanDays} debit={Debit} credit={Credit} dept={Dept} rows={Rows} elapsedMs={ElapsedMs}", ...)`; on internal timeout `OperationCanceledException` keep the existing warning text plus elapsedMs and rethrow; `finally` removes the key from `_inflight`. Failures are never cached.
- Check consumers do not mutate the shared returned list (grep callers of `GetLedgerItems`/`GetCosts`: CostPoolService, FinancialAnalysisService, SalesCostProvider, FlatManufactureCostProvider); if any mutate, return a read-only wrapper.
- Do not change `GetCosts`/`GetDirectCosts`/`GetPersonalCosts` semantics or the `ILedgerService` signature.
- Verify: `dotnet build backend`, `dotnet format backend --verify-no-changes`.

### task: ledger-service-tests
Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Ledger/LedgerServiceTests.cs` (xUnit/Moq style used in that project; fake `TimeProvider`, real `MemoryCache`, mocked `ILedgerClient`, AutoMapper config as other tests do).
- N=10 parallel identical calls -> `ILedgerClient.GetAsync` called exactly once; all callers get the same list.
- Failing/canceled shared request is not cached; subsequent call retries (client called again).
- Caller cancels its token while another caller waits: canceling caller gets `OperationCanceledException`, the other caller still receives the result.
- TTL: window ending before current-month start uses closed TTL, window touching current month uses open TTL (advance fake time to prove expiry boundaries).
- `FlexiLedger.QueryCompleted` logged on success with rows/span (use a capturing logger); timeout and caller-cancel exceptions rethrown.
- FR-5: add a test in `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/` (or beside `FinancialAnalysisServiceTests.cs`) wiring real `LedgerService` over a counting fake `ILedgerClient` and asserting calls <= distinct (window, prefix, department) tuples for a `FinancialAnalysisService` run. If the existing service structure makes this impractical, assert the same through repeated `LedgerService.GetLedgerItems` calls and state why in the test file.
- Verify: `dotnet test backend --filter "LedgerService|FinancialAnalysis"` passes.

### task: lots-refresh-logging
FR-2 in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs`.
- In `RefreshLotsData`: Stopwatch around `_lotsClient.GetAsync(cancellationToken: ct)`, materialize list, `_cacheStore.SetLotsData(list)`, then `_logger.LogInformation("Catalog.LotsRefreshCompleted rows={Rows} elapsedMs={ElapsedMs}", ...)`. Exceptions propagate unchanged (no try/catch).
- Add a test in the existing Catalog tests area (find with `grep -rl CatalogMetaRefreshService backend/test`; create `CatalogMetaRefreshServiceLotsTests.cs` if none) asserting the event is emitted once with row count and that a throwing client propagates.
- Verify: `dotnet test backend --filter CatalogMetaRefresh` passes.

### task: docs-and-process-docs
FR-6 and process-doc gate.
- `docs/integrations/flexi-api.md`: add "Known slow endpoints" section: per-endpoint table from `brief.md` (ucetni-denik/query, sarze-expirace.json, stav-skladu-k-datu/query, uzivatelsky-dotaz/21/call.json), measures taken (FR-1..FR-5), what is FlexiBee-side/not fixable, follow-up option (read from `flexi_raw.ledger_entry`), Application Insights re-measurement query by endpoint, and the decision rule: after one release cycle, if `ucetni-denik/query` p95 > 10 s, close the signal as an accepted third-party ceiling instead of re-filing.
- Update `docs/processes/calc-margins.md` and `docs/processes/calc-financial-overview.md` (ledger cache semantics: single-flight, closed 6 h / open 15 min TTL, `FlexiLedger` config) and the doc(s) owning `CatalogMetaRefreshService.cs` (`sync-catalog-master-data.md`, plus `calc-dqt-lot-stock.md`/`sync-catalog-prices.md` if `python3 scripts/process-docs/check.py` lists them as owners): lots refresh log event. Bump `verified_at`; add Runtime facts/Known quirks entries.
- Verify: `python3 scripts/process-docs/check.py index` passes and the check reports no owned-code-without-doc failures for the changed files.

### task: final-validation
- `dotnet build backend` and `dotnet format backend --verify-no-changes` clean.
- All tests touched pass (Adapters.Flexi.Tests, Anela.Heblo.Tests filters above, plus `Shared/CostPools` and `Features/Catalog` suites that construct ledger consumers).
- Re-read the diff: every changed line traces to FR-1..FR-6; no timeout/circuit-breaker edits; no secrets or amounts in logs.
