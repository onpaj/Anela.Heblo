# Design: Targeted FlexiBee ledger/lots latency work

## Component Design

### LedgerService (modified)
Responsibilities: cache, single-flight, timing/logging for `ucetni-denik/query`.
```
GetLedgerItems(dateFrom, dateTo, debit?, credit?, dept?, ct)
  key = existing format
  if cache.TryGetValue(key) -> return
  lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<IList<LedgerItem>>>(() => FetchAsync(...), ExecutionAndPublication))
  try   { return await lazy.Value.WaitAsync(ct); }
  catch (OperationCanceledException) when (ct.IsCancellationRequested) { log info (caller abort); throw; }
FetchAsync(...):
  sw = Stopwatch.StartNew()
  try { dto = await _ledgerClient.GetAsync(..., CancellationToken.None); map; cache.Set(key, result, ttl(dateTo)); log QueryCompleted; return }
  catch (OperationCanceledException ex) { log warning timeout (internal HttpClient timeout) incl. elapsed; throw }
  finally { _inflight.TryRemove(key, out _) }
ttl(dateTo) = dateTo.Date < firstOfMonth(timeProvider.GetLocalNow()) ? Closed : Open
```
Note: the "timed out vs caller canceled" distinction in the current code stays: an `OperationCanceledException` arriving while the caller token is not canceled is logged as the internal timeout warning.

### FlexiLedgerOptions (new, class)
`ClosedPeriodCacheMinutes` (default 360), `OpenPeriodCacheMinutes` (default 15). Section `FlexiLedger`.

### CatalogMetaRefreshService.RefreshLotsData (modified)
Stopwatch around `_lotsClient.GetAsync`; log `Catalog.LotsRefreshCompleted rows={Rows} elapsedMs={ElapsedMs}`.

### Log events
- `FlexiLedger.QueryCompleted from={From} to={To} spanDays={SpanDays} debit={Debit} credit={Credit} dept={Dept} rows={Rows} elapsedMs={ElapsedMs}`
- Existing timeout/cancel messages extended with `elapsedMs`.

## Data Schemas
No database, API or DTO changes.
Configuration:
```json
"FlexiLedger": { "ClosedPeriodCacheMinutes": 360, "OpenPeriodCacheMinutes": 15 }
```
