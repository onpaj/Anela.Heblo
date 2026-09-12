# Specification: Atomic Transaction for Gift Package Manufacture/Disassembly Stock Operations

## Summary
`GiftPackageManufactureService.CreateManufactureAsync` and `DisassembleGiftPackageAsync` each split one logical operation across multiple independently-committed `SaveChangesAsync` calls: the `GiftPackageManufactureLog` is committed first, then stock operations are created one by one in a loop. If any step after the log commit throws, the log row is left permanently orphaned with no (or partial) matching stock movements, and nothing detects or repairs it. This spec wraps each method's full unit of work in a single database transaction so the log and every stock operation it produces commit or roll back together — Option A from the brief. Investigation confirmed the stock-operation write path performs no external call that would be incompatible with a local DB transaction, so the heavier Option B (status field + background reconciliation) is not required for this defect.

## Background
`GiftPackageManufactureService` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`) implements two related operations, both reachable synchronously from an HTTP request via MediatR (`CreateGiftPackageManufactureHandler` → `CreateManufactureAsync`; `DisassembleGiftPackageHandler` → `DisassembleGiftPackageAsync`) — neither is queued through Hangfire, so there is no automatic background retry of a failed call; a retry only happens if a human clicks the action again.

**`CreateManufactureAsync` (lines 139–205 in the current file):**
1. Constructs a `GiftPackageManufactureLog` and calls `_giftPackageRepository.AddAsync` + `SaveChangesAsync` (lines 155–156) — committed immediately, comment "CRITICAL: Save the log FIRST to get the ID for DocumentNumber" (line 154).
2. Fetches the BOM/ingredient detail via `GetGiftPackageDetailAsync` (line 162) — this calls `IManufactureClient.GetSetPartsAsync` and `ILogisticsCatalogSource.GetCatalogItemAsync`, i.e. reads from other modules, **after** the log is already committed.
3. Loops over ingredients, calling `_stockOperationService.CreateOperationAsync` once per ingredient (lines 176–182) to record stock-down.
4. Calls `CreateOperationAsync` once more for the output product stock-up (lines 193–199).

**`DisassembleGiftPackageAsync` (lines 207–298):** validates quantity and fetches gift-package detail first (lines 213–227), then creates the `GiftPackageManufactureLog` (Disassembly type) and commits it (lines 238–239, same "CRITICAL" comment at line 237), then creates one stock-down operation for the package (lines 250–256) and one stock-up operation per returned component in a loop (lines 272–278).

In both methods, any exception raised after the log's `SaveChangesAsync` (e.g. a transient DB error, a constraint violation, an out-of-range amount) propagates up through the method — it is not swallowed — but the `GiftPackageManufactureLog` row it left behind is not rolled back. The caller sees the operation fail and, having no way to know the log and some stock operations already persisted, may retry the whole action from the UI; the retry creates a **second** log with a **new** ID and repeats the ingredient consumption/production a second time, compounding the original inconsistency rather than just leaving an orphan. `GiftPackageManufactureLog` (`backend/src/Anela.Heblo.Domain/Features/Logistics/GiftPackageManufacture/GiftPackageManufactureLog.cs`) has no `Status`/`IsComplete` field, and there is no reconciliation job scanning these logs for partial execution.

**Does `CreateOperationAsync` call anything external?** `ILogisticsStockOperationService.CreateOperationAsync` (`LogisticsStockOperationAdapter`) delegates to `IStockUpProcessingService.CreateOperationAsync` (`StockUpProcessingService.cs`), which only constructs a `StockUpOperation` entity in `Pending` state and calls `_repository.AddAsync` + `SaveChangesAsync` — a pure DB write against the same `ApplicationDbContext`, no HTTP/queue call. The actual Shoptet e-shop call (`IEshopStockDomainService.StockUpAsync`) happens later, decoupled, inside `StockUpProcessingService.ProcessPendingOperationsAsync`, which is invoked by a recurring background refresh task (`CatalogModule.AddCatalogModule` → `RegisterRefreshTask<IStockUpProcessingService>`) and already implements its own Pending → Submitted → Completed/Failed state machine with per-operation error capture. That is, **the outbox pattern the brief describes as "Option B" already exists one layer down, at the `StockUpOperation` level**, and is unaffected by this change.

Both `IGiftPackageManufactureRepository` (`GiftPackageManufactureRepository : BaseRepository<GiftPackageManufactureLog, int>`) and `IStockUpOperationRepository` (`StockUpOperationRepository : BaseRepository<StockUpOperation, int>`) are backed by the same `ApplicationDbContext`, registered `Scoped` (`PersistenceModule.AddDbContext<ApplicationDbContext>`), and both are resolved within the same request/DI scope as `GiftPackageManufactureService`. This confirms a single ambient EF Core transaction spanning the log write and all `CreateOperationAsync` calls is achievable with today's architecture (ADR-001, single shared `ApplicationDbContext` for Phase 1) without introducing cross-module coupling beyond what already exists.

One infrastructure detail materially affects the implementation: the Npgsql `DbContext` is configured with `PollyExecutionStrategy` (`backend/src/Anela.Heblo.Persistence/Infrastructure/Resilience/PollyExecutionStrategy.cs`), which reports `RetriesOnFailure => true`. EF Core forbids calling `Database.BeginTransactionAsync()` directly under a retrying execution strategy — it throws `InvalidOperationException: The configured execution strategy ... does not support user-initiated transactions`. Any manual transaction here must be opened through `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`. There is no existing precedent for this pattern anywhere else in the codebase (`grep` for `CreateExecutionStrategy`/`BeginTransactionAsync` returns no hits), so this spec introduces it for the first time.

## Functional Requirements

### FR-1: Atomic persistence for `CreateManufactureAsync`
Wrap the log creation and every resulting stock operation in one database transaction so they commit or roll back as a unit.

Required reordering: `GetGiftPackageDetailAsync` (the BOM/ingredient read, which calls out to `IManufactureClient` and `ILogisticsCatalogSource`) must be fetched **before** the transaction opens, not after the log save as today. This keeps the transaction's lifetime limited to actual database writes and avoids holding a Postgres connection/transaction open across cross-module read calls. The resulting sequence:
1. Fetch gift-package detail (ingredients) — outside the transaction, as data needed to compute consumed quantities and document numbers.
2. Open a transaction (execution-strategy-safe, see FR-3).
3. Create and save the `GiftPackageManufactureLog` (unchanged: still needed first, to obtain the DB-generated `Id` used in `DocumentNumber`).
4. For each ingredient: call `AddConsumedItem` and `_stockOperationService.CreateOperationAsync` (stock-down), unchanged in shape from today.
5. Call `CreateOperationAsync` for the output product stock-up, unchanged in shape from today.
6. Commit the transaction.
7. On any exception in steps 3–5, roll back and rethrow the original exception unchanged (no swallowing, no new exception type) so existing caller/handler error handling is unaffected.

**Acceptance criteria:**
- A failure thrown by any `CreateOperationAsync` call (ingredient or output) results in **zero** `GiftPackageManufactureLog`, `GiftPackageManufactureItem`, or `StockUpOperation` rows persisted for that attempt — verified with an integration test against a real Postgres instance (see Dependencies) that injects a failure on the Nth stock operation and asserts no rows exist afterward.
- A successful call still produces exactly the same rows, with the same `DocumentNumber` format (`GPM-{logId:000000}-{productCode}`), as today — verified by existing/updated unit tests in `GiftPackageManufactureServiceTests.cs`.
- The returned `GiftPackageManufactureDto` and its shape are unchanged.
- `IGiftPackageManufactureService`'s public interface (method signatures) is unchanged — this is a purely internal implementation change.
- The exception type/message surfaced to the caller on failure is unchanged (whatever `CreateOperationAsync` or `SaveChangesAsync` throws today still propagates as-is).

### FR-2: Atomic persistence for `DisassembleGiftPackageAsync`
Apply the same wrapping to `DisassembleGiftPackageAsync`. Here the gift-package detail fetch and quantity validation already happen before the log is created, so no reordering is needed — the transaction opens right after validation/detail-fetch and before `disassemblyLog` is constructed, and wraps: log creation + save, the package stock-down operation, and the per-component stock-up loop, committing only after all of them succeed.

**Acceptance criteria:**
- A failure thrown by any `CreateOperationAsync` call (package stock-down or any component stock-up) results in zero `GiftPackageManufactureLog` (Disassembly), `GiftPackageManufactureItem`, or `StockUpOperation` rows persisted for that attempt — verified with an integration test analogous to FR-1's.
- A successful call still produces the same rows and `DocumentNumber` format (`GPD-{logId:000000}-{productCode}`) as today.
- `GiftPackageDisassemblyDto` and `IGiftPackageManufactureService`'s public interface are unchanged.
- Pre-transaction validation (`quantity <= 0`, `quantity > giftPackage.AvailableStock`) keeps throwing before any DB write, exactly as today.

### FR-3: Execution-strategy-safe transactional helper on the repository abstraction
Add a transaction-wrapping method to the generic repository abstraction so `GiftPackageManufactureService` does not need to depend on EF Core or `ApplicationDbContext` directly (preserving the existing "Application layer talks to repositories, not to `DbContext`" convention).

- Add to `IRepository<TEntity, TKey>` (`backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs`):
  `Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);`
- Implement in `BaseRepository<TEntity, TKey>` (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`) using `Context.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping `Context.Database.BeginTransactionAsync()` / `CommitAsync()` / `RollbackAsync()`, so it is safe under `PollyExecutionStrategy`'s retry-on-failure behavior.
- Update `EmptyRepository<TEntity, TKey>` (`backend/src/Anela.Heblo.Xcc/Persistance/EmptyRepository.cs`) with a trivial pass-through implementation (`return await operation(cancellationToken);`, no real transaction) so it keeps compiling and behaving as a no-op stub.
- `GiftPackageManufactureService` calls `_giftPackageRepository.ExecuteInTransactionAsync(async ct => { ...steps 3–6 of FR-1 / the disassembly equivalent... }, cancellationToken)`.

**Acceptance criteria:**
- A new method exists on `IRepository<TEntity, TKey>`; all existing concrete repositories deriving from `BaseRepository` get the real implementation for free; `EmptyRepository` compiles and behaves as a no-op.
- A focused unit/integration test on `BaseRepository.ExecuteInTransactionAsync` (or exercised transitively via FR-1/FR-2's integration tests) confirms: (a) on success, the delegate's writes are durably committed after the method returns; (b) on the delegate throwing, none of its writes are visible afterward; (c) it does not throw `InvalidOperationException` about unsupported user-initiated transactions when run against the real Npgsql provider with `PollyExecutionStrategy` configured.
- No other repository's behavior changes — this is an additive interface member with a working default-shaped implementation in the one base class almost everything derives from.

## Non-Functional Requirements

### NFR-1: Performance
- The added transaction should add only the cost of a `BEGIN`/`COMMIT` round-trip (single-digit milliseconds) per manufacture/disassembly call — no new network calls are introduced.
- The transaction's lifetime must be limited to DB writes only. External/cross-module reads (`GetGiftPackageDetailAsync`, i.e. `IManufactureClient`/`ILogisticsCatalogSource` calls) must happen **before** the transaction opens (FR-1's required reordering), so the transaction never holds a Postgres connection open across a slow external dependency.
- No change to Shoptet call volume or timing: `ProcessPendingOperationsAsync`'s cadence and the `StockUpOperation` state machine are untouched.

### NFR-2: Security
- No change to authentication/authorization — the same MediatR handlers and controller-level auth apply; this is a purely internal persistence-consistency fix.
- No new or more sensitive data is stored; no PII implications.

### NFR-3: Data integrity
- After this change, a `GiftPackageManufactureLog` row must never exist in the database without its full, matching set of `StockUpOperation` rows (all in `Pending` state, awaiting Shoptet submission by the existing background job) — either all of {log, items, stock operations} exist, or none do.
- This guarantee applies per manufacture/disassembly *attempt*; it does not make retries idempotent (a second manual retry after a genuine failure still creates a second, independent log — that is unchanged and out of scope, see Out of Scope).

## Data Model
No schema changes. Existing entities, unchanged:
- `GiftPackageManufactureLog` (`Id`, `GiftPackageCode`, `QuantityCreated`, `StockOverrideApplied`, `CreatedAt`, `CreatedBy`, `OperationType`, `ConsumedItems: List<GiftPackageManufactureItem>`).
- `GiftPackageManufactureItem` (child of the log, `LogId` FK, `ProductCode`, `QuantityConsumed`).
- `StockUpOperation` (`DocumentNumber`, `ProductCode`, `Amount`, `SourceType`, `SourceId` → the log's `Id`, `State`: Pending/Submitted/Completed/Failed) — the relationship to the log is by convention (`SourceType = GiftPackageManufacture`, `SourceId = logId`), not an enforced FK; this spec does not change that.

No new tables, columns, or EF Core migration are required.

## API / Interface Design
- No changes to any REST endpoint, controller, or MediatR request/response contract (`CreateGiftPackageManufactureRequest/Response`, `DisassembleGiftPackageRequest/Response` are untouched).
- No changes to `IGiftPackageManufactureService`'s public method signatures.
- New internal member: `IRepository<TEntity, TKey>.ExecuteInTransactionAsync<TResult>(...)` (Xcc), as specified in FR-3 — an implementation-layer addition, not exposed through any module contract.

## Dependencies
- EF Core + Npgsql provider, and specifically `PollyExecutionStrategy` (`Anela.Heblo.Persistence.Infrastructure.Resilience`) — the transaction must be opened via `CreateExecutionStrategy().ExecuteAsync(...)` to be compatible with it.
- `PostgresSharedContainerFixture` (`backend/test/Anela.Heblo.Tests/Common/PostgresSharedContainerFixture.cs`) — existing Testcontainers-based fixture, already used by `KnowledgeBaseRepositoryIntegrationTests`/`LeafletRepositoryIntegrationTests`, is the intended vehicle for the new rollback-verification integration tests (real transactional semantics cannot be verified with Moq-mocked repositories, which the existing `GiftPackageManufactureServiceTests.cs` uses).
- `IStockUpProcessingService` / `StockUpOperation`'s existing Pending→Submitted→Completed/Failed pipeline and its background refresh task registration (`CatalogModule.AddCatalogModule`) — depended upon, unmodified.

## Out of Scope
- **Option B (status field + reconciliation job) for `GiftPackageManufactureLog`.** Not needed: the write path has no external call that a local DB transaction can't cover, so Option A fully closes the finding. Revisit only if a future change makes `CreateOperationAsync` (or something in its call chain) perform a synchronous external call.
- Any change to `StockUpOperation`'s own state machine, retry policy, or `ProcessPendingOperationsAsync` cadence.
- Idempotent/duplicate-safe retries of `CreateManufactureAsync`/`DisassembleGiftPackageAsync` themselves (e.g. a client-supplied idempotency key to prevent a double-click or manual retry from creating two logs). The fix here guarantees each *attempt* is all-or-nothing; it does not deduplicate distinct attempts.
- Retrofitting the same split-transaction pattern found elsewhere in the codebase (this spec touches only `GiftPackageManufactureService`; any similar pattern in other services is a separate finding).
- UI/UX changes to how manufacture/disassembly failures are surfaced to the operator.

## Open Questions
None.

## Status: COMPLETE
