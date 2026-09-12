# Design: Atomic Transaction for Gift Package Manufacture/Disassembly Stock Operations

## Component Design

This change touches four existing components only — no new modules, classes, or files beyond a test.

### 1. `IRepository<TEntity, TKey>` (Xcc — `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`)

Contract addition, alongside the existing `// Unit of Work operations` member `SaveChangesAsync()`:

```csharp
Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<CancellationToken, Task<TResult>> operation,
    CancellationToken cancellationToken = default);
```

Responsibility: run `operation` as one all-or-nothing database transaction and return its result on
success. XML doc on the member must state explicitly that this wraps the **entire underlying
`DbContext`**, not just `TEntity` — the same whole-context semantics `SaveChangesAsync()` already
has by precedent — so a caller resolving `_giftPackageRepository` also covers writes made through any
other repository sharing that same `DbContext` instance in the DI scope (as this feature relies on for
`IStockUpOperationRepository`).

Every one of the 16 current `I*Repository : IRepository<T,K>` interfaces derives from
`BaseRepository<,>`, so all of them get a real, working implementation for free; the only other
implementer, `EmptyRepository<,>`, gets the no-op below. No direct `IRepository<,>` implementer exists
in the codebase today, so this is a pure additive member with no other blast radius.

### 2. `BaseRepository<TEntity, TKey>` (Persistence — `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`)

Real implementation, execution-strategy-safe under `PollyExecutionStrategy`
(`RetriesOnFailure => true`, which forbids calling `Context.Database.BeginTransactionAsync()` directly):

```csharp
public virtual async Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<CancellationToken, Task<TResult>> operation,
    CancellationToken cancellationToken = default)
{
    var strategy = Context.Database.CreateExecutionStrategy();
    return await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    });
}
```

Responsibility boundary: everything the transaction must cover (entity adds, `SaveChangesAsync` calls)
has to live *inside* `operation`, since `PollyExecutionStrategy` resets the change tracker and re-runs
the whole delegate on a transient retry — nothing may be staged before `ExecuteInTransactionAsync` is
called. The `catch { rollback; throw; }` must rethrow unchanged (bare `throw;`) so the exception
type/message a caller sees is identical to today's un-transacted failure path.

### 3. `EmptyRepository<TEntity, TKey>` (Xcc — `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`)

Trivial pass-through, no real transaction:

```csharp
public Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<CancellationToken, Task<TResult>> operation,
    CancellationToken cancellationToken = default)
    => operation(cancellationToken);
```

Responsibility: keep this stub compiling and behaving as a no-op, consistent with its role as an
unwired "not yet implemented" placeholder (it has no consumers today). XML doc must carry a one-line
caveat that this provides no isolation if ever mixed with real repositories in the same logical
operation — it only "succeeds" because it has nothing of its own to roll back.

### 4. `GiftPackageManufactureService` (Application — `.../GiftPackageManufacture/Services/GiftPackageManufactureService.cs`)

Consumer of the new contract via its existing `IGiftPackageManufactureRepository` dependency — no new
dependency is introduced, and `IGiftPackageManufactureService`'s public method signatures are
unchanged.

**`CreateManufactureAsync`** — reordered so the transaction's lifetime covers only DB writes:
1. `GetGiftPackageDetailAsync(giftPackageCode, ...)` (cross-module reads via `IManufactureClient`,
   `ILogisticsCatalogSource`) — moved to run **before** the transaction opens (previously ran after the
   log's commit).
2. `_giftPackageRepository.ExecuteInTransactionAsync(async ct => { ... }, cancellationToken)` wraps:
   add + save `GiftPackageManufactureLog` (still first, to obtain the DB-generated `Id` used in
   `DocumentNumber`); per ingredient, `AddConsumedItem` + `_stockOperationService.CreateOperationAsync`
   (stock-down); `_stockOperationService.CreateOperationAsync` for the output product (stock-up); return
   the mapped `GiftPackageManufactureDto`.
3. Commit on success; rollback + rethrow unchanged on any exception from step 2's body.

**`DisassembleGiftPackageAsync`** — no reordering needed (validation and detail-fetch already precede
any write). The transaction opens right after validation, immediately before `disassemblyLog` is
constructed, and wraps: log creation + save, the package stock-down operation, and the per-component
stock-up loop, returning the mapped `GiftPackageDisassemblyDto`.

**Cross-repository dependency (load-bearing invariant):** the transaction is opened via
`_giftPackageRepository` (`IGiftPackageManufactureRepository : BaseRepository<GiftPackageManufactureLog, int>`)
but must also cover writes made through the separately-injected `IStockUpOperationRepository` (reached
via `ILogisticsStockOperationService` → `LogisticsStockOperationAdapter` → `IStockUpProcessingService`
→ `_repository.AddAsync`/`SaveChangesAsync`). This works only because both repositories resolve to the
same `Scoped ApplicationDbContext` instance within one request/DI scope. Add a code comment at the
`GiftPackageManufactureService` call site naming this cross-repository dependency explicitly, so a
future per-module-`DbContext` refactor (Phase 2) is forced to re-examine this call site.

**Unaffected downstream component:** `IStockUpProcessingService.ProcessPendingOperationsAsync` and its
Pending → Submitted → Completed/Failed state machine (driven by a separate recurring background refresh
task) are untouched — `CreateOperationAsync` still only writes a `Pending` `StockUpOperation` row inside
the new transaction; the Shoptet call remains fully decoupled from this feature's scope.

## Data Schemas

No schema changes and no API/contract changes.

- No new or modified database tables, columns, or EF Core migration. `GiftPackageManufactureLog`,
  `GiftPackageManufactureItem`, and `StockUpOperation` keep their existing shapes and relationships
  (including the `StockUpOperation.SourceType`/`SourceId` convention-based, non-FK link back to the log).
- No changes to any REST endpoint, controller, or MediatR request/response contract
  (`CreateGiftPackageManufactureRequest/Response`, `DisassembleGiftPackageRequest/Response`) and no
  changes to `GiftPackageManufactureDto` / `GiftPackageDisassemblyDto` shapes.
- No changes to `IGiftPackageManufactureService`'s public method signatures.
- The only new interface surface in the entire change is the internal, implementation-layer method
  signature below, added to `IRepository<TEntity, TKey>` and not exposed through any module contract:

  ```csharp
  Task<TResult> ExecuteInTransactionAsync<TResult>(
      Func<CancellationToken, Task<TResult>> operation,
      CancellationToken cancellationToken = default);
  ```
