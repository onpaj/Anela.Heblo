# Architecture Review: Atomic Transaction for Gift Package Manufacture/Disassembly

## Skip Design: true

Backend-only persistence/transaction fix. No controller contract, DTO shape, or UI/UX change of any
kind — confirmed by reading the spec's API/Interface Design section (explicitly "no changes to any
REST endpoint... or MediatR request/response contract") and the service/handler code itself.

## Architectural Fit Assessment

The spec's diagnosis is correct and its chosen remedy (Option A, a single DB transaction per
use-case method) is the right scope for this defect. Two things anchor that judgment in the actual
code rather than the spec's narrative:

1. **`CreateOperationAsync` really is a pure local write.** `StockUpProcessingService.CreateOperationAsync`
   (`backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpProcessingService.cs:22-42`)
   only constructs a `StockUpOperation` and calls `_repository.AddAsync` + `SaveChangesAsync` against
   `IStockUpOperationRepository : BaseRepository<StockUpOperation, int>`. The Shoptet call
   (`IEshopStockDomainService.StockUpAsync`) lives in `ProcessOperationAsync`, reached only from the
   separate `ProcessPendingOperationsAsync` background path. Option B (status field + reconciliation)
   is correctly ruled out of scope for this defect.

2. **There is closer, better prior art in this codebase than the spec cites.** The spec's Dependencies
   section claims `PostgresSharedContainerFixture` is "already used by `KnowledgeBaseRepositoryIntegrationTests`/
   `LeafletRepositoryIntegrationTests`" — that's factually wrong (see Specification Amendments); those two
   spin up their own dedicated `pgvector/pgvector:pg16` Testcontainer and never touch the shared fixture
   or `[Collection("PostgresIntegration")]`. The real precedent, missed by the spec, is
   `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`
   — it proves atomicity of a state-mutating log/entity *plus* a batch of `StockUpOperation` rows
   against real Postgres, using `PostgresSharedContainerFixture`, a raw-SQL minimal schema, and a
   `SaveChangesInterceptor` to inject a failure mid-write. This is materially the same shape of problem
   (a log/state row + N `StockUpOperation` rows must commit or roll back together) and should be the
   template copied, not `KnowledgeBase`/`Leaflet`.

   Worth noting: that existing test's own doc-comment says the atomicity there is "a property of EF
   Core's implicit `SaveChanges` transaction... not of handler control flow" — i.e. it achieves
   atomicity by batching everything into **one** `SaveChangesAsync()` call, not an explicit
   `BeginTransactionAsync`/`CommitAsync`. That technique does **not** transfer to
   `GiftPackageManufactureService` unchanged, because `CreateManufactureAsync`/`DisassembleGiftPackageAsync`
   have a genuine chicken-and-egg dependency the transport-box case doesn't: the `DocumentNumber`
   strings (`GPM-{logId:000000}-...`) require the DB-generated `Id` of the log row *before* the
   stock operations that reference it can be constructed, which forces a first `SaveChangesAsync` to
   materialize the ID. Two `SaveChangesAsync` calls cannot be made atomic by batching alone — an
   explicit transaction spanning both is genuinely required here. This confirms Option A (not a
   "single `SaveChangesAsync`" refactor) is the correct shape for this specific case, while the
   `ChangeTransportBoxState...` test remains the right *test-authoring* template.

Integration point: `GiftPackageManufactureService` holds `IGiftPackageManufactureRepository` and (via
`ILogisticsStockOperationService` → `LogisticsStockOperationAdapter` → `IStockUpProcessingService`)
indirectly drives `IStockUpOperationRepository`. Both repositories are `BaseRepository<,>`-backed,
resolved from the same `Scoped` `ApplicationDbContext` (`PersistenceModule.AddDbContext`) within one
request/DI scope. A transaction opened via either repository's `Context.Database` therefore covers
writes made through the other — this is the load-bearing invariant the whole design depends on (see
Risks).

## Proposed Architecture

### Component Overview

