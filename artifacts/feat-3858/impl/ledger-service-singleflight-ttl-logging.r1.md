# ledger-service-singleflight-ttl-logging r1
- LedgerService now takes IOptions<FlexiLedgerOptions> and TimeProvider (null-checked); DI already resolves both (TimeProvider.System singleton, options configured in prior task).
- Added ConcurrentDictionary<string, Lazy<Task<IList<LedgerItem>>>> single-flight (ExecutionAndPublication); callers await WaitAsync(ct); caller abort logs info (with ElapsedMs) and rethrows.
- FetchAsync: Stopwatch, client called with CancellationToken.None, cache TTL Closed/Open minutes by dateTo vs first of current local month, logs FlexiLedger.QueryCompleted, timeout warning gets ElapsedMs, finally removes key. Failures never cached.
- Consumers (CostPoolService, FinancialAnalysisService, Sales/FlatManufacture providers via GetCosts) only read/Where/GroupBy the list; no mutation, so no read-only wrapper.
- Updated existing LedgerServiceTests constructor. Build 0 errors; touched files format-clean; LedgerServiceTests 2/2 pass.
