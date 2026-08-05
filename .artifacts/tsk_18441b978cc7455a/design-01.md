# Design — Atomic, idempotent Receive for TransportBoxes

No UI is involved: this is a backend persistence-correctness fix inside one MediatR handler and its
collaborating services. The `ChangeTransportBoxState` request/response contract, the endpoint, and
the frontend are all unchanged.

## 1. Key decision: collapse two `SaveChangesAsync` calls into one, don't add a transaction API

`ITransportBoxRepository` and `IStockUpOperationRepository` are both `BaseRepository<T,TKey>` over
the **same scoped `ApplicationDbContext`** (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:16-23`).
`AddAsync` only calls `DbSet.AddAsync` (stages an entity in the change tracker); nothing is written to
the DB until `SaveChangesAsync` runs (`BaseRepository.cs:57-61,97-100`). The bug is not "we lack a
transaction" — it's that `StockUpProcessingService.CreateOperationAsync` calls `SaveChangesAsync`
**immediately** for every operation (`StockUpProcessingService.cs:36-37`), flushing the stock-up rows
to the DB in their own commit, before the handler even starts the box's own state transition.

If we simply **stop flushing early** and let the staged `StockUpOperation` adds sit in the shared
`ApplicationDbContext`'s change tracker until the handler's existing final
`_repository.SaveChangesAsync(cancellationToken)` (`ChangeTransportBoxStateHandler.cs:135`) runs, that
one call now persists **both** the box row and the `StockUpOperation` rows in a single `SaveChanges`
batch. EF Core wraps a multi-statement `SaveChangesAsync` call in an implicit database transaction by
default — no explicit `BeginTransactionAsync`/`CommitAsync` required, and, critically, this implicit
transaction is created *through* the DbContext's configured `IExecutionStrategy`, so it is already
compatible with the app's custom `PollyExecutionStrategy`
(`backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`,
`RetriesOnFailure => true`). This resolves the plan's open question about execution-strategy
compatibility: we deliberately **do not** call `Database.BeginTransactionAsync` anywhere, so we never
hit EF Core's "the configured execution strategy does not support user-initiated transactions"
restriction. Only *user*-initiated `BeginTransaction`/`TransactionScope` needs the
`CreateExecutionStrategy().ExecuteAsync(...)` wrapper; an implicit single-`SaveChanges` transaction
does not.

This is the narrowest possible fix: no new transaction abstraction, no `IUnitOfWork`, no change to
`Handle`'s control flow or the other five transitions. It also naturally satisfies "transaction scope
as narrow as correctness allows" — the scope is exactly one `SaveChangesAsync` call, same as today.

## 2. Component design

### 2.1 New idempotent staging method (replaces the plain create call, Received path only)

Add **one** new method to the existing contracts. It does not replace `CreateOperationAsync` — that
stays exactly as-is for `GiftPackageManufactureService`, which has no retry/atomicity requirement in
this finding's scope and must keep its current immediate-commit-per-call semantics.

`backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationService.cs`
```csharp
public interface ILogisticsStockOperationService
{
    Task CreateOperationAsync(...);   // unchanged — immediate add + save, used by GiftPackageManufactureService

    /// <summary>
    /// Idempotently stages a StockUpOperation: no-op if a row with this DocumentNumber already
    /// exists, otherwise adds a new Pending operation to the current unit of work WITHOUT saving.
    /// The caller commits it together with its own aggregate's SaveChangesAsync.
    /// </summary>
    Task StageOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        LogisticsStockOperationSource sourceType,
        int sourceId,
        CancellationToken cancellationToken = default);
}
```

`backend/src/Anela.Heblo.Application/Features/Catalog/Services/IStockUpProcessingService.cs` /
`StockUpProcessingService.cs` — mirror method, same name, using the existing
`IStockUpOperationRepository.GetByDocumentNumberAsync` (already exists, no repository change needed):

```csharp
public async Task StageOperationAsync(
    string documentNumber, string productCode, int amount,
    StockUpSourceType sourceType, int sourceId, CancellationToken ct = default)
{
    var existing = await _repository.GetByDocumentNumberAsync(documentNumber, ct);
    if (existing != null)
    {
        _logger.LogInformation(
            "StockUpOperation {DocumentNumber} already exists (state {State}) — skipping duplicate staging on retry",
            documentNumber, existing.State);
        return;
    }

    var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);
    await _repository.AddAsync(operation, ct);
    // Deliberately no SaveChangesAsync: caller (ChangeTransportBoxStateHandler) commits this
    // together with the box's own state-transition save.
}
```

`LogisticsStockOperationAdapter` gets a one-line pass-through (same shape as its existing
`CreateOperationAsync`, mapping `LogisticsStockOperationSource` → `StockUpSourceType`).

No change to `IStockUpOperationRepository` or `StockUpOperationRepository` — `GetByDocumentNumberAsync`
and `AddAsync` already exist and already do exactly what's needed (plain read, plain stage-without-save).

### 2.2 Handler change — `HandleReceived`

`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs:246`
— swap the call:

```csharp
foreach (var group in aggregated)
{
    var documentNumber = $"BOX-{box.Id:000000}-{group.ProductCode}";

    await _stockOperationService.StageOperationAsync(   // was: CreateOperationAsync
        documentNumber,
        group.ProductCode,
        group.Amount,
        LogisticsStockOperationSource.TransportBox,
        box.Id,
        cancellationToken);

    _logger.LogDebug(...);   // unchanged
}
```

Nothing else in `HandleReceived` or `Handle` changes. The existing `transition.ChangeStateAsync` at
`:126` is pure in-memory (`TransportBoxTransition.ChangeStateAsync` just invokes a delegate and
returns — verified, no DB access, `TransportBoxTransition.cs:26-30`), so there's no ordering hazard:
by the time `_repository.SaveChangesAsync(cancellationToken)` runs at `:135`, the change tracker holds
both the staged `StockUpOperation` adds and the box's `Modified` state — one flush, one transaction.

### 2.3 Sequence — before vs. after

```
BEFORE (bug)                                  AFTER (fix)
──────────────                                ──────────────
HandleReceived:                               HandleReceived:
  for each product group:                       for each product group:
    AddAsync(StockUpOperation)                    if GetByDocumentNumberAsync(doc) exists: skip
    SaveChangesAsync()  ──► COMMIT #1             else AddAsync(StockUpOperation)   [no save]
