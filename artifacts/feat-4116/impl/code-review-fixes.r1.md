# Code review fix pass — round 1

All three Blocking findings from `code-review.r1.md` are fixed. All three live in
`BaseRepository.ExecuteInTransactionAsync`
(`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`).

## Finding 1 — change tracker not reset between execution-strategy retries

`Context.ChangeTracker.Clear()` is now the first statement inside the
`strategy.ExecuteAsync` lambda, before `BeginTransactionAsync`. Every attempt therefore
starts from an empty tracker, so a retry can neither re-insert the failed attempt's
leftover `Added` entity (carrying its stale, rolled-back key) nor silently skip the
entities that attempt had already accepted as `Unchanged`.

This is safe for both current call sites: `CreateManufactureAsync` and
`DisassembleGiftPackageAsync` create every entity they write *inside* the delegate, and
everything they compute before it (BOM/catalog DTOs from `IManufactureClient` /
`ILogisticsCatalogSource`) is not EF-tracked.

The XML doc was rewritten: it previously claimed "a retry re-runs the whole delegate from
a clean change tracker", which is false for EF Core. It now states that EF Core does *not*
reset the tracker, that this method clears it explicitly, and spells out the resulting
contract — the delegate must re-create everything it writes on every invocation, and any
entity tracked by the caller before the call is discarded.

The same false claim in `PollyExecutionStrategy`'s class comment
(`Infrastructure/Resilience/PollyExecutionStrategy.cs:9`, pre-existing and named by the
review as the source of the wrong claim) was corrected too. That change is **comment
only** — no behaviour change to the strategy.

## Finding 2 — wrong `strategy.ExecuteAsync` overload

Switched from `ExecuteAsync(Func<Task<TResult>>)` to
`ExecuteAsync(async ct => { … }, cancellationToken)`. The caller's token now reaches the
execution strategy (so an aborted request cancels the retry loop and its backoff delays),
and the per-attempt token the strategy's `AddTimeout` cancels is threaded into
`BeginTransactionAsync`, `operation(ct)` and `CommitAsync` instead of being discarded in
favour of the captured outer token. A timed-out attempt can no longer keep running against
the same scoped `ApplicationDbContext` while the next attempt starts on it.

`PollyExecutionStrategy.ExecuteAsync` already supplies that per-attempt `ct`
(`Pipeline.ExecuteAsync(async ct => await operation(ctx, state, ct), cancellationToken)`),
so the token now flows end to end.

## Finding 3 — explicit `RollbackAsync` replacing the original exception

The `catch { await transaction.RollbackAsync(...); throw; }` block is gone. `await using var
transaction` already rolls an uncommitted transaction back on dispose, so the rollback still
happens, but a `RollbackAsync` that throws on a broken connector can no longer replace the
original exception. This restores FR-1's "exception type/message surfaced to the caller on
failure is unchanged" criterion and keeps the strategy's transient classification seeing the
real cause. A comment at the call site records why there is deliberately no catch.

## Verification

New real-Postgres test in
`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`:

`CreateManufactureAsync_TransientFailureOnFinalSave_RetryCommitsExactlyOneConsistentSet`

The existing `ThrowOnNthSaveInterceptor` gained an optional exception-factory parameter so
it can throw a *transient* exception (`TimeoutException`, transient per
`TransientErrorClassifier`) instead of the default non-transient one. Its call counter keeps
running across attempts, so the throw happens exactly once: attempt 1 fails on save #4 (the
output stock-up) and rolls back, and attempt 2 (saves #5–#8) is allowed to succeed. The test
asserts exactly one log row, two items, and exactly three stock operations, all with
`SourceId` equal to the committed log's id.

**Negative control run.** The `ChangeTracker.Clear()` line was temporarily commented out, the
solution rebuilt, and the test re-run. It fails with exactly the corruption the review
predicted:

```
Expected allStockOps to contain 3 item(s) because the retry must not also flush the
failed attempt's leftover stock-up, but found 4:
    DocumentNumber = "GPM-000001-SET001",  SourceId = 1,   <-- orphan, log 1 was rolled back
    DocumentNumber = "GPM-000002-ING001",  SourceId = 2,
    DocumentNumber = "GPM-000002-ING002",  SourceId = 2,
    DocumentNumber = "GPM-000002-SET001",  SourceId = 2,
```

The fix was then restored and everything re-run green. So finding 1 is **demonstrated, not
just argued**: the committed orphan `StockUpOperation` whose `SourceId` points at a
non-existent log row is reproduced on real Postgres without the fix and absent with it.

### What this verification does and does not prove

- **Proves:** the retry-after-transient-failure path is exercised end to end against real
  Postgres, the tracker-reset fix is load-bearing for it, and the resulting committed state
  satisfies the NFR-3 invariant (no stock operation referencing a rolled-back log).
- **Does not prove:** findings 2 and 3 are not covered by a dedicated test. Both are about
  failure modes that need a genuinely broken connector or a real per-attempt timeout to
  reproduce — a `TimeoutRejectedException` from Polly's `AddTimeout` firing mid-`SaveChanges`,
  and a `RollbackAsync` that throws on a dead Npgsql connector. Simulating either faithfully
  needs connector-level fault injection (e.g. a proxy that kills the TCP connection
  mid-statement) that this repo has no harness for, and a crude simulation would assert the
  mock rather than the behaviour. They are verified by inspection: the token now flows into
  the delegate and out to `operation`/`BeginTransactionAsync`/`CommitAsync`, and there is no
  longer any code path in which an exception thrown during rollback can replace the original.

### Gates

| Gate | Result |
|---|---|
| `dotnet build Anela.Heblo.sln` | 0 errors |
| `dotnet format --verify-no-changes` | clean |
| non-Integration (`Category!=Integration`) | 7303 passed, 0 failed (baseline 7303) |
| Integration (`Category=Integration`) | 209 passed, 0 failed (baseline 208 + the new test) |

### Not done (Advisory, not Blocking)

The two Advisory findings were left alone as out of scope for this fix pass: collapsing
round-trips via `IStockUpProcessingService.StageOperationAsync`, and documenting that
`EmptyRepository` has no production DI registration.