```
CreateGiftPackageManufactureHandler / DisassembleGiftPackageHandler  (MediatR, unchanged)
            │
            ▼
GiftPackageManufactureService                                        (Application)
    ├─ GetGiftPackageDetailAsync()  ── IManufactureClient, ILogisticsCatalogSource   (moved OUTSIDE tx)
    │                                                                      │
    └─ _giftPackageRepository.ExecuteInTransactionAsync(async ct => {     │  cross-module reads,
           add + save GiftPackageManufactureLog        ──┐               │  no DB txn held open
           foreach ingredient:                            │  same
             AddConsumedItem                               │  ApplicationDbContext
             _stockOperationService.CreateOperationAsync ──┼─► IStockUpOperationRepository
           CreateOperationAsync (output product)          │      (BaseRepository<StockUpOperation,int>)
       })                                                 ┘
            │
            ▼
IGiftPackageManufactureRepository : BaseRepository<GiftPackageManufactureLog,int>
            │
            ▼
ApplicationDbContext (Npgsql, PollyExecutionStrategy)
            │
   CreateExecutionStrategy().ExecuteAsync(async () => {
       await using var tx = await Database.BeginTransactionAsync(ct);
       try { var r = await operation(ct); await tx.CommitAsync(ct); return r; }
       catch { await tx.RollbackAsync(ct); throw; }
   })
```

`IRepository<TEntity,TKey>` gains `ExecuteInTransactionAsync<TResult>`; `BaseRepository<,>` gets the
real implementation (inherited by all 16 current `IRepository<,>` derivatives for free);
`EmptyRepository<,>` gets a pass-through no-op.

### Key Design Decisions

#### Decision 1: `ExecuteInTransactionAsync` on `IRepository<TEntity,TKey>` vs. a narrower `IUnitOfWork`