Handle:                                       Handle:
  transition.ChangeStateAsync(box)              transition.ChangeStateAsync(box)
  repository.UpdateAsync(box)                   repository.UpdateAsync(box)
  repository.SaveChangesAsync() ──► COMMIT #2   repository.SaveChangesAsync() ──► single COMMIT
                                                 (box + all staged StockUpOperations together,
                                                  implicit EF transaction, rolls back as one unit
                                                  on any failure)
```

### 2.4 Failure and idempotency semantics (replaces the old "commit then maybe wedge" behavior)

| Scenario | Behavior after fix |
|---|---|
| Happy path, first Receive | All product `StockUpOperation`s staged, box transitioned, one commit. Both visible together or neither is — no window with one but not the other. |
| `SaveChangesAsync` fails (any reason: DB blip, unique-index collision, concurrency conflict) | Whole batch rolls back: **zero** `StockUpOperation` rows written, box state **unchanged** (still `InTransit`/`Reserve`/`Quarantine`). Falls into the existing `catch (Exception)` → `TransportBoxStateChangeError` (unchanged, per FR-3 — no new error contract needed). |
| Operator retries Receive after the above | For each product, `StageOperationAsync` finds no existing row (none survived the rollback) → stages fresh → succeeds normally. |
| Retry against a box that has a **pre-existing** `StockUpOperation` from *before* this fix shipped (already-committed legacy wedge) | `StageOperationAsync` finds the row by `DocumentNumber`, logs and skips it; other products (if any weren't yet created) get staged; one commit transitions the box. No duplicate, no unique-constraint exception. |
| Two concurrent Receive calls for the same box (should not happen via UI, but not prevented at the DB layer) | Both read "no existing row", both stage, one's `SaveChangesAsync` wins, the other's throws a unique-constraint `DbUpdateException` on the shared index — caught generically, that request's box save also rolls back (same all-or-nothing batch), so the loser is left unchanged and safely retryable (it will then see the winner's rows and skip them). The unique index remains the concurrency backstop; the pre-check exists purely to avoid the common non-concurrent retry case throwing at all. |

## 3. Data model

No schema changes. `StockUpOperation.DocumentNumber` (`BOX-{box.Id:000000}-{productCode}`) continues
to be the idempotency key, backed by the existing unique index
(`IX_StockUpOperations_DocumentNumber_Unique`,
`backend/src/Anela.Heblo.Persistence/Catalog/Stock/StockUpOperationConfiguration.cs:52-55`), which now
plays two roles: (1) idempotency key looked up by `GetByDocumentNumberAsync` before staging, and (2)
the pre-existing concurrent-write safety net.

No new request/response/event payloads — `ChangeTransportBoxStateRequest`/`Response` are unchanged.

## 4. Scope confirmation

- **`GiftPackageManufactureService`**: untouched. It keeps calling `CreateOperationAsync` (immediate
  add + save per call), which is unchanged. It is not proven to share this atomicity bug in this
  finding's investigation — flagged in the plan as a candidate separate finding, not fixed here.
- **`TransportBoxCompletionService`**: untouched — only reconciles already-`Received` boxes; not
  affected by how Received got there.
- **Other five transitions** (`New→Opened`, `Opened→Reserve`, `Opened→Quarantine`, and the reverse
  `Opened→New` inventory-restore path): untouched. They don't call `_stockOperationService` at all, so
  they're structurally unaffected by this change.
- No data migration for pre-existing wedged boxes — out of scope, confirmed as an ops follow-up.

## 5. Testing design

Mocked unit tests (existing `ChangeTransportBoxStateHandlerTests`, using
`Mock<ILogisticsStockOperationService>`) can verify that `HandleReceived` calls `StageOperationAsync`
(not `CreateOperationAsync`) with the right arguments per aggregated product group — but they **cannot**
verify atomicity or real rollback behavior, because that's a property of EF Core + Postgres, not of
handler control flow. Per FR-1/FR-2's acceptance criteria, the real proof needs a real database:

**New integration test** (real `ApplicationDbContext` + real repositories against Postgres via the
existing `PostgresSharedContainerFixture` pattern used elsewhere, e.g.
`GetStockUpOperationsSummaryIntegrationTests.cs` — no mocks for the persistence layer):

1. **Atomicity and idempotent-recovery — two focused tests:**
   - **Test A — atomicity on genuine failure.** Seed a box with product lines A, B, C in `InTransit`.
     Register an EF Core `SaveChangesInterceptor` (test-only, added via
     `AddInterceptors` on a dedicated `DbContextOptions` for this test) that throws on the *first*
     `SavingChangesAsync` for this context, simulating a transient DB failure (e.g. a dropped
     connection) at the exact point the handler's single `SaveChangesAsync` would fire. Call `Handle`
     (Received transition) end-to-end and assert: the response is `Success = false` /
     `TransportBoxStateChangeError` (existing generic-catch behavior, FR-3), the box is still
     `InTransit` in the DB, and **zero** `StockUpOperation` rows exist for A, B, or C — proving the
     staged-but-uncommitted operations rolled back together with the box, not just that the box save
     failed after operations were already durable (the old bug's failure mode).
   - **Test B — idempotent retry.** Seed a `StockUpOperation` row for product A only (`DocumentNumber`
     matching what the handler would generate — representing a legacy wedge from before this fix, or
     the aftermath of Test A's failure on a real transient error that *did* partially apply outside
     this app's control), box in `InTransit` with products A, B, C. Call `Handle` with no interceptor.
     Assert: `Success = true`, box is now `Received`, exactly one `StockUpOperation` row per product
     (A's is the pre-seeded row, untouched; B and C are newly created), no exception surfaced.
2. **Regression test:** the other five transitions still do a single `SaveChangesAsync` with no
   `_stockOperationService` involvement — covered by the existing unit tests, no new test needed beyond
   confirming they still pass unchanged.
3. **Unit test updates** to `ChangeTransportBoxStateHandlerTests`: replace
   `_stockUpProcessingServiceMock.Setup(x => x.CreateOperationAsync(...))` with a `StageOperationAsync`
   setup; add a test asserting `CreateOperationAsync` is never called for the Received path (guards
   against silently reverting to the old, non-idempotent method).
4. **`StockUpProcessingServiceTests`**: add unit tests for the new `StageOperationAsync` — (a) no
   existing row → repository `AddAsync` called, `SaveChangesAsync` **not** called; (b) existing row
   found → `AddAsync` not called, method returns without throwing.

## 6. Answers to the plan's open questions

- **`EnableRetryOnFailure`/execution-strategy compatibility**: confirmed via `PersistenceModule.cs` —
  the app uses a custom `PollyExecutionStrategy` (not EF's built-in `EnableRetryOnFailure`), registered
  as the Npgsql execution strategy. Because this design uses only an *implicit* transaction (one
  `SaveChangesAsync` call, no `BeginTransactionAsync`), it never triggers EF Core's "execution strategy
  doesn't support user-initiated transactions" guard — no `IExecutionStrategy.ExecuteAsync` wrapping
  needed in application code.
- **Idempotency mechanism**: pre-check via the existing `GetByDocumentNumberAsync`, wrapped in a new
  `StageOperationAsync` method — not a caught unique-constraint exception. Confirms the plan's default.
- **`GiftPackageManufactureService`**: left untouched, not proven to share the bug, flagged as a
  candidate follow-up finding rather than expanded into this change.
- **Reconciling pre-existing wedged boxes**: out of scope, ops/data follow-up, not part of this fix.
