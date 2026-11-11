### task: ledger-service-singleflight-ttl-logging
Rework `LedgerService.GetLedgerItems` per `design.r1.md` (FR-1, FR-3, FR-4).
- Constructor gains `IOptions<FlexiLedgerOptions>` and `TimeProvider` (keep null checks). Update DI (singleton already resolves them) and any test constructors found via `grep -rn "new LedgerService" backend/test`.
- Add `ConcurrentDictionary<string, Lazy<Task<IList<LedgerItem>>>> _inflight` (`LazyThreadSafetyMode.ExecutionAndPublication`).
- Flow: cache hit returns; else `GetOrAdd` the lazy; `await lazy.Value.WaitAsync(cancellationToken)`. A caller's `OperationCanceledException` with its token canceled logs the existing "canceled by the caller" info message (add elapsedMs) and rethrows.
- `FetchAsync`: Stopwatch; call `_ledgerClient.GetAsync(..., cancellationToken: CancellationToken.None)`; map; `_cache.Set` with TTL = Closed minutes when `dateTo.Date < first day of month of _timeProvider.GetLocalNow()` else Open minutes; emit `LogInformation("FlexiLedger.QueryCompleted from={From} to={To} spanDays={SpanDays} debit={Debit} credit={Credit} dept={Dept} rows={Rows} elapsedMs={ElapsedMs}", ...)`; on internal timeout `OperationCanceledException` keep the existing warning text plus elapsedMs and rethrow; `finally` removes the key from `_inflight`. Failures are never cached.
- Check consumers do not mutate the shared returned list (grep callers of `GetLedgerItems`/`GetCosts`: CostPoolService, FinancialAnalysisService, SalesCostProvider, FlatManufactureCostProvider); if any mutate, return a read-only wrapper.
- Do not change `GetCosts`/`GetDirectCosts`/`GetPersonalCosts` semantics or the `ILedgerService` signature.
- Verify: `dotnet build backend`, `dotnet format backend --verify-no-changes`.