**Options considered:**
- (a) Add `ExecuteInTransactionAsync<TResult>` to the generic `IRepository<TEntity,TKey>` (the spec's proposal).
- (b) Introduce a new, narrow `IUnitOfWork`/`IDbContextTransactionScope` in `Xcc`, implemented once
  against `ApplicationDbContext`, injected directly into `GiftPackageManufactureService` alongside its
  existing repository dependency.

**Chosen approach:** (a), as the spec proposes. Do not introduce a separate `IUnitOfWork`.

**Rationale:** The task description asks me to weigh this against "how many other classes implement
`IRepository<,>` directly rather than deriving from `BaseRepository`" — I grepped this exhaustively
(`: IRepository<` and `: BaseRepository<` across `backend/src`) and found **zero** direct
implementers. Every one of the 16 `I*Repository : IRepository<T,K>` interfaces
(`IIssuedInvoiceRepository`, `IPurchaseOrderRepository`, `IDqtRunRepository`,
`IGiftPackageManufactureRepository`, `ITransportBoxRepository`, `IMaterialContainerRepository`,
`ILotRepository`, `IManufacturedProductInventoryRepository`, `IStockTakingRepository`,
`IMarketingActionRepository`, `IJournalRepository`, `IJournalTagRepository`,
`IPackingMaterialAllocationRepository`, `IPackingMaterialRepository`,
`IImportedMarketingTransactionRepository`, `IStockUpOperationRepository`) is backed by a concrete
class deriving from `BaseRepository<,>`. The only other `IRepository<,>` implementer at all is
`EmptyRepository<,>` itself, and it has **zero consumers anywhere in the codebase** today (grepped
`EmptyRepository<` — the only hit is its own class declaration; it's vestigial "not yet implemented"
scaffolding, not something wired into DI or a test today). So the premise that this addition risks
breaking "many other classes implementing `IRepository<,>` directly" does not hold in the current
codebase — the blast radius of an additive interface member here is exactly one base class plus one
unused stub.

More importantly, the interface already carries a method with the exact same "looks per-entity, is
actually whole-`DbContext`" shape: `SaveChangesAsync()` is declared under the `// Unit of Work
operations` comment in `IRepository.cs` and, in `BaseRepository.SaveChangesAsync`, calls
`Context.SaveChangesAsync()` — flushing **every** tracked entity in the shared `ApplicationDbContext`,
not just `TEntity`'s. The codebase has therefore already decided, by precedent, that
`IRepository<TEntity,TKey>` doubles as the ambient unit-of-work handle for Phase 1's single shared
`ApplicationDbContext` (per `development_guidelines.md`'s "Generic repository abstraction in Xcc,
implementation in Persistence layer" / "Current State (Phase 1): Single `ApplicationDbContext`").
Adding `ExecuteInTransactionAsync` alongside `SaveChangesAsync` is consistent with that existing
(admittedly leaky) convention, not a new violation of it. Introducing a parallel `IUnitOfWork`
abstraction now would give this codebase two different Unit-of-Work-shaped interfaces solving the
same single-`DbContext` problem, which is worse than the mild interface bloat of one additive method.

Caveat for later, not now: `development_guidelines.md` calls out a **Phase 2** where "each module
will have its own `DbContext`." At that point `SaveChangesAsync`'s whole-context semantics *and*
`ExecuteInTransactionAsync`'s whole-context semantics both need re-examination together — that's a
reason to revisit this interface as a pair when Phase 2 happens, not a reason to solve it differently
today.

#### Decision 2: `EmptyRepository<,>`'s no-op transaction semantics

**Chosen approach:** `return await operation(cancellationToken);` as the spec proposes, with no real
transaction.

**Rationale:** Confirmed by grep that `EmptyRepository<,>` has no current consumer in production code
or tests (its doc-comment "useful for testing or when a repository is not yet implemented" describes
an intended future use that hasn't materialized). A no-op wrapper is safe *for that reason*: there is
nothing today that depends on `EmptyRepository`'s `ExecuteInTransactionAsync` providing real rollback.
Flag for the implementer: if `EmptyRepository<,>` is ever wired up as a stand-in for one repository in
a scenario that mixes it with *other*, real repositories in the same logical operation, its
`ExecuteInTransactionAsync` provides no isolation for those other repositories' writes — it only
"succeeds" because there's nothing of its own to roll back. Add a one-line XML-doc caveat on the
`EmptyRepository` method saying exactly that, so a future consumer doesn't assume it gives real
atomicity across a mixed repository set.

#### Decision 3: EF Core execution-strategy-safe transaction pattern

**Chosen approach:** confirmed correct, with one required refinement below.

**Rationale:** `PollyExecutionStrategy.RetriesOnFailure => true` (`PollyExecutionStrategy.cs:31`) makes
`Context.Database.BeginTransactionAsync()` illegal to call directly — exactly as the spec states; this
is standard EF Core behavior for any retrying `IExecutionStrategy`. The correct pattern is to put
`BeginTransactionAsync`/commit/rollback **and every write the transaction must cover** inside the
delegate passed to `CreateExecutionStrategy().ExecuteAsync(...)`, so that a retry re-runs the whole
unit of work from a clean slate:

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

Two things the implementer must get right that the spec doesn't spell out at this level of detail:

- **Everything the transaction must cover has to be inside `operation`, not staged before the call.**
  `PollyExecutionStrategy`'s own doc-comment says "EF Core resets its change tracker before each retry
  so transient mid-call failures replay safely" — that guarantee only holds for entities added/tracked
  *inside* the retried delegate. FR-1/FR-2 already put log creation and all stock operations inside
  `operation` (steps 3–6), which is correct; just don't let a future edit hoist any `AddAsync`/tracking
  call above the `ExecuteInTransactionAsync` call.
- **`catch { rollback; throw; }` must rethrow unchanged** — FR-1's acceptance criterion ("the exception
  type/message surfaced to the caller ... is unchanged") is satisfied by a bare `throw;`, not
  `throw ex;` or a wrapping exception.

#### Decision 4: FR-1's reordering (BOM/ingredient read before the transaction) is achievable

Confirmed against the current control flow of `CreateManufactureAsync`
(`GiftPackageManufactureService.cs:139-205`). `GetGiftPackageDetailAsync` (line 162) does not depend
on `manufactureLog.Id` or any state produced by the log's `SaveChangesAsync` — it only needs
`giftPackageCode`, which is a method parameter available from the start. Moving the call to before log
creation is a pure reordering with no data dependency to work around; the ingredient loop that
currently follows it is unaffected because it only reads from the already-fetched `giftPackage.Ingredients`
and the log's `Id` (available after the log's `SaveChangesAsync`, which still happens first inside the
transaction, per FR-1 step 3). `DisassembleGiftPackageAsync` already fetches detail and validates
before creating the log, so FR-2's "no reordering needed" claim is also confirmed correct as-is.

## Implementation Guidance

### Directory / Module Structure

No new modules or folders for production code — this is a same-file change in four existing files:

- `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs` — add `ExecuteInTransactionAsync<TResult>`.
- `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs` — real implementation (Decision 3).
- `backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs` — no-op pass-through (Decision 2), plus
  the one-line XML-doc caveat.
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
  — reorder `CreateManufactureAsync` per FR-1, wrap both methods' write sequences in
  `_giftPackageRepository.ExecuteInTransactionAsync(...)`.

New test file (see Decision/Amendment on location):

- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`
  (new subfolder, mirroring how `Transport`'s equivalent test lives at
  `Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`), using
  `[Collection("PostgresIntegration")]` + `PostgresSharedContainerFixture` + a
  `SaveChangesInterceptor`-based failure injection, following that file's pattern line for line
  (raw-SQL `CREATE TABLE IF NOT EXISTS` for `GiftPackageManufactureLogs`, `GiftPackageManufactureItems`,
  `StockUpOperations` — column shapes are in `GiftPackageManufactureLogConfiguration.cs` /
  `GiftPackageManufactureItemConfiguration.cs` / the existing `StockUpOperations` DDL already present
  in `GetStockUpOperationsSummaryIntegrationTests.cs`/`ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`).

### Interfaces and Contracts

```csharp
// Xcc/Persistance/IRepository.cs
Task<TResult> ExecuteInTransactionAsync<TResult>(
    Func<CancellationToken, Task<TResult>> operation,
    CancellationToken cancellationToken = default);
```

Document on the interface member itself (XML doc) that this wraps the **entire underlying
`DbContext`**, not just `TEntity` — i.e. calling it on `_giftPackageRepository` also covers writes made
through any other repository sharing the same `DbContext` instance in that DI scope (as
`GiftPackageManufactureService` relies on for `IStockUpOperationRepository`). This is the same
whole-context caveat that already implicitly applies to `SaveChangesAsync` and should be spelled out
once, on both members, so a future caller doesn't assume per-entity isolation that was never real.

`GiftPackageManufactureService` call sites (shape only, not full code):

```csharp
return await _giftPackageRepository.ExecuteInTransactionAsync(async ct =>
{
    await _giftPackageRepository.AddAsync(manufactureLog, ct);
    await _giftPackageRepository.SaveChangesAsync(ct);
    foreach (var ingredient in giftPackage.Ingredients ?? []) { /* AddConsumedItem + CreateOperationAsync */ }
    await _stockOperationService.CreateOperationAsync(/* output product */);
    return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);
}, cancellationToken);
```

### Data Flow

**CreateManufactureAsync:**
1. `GetGiftPackageDetailAsync(giftPackageCode, ...)` — cross-module reads (`IManufactureClient`,
   `ILogisticsCatalogSource`), outside any DB transaction.
2. `ExecuteInTransactionAsync` opens the transaction (execution-strategy-safe).
3. Inside: create + save `GiftPackageManufactureLog` (gets `Id`); per ingredient, `AddConsumedItem` +
   `CreateOperationAsync` (stock-down); `CreateOperationAsync` for the output product (stock-up).
4. Commit on success (mapped DTO returned); rollback + rethrow unchanged on any exception in step 3.

**DisassembleGiftPackageAsync:** validation and detail-fetch already precede any write (unchanged);
transaction opens right after, wraps log creation + package stock-down + per-component stock-up loop,
commits/rolls back the same way.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing mocked unit tests in `GiftPackageManufactureServiceTests.cs` call `CreateManufactureAsync`/`DisassembleGiftPackageAsync` against `Mock<IGiftPackageManufactureRepository>`. Moq does not auto-invoke a delegate parameter — an unstubbed `ExecuteInTransactionAsync<TResult>` returns `default` (`null` `Task<TResult>`), and awaiting it throws `NullReferenceException`, breaking every existing test that reaches this code path. | High (build/test breakage, not called out explicitly by the spec's FR-3 acceptance criteria) | Add, in every affected test's setup, `_giftPackageRepositoryMock.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<GiftPackageManufactureDto>>>(), It.IsAny<CancellationToken>())).Returns<Func<CancellationToken, Task<GiftPackageManufactureDto>>, CancellationToken>((op, ct) => op(ct));` (one overload per return type used) so mocked tests keep exercising the wrapped body. |
| A test that constructs `ApplicationDbContext` via `new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options` (the pattern used by every existing integration test in this repo, including the `ChangeTransportBoxState...` template) never calls `npgsql.ExecutionStrategy(...)`, so `Context.Database.CreateExecutionStrategy()` returns EF Core's **default, non-retrying** strategy — which already allows direct `BeginTransactionAsync`. FR-3 acceptance criterion (c) ("does not throw `InvalidOperationException`... when run against ... `PollyExecutionStrategy`") would pass trivially without ever exercising the actual risk the feature exists to avoid. | High (the one test that's supposed to prove the whole reason for `CreateExecutionStrategy().ExecuteAsync` would silently test nothing) | See Specification Amendments — the new integration test must explicitly configure `.UseNpgsql(cs, npgsql => npgsql.ExecutionStrategy(deps => new PollyExecutionStrategy(deps, provider, metrics, logger)))` with a manually-constructed `DbResiliencePipelineProvider`/`DbResilienceMetrics`/`NullLogger`, not the bare options builder every other test in the repo uses. |
| `ExecuteInTransactionAsync` opened via `_giftPackageRepository` implicitly covers writes made through the separately-injected `IStockUpOperationRepository` (via `ILogisticsStockOperationService`) only because both resolve to the *same* `Scoped ApplicationDbContext` instance in this DI scope. That invariant is correct today but is not enforced by any type — a future refactor (e.g. Phase 2's per-module `DbContext`) could silently break atomicity without a compile error. | Medium | The whole-context XML-doc caveat (Implementation Guidance) plus a code comment at the `GiftPackageManufactureService` call site naming the cross-repository dependency explicitly, so Phase-2 module-`DbContext` work is forced to re-examine this call site. |
| `EmptyRepository<,>`'s no-op `ExecuteInTransactionAsync` provides no real isolation if ever mixed with real repositories in the same operation. | Low (currently zero consumers of `EmptyRepository<,>` exist) | One-line XML-doc caveat (Decision 2); no functional change needed now. |

## Specification Amendments

1. **Wrong integration-test precedent cited.** The spec's Dependencies section states
   `PostgresSharedContainerFixture` is "already used by `KnowledgeBaseRepositoryIntegrationTests`/
   `LeafletRepositoryIntegrationTests`." Verified false: both of those spin up their own dedicated
   `pgvector/pgvector:pg16` `PostgreSqlContainer` per test class and never reference
   `PostgresSharedContainerFixture` or `[Collection("PostgresIntegration")]`. The actually-relevant,
   and materially closer, precedent is
   `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateReceiveAtomicityIntegrationTests.cs`
   — same fixture, same collection, same "log/state row + N `StockUpOperation` rows must be atomic"
   shape, same `SaveChangesInterceptor` failure-injection technique. Use it as the template instead.

2. **New integration test must actually configure `PollyExecutionStrategy` on the test `DbContext`.**
   None of the existing Postgres integration tests (including the recommended template) configure
   `npgsql.ExecutionStrategy(...)` — they all use EF Core's default strategy. Add this explicitly as an
   FR-3 acceptance requirement: the new test's `DbContextOptionsBuilder` must wire a real
   `PollyExecutionStrategy` (via a manually-constructed `DbResiliencePipelineProvider` — itself
   constructible from `Options.Create(new DbResilienceOptions())`, a `DbResilienceMetrics` needing only
   a trivial `IMeterFactory` stub, and `NullLogger<T>.Instance` — none of it needs a full DI container).
   Without this, acceptance criterion (c) is unverifiable by the described test.

3. **Data Model section names the wrong FK property.** The spec's Data Model section calls it
   "`LogId` FK." The actual property (`GiftPackageManufactureItem.cs`) and column
   (`GiftPackageManufactureItemConfiguration.cs`) are both named `ManufactureLogId`. Minor, but will
   mislead whoever writes the integration test's raw-SQL schema or seed code if not corrected.

4. **Add an explicit unit-test-update requirement to FR-1/FR-2's acceptance criteria.** "Verified by
   existing/updated unit tests" understates the required change: every existing mocked test exercising
   `CreateManufactureAsync`/`DisassembleGiftPackageAsync` must add an `ExecuteInTransactionAsync`
   pass-through stub or it will fail with `NullReferenceException`, not merely "need updating" in some
   soft sense (see Risks).

## Prerequisites

None. No schema change, no new migration, no new DI registration beyond what `PersistenceModule`
already wires for `PollyExecutionStrategy` in production. The only setup work is test-side: the new
integration test project must be able to construct a `PollyExecutionStrategy` instance outside full DI
(straightforward — all three of its dependencies are cheaply constructible, per Amendment 2).
