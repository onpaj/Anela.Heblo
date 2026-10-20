## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/LedgerService.cs`

## Coverage
Line coverage: 52.3% (filter threshold: 60%)

## What's not tested
1. **Cache hit path** — when `_cache.TryGetValue` succeeds, the method returns early without calling `_ledgerClient.GetAsync`. No test verifies that a warm cache prevents a redundant FlexiBee call.
2. **Internal HttpClient timeout** — the first `catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)` handles FlexiBee's internal timeout (the HttpClient deadline fires before the caller cancels). This is logged as a warning and rethrown. No test distinguishes it from the second case.
3. **Caller abort** — the second `catch (OperationCanceledException)` handles a caller-initiated cancel. This is logged at Info level and rethrown. No test verifies the correct log level or that the caller's token state is checked correctly.
4. **GetCosts daily aggregation** — the GroupBy + Sum that computes per-day cost statistics by department is not exercised by any test, so an off-by-one in the date grouping or incorrect Sum would go undetected.

## Why it matters
The two `OperationCanceledException` catches have different log levels (Warning vs Info) and different diagnostic messages. If the `when` predicate is wrong, an internal timeout is silently mis-classified as a client abort, making operational alerting fire at the wrong severity. The cache path is the hot path in production; a regression there causes redundant FlexiBee API calls on every request.

## Suggested approach
Unit test with a mocked `ILedgerClient` and a real `MemoryCache`:
- Case: second call with same key → `ILedgerClient.GetAsync` called once only
- Case: `GetAsync` throws `OperationCanceledException` with `CancellationToken.None` → caught by the `when` filter, logged as warning, rethrown
- Case: `GetAsync` throws `OperationCanceledException` with a cancelled token → caught by the second handler, logged as info, rethrown
- Case: GetCosts with mixed-date ledger items → correct per-day Cost aggregation
~2 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
