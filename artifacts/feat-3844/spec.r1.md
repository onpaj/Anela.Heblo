# Specification: Atomic and idempotent TransportBox Receive (fix non-atomic StockUpOperation creation)

## Summary
`ChangeTransportBoxStateHandler.HandleReceived` currently creates and commits one `StockUpOperation` per product (via `StockUpProcessingService.CreateOperationAsync`, which calls `SaveChangesAsync` immediately) *before* the box's own state transition is saved in a second, separate `SaveChangesAsync`. Because these are two independent commits with no enclosing transaction, a failure or crash between them leaves inventory-affecting `StockUpOperation` rows committed while the box is stuck in its pre-transition state, and a subsequent retry fails permanently on the `DocumentNumber` unique constraint. This spec defines the atomicity and idempotency guarantees the fix must provide so that a Receive operation either fully succeeds (box transitioned + operations created) or fully fails (nothing persisted), and so that retrying a partially-failed Receive is always safe.

## Background
Transport boxes move through a state machine (New → Opened → Reserve/Quarantine → InTransit → Received → Stocked/Closed, etc.), implemented in `TransportBox.TransitionNode` and driven by `ChangeTransportBoxStateHandler.Handle`. On the `InTransit|Reserve|Quarantine → Received` transitions, `HandleReceived` (`ChangeTransportBoxStateHandler.cs:273-305`) aggregates the box's line items by product code and calls `_stockOperationService.CreateOperationAsync(...)` once per distinct product (`:289`). This is a MediatR-callback step invoked from within `Handle` *before* `transition.ChangeStateAsync` (`:126`) runs and before the box is persisted via `_repository.UpdateAsync` + `_repository.SaveChangesAsync` (`:134-135`).

`CreateOperationAsync` is implemented by `LogisticsStockOperationAdapter` → `StockUpProcessingService.CreateOperationAsync` (`StockUpProcessingService.cs:22-42`), which does `_repository.AddAsync(operation, ct)` followed immediately by its own `await _repository.SaveChangesAsync(ct)`. This commits each `StockUpOperation` row to the database right away, independently of whatever happens afterward to the box.

Both `ITransportBoxRepository` (`TransportBoxRepository`) and `IStockUpOperationRepository` (`StockUpOperationRepository`) are `BaseRepository<TEntity,TKey>` implementations that wrap the same injected `ApplicationDbContext` (`PersistenceModule.cs` registers it via `AddDbContext`, which is Scoped by default; both repositories are registered `AddScoped` in their respective modules). Because MediatR resolves the handler and its dependencies from a single request scope, **within one `Handle` invocation both repositories share the exact same `ApplicationDbContext` instance**. This is a key fact for the fix: there is no cross-DbContext / distributed-transaction problem to solve — a single `DbContext`-level transaction (or a single `SaveChangesAsync` call covering both repositories' pending changes) is sufficient to make the two writes atomic.

A background job, `StockUpProcessingService.ProcessPendingOperationsAsync`, later picks up `Pending` operations and calls the Shoptet e-shop API to actually increase stock, independent of the box's state. `TransportBoxCompletionService.CompleteReceivedBoxesAsync` only scans boxes already in `Received` state to advance them further (e.g., to `Stocked`) — it has no mechanism to detect or reconcile a box that never made it to `Received` despite its `StockUpOperation`s existing and being processed.

`StockUpOperation.DocumentNumber` is deterministic: `BOX-{box.Id:000000}-{productCode}` (`ChangeTransportBoxStateHandler.cs:287`), and has a unique DB index, `IX_StockUpOperations_DocumentNumber_Unique` (`StockUpOperationConfiguration.cs:52-55`), described in a comment as "Layer 1 protection" against duplicate operations. `CreateOperationAsync` does not check for an existing row with the same `DocumentNumber` before inserting.

### Concrete failure scenario (from the issue)
1. Operator receives a box (e.g. `InTransit → Received`). `HandleReceived` creates and *commits* a `StockUpOperation` per product.
2. The box's own `SaveChangesAsync` (`:135`) then fails (transient DB error, concurrency conflict, or process crash in the window). The box remains in its pre-transition state (e.g. `InTransit`); the operations already committed remain `Pending`.
3. The background pipeline (`ProcessPendingOperationsAsync`) processes those `Pending` operations and calls Shoptet, which **increases real inventory** — but the box was never marked `Received`, so `TransportBoxCompletionService` (which only scans `Received` boxes) never sees or reconciles it.
4. The operator retries Receive. `HandleReceived` recomputes the same aggregated products and re-derives the same `DocumentNumber`s, so `CreateOperationAsync` attempts to insert rows that already exist → unique-constraint (`IX_StockUpOperations_DocumentNumber_Unique`) violation. This exception is caught by the blanket `catch (Exception ex)` in `Handle` (`:202-211`) and surfaces only as a generic `TransportBoxStateChangeError`. The box is now permanently wedged: its state can never advance via Receive again, even though its stock has already been added to inventory. Recovery currently requires manual DB intervention.

The fix must close both failure modes: (a) the two writes must not be independently, non-atomically committed, and (b) retrying a Receive that has partially succeeded must be a safe no-op for the parts that already happened, not a permanent failure.

## Functional Requirements

### FR-1: Atomic persistence of Receive
The state transition to `Received` and the creation of the corresponding `StockUpOperation` row(s) for a given Receive invocation must be persisted as a single all-or-nothing unit. Either both the box's new state (and `StateLog` entry) and all of that invocation's `StockUpOperation` rows are committed, or none of them are.

**Acceptance criteria:**
- If the process crashes, the app pool recycles, or the database rejects the final commit (e.g. transient error, concurrency conflict) at any point after `HandleReceived` starts creating operations and before the box's state is durably persisted, the database must contain **either** (a) no new `StockUpOperation` rows for this Receive call and the box still in its pre-transition state, **or** (b) the box in `Received` state (with its `StateLog` entry) **and** all of this invocation's `StockUpOperation` rows — never a state where operations exist but the box did not transition.
- The implementation must not rely on the ordering of two independently-committed `SaveChangesAsync` calls to approximate atomicity. Acceptable mechanisms (any one is sufficient, consistent with "Suggested direction" in the source issue):
  - An explicit `ApplicationDbContext.Database.BeginTransactionAsync(...)` spanning both the operation inserts and the box update, committed once at the end of `Handle` (feasible here because both repositories share one `ApplicationDbContext` instance per request scope — see Background).
  - Removing the intermediate `SaveChangesAsync` from the stock-up-operation creation path for this call path and deferring a single combined `SaveChangesAsync` to the end of `Handle`, so both the `AddAsync`-staged operations and the box update are flushed together in one commit.
  - Deferring `StockUpOperation` creation until after the box transition has been staged, and only calling `SaveChangesAsync` once, after both are staged (still within the same transaction/commit).
- Whichever mechanism is chosen, it must not change behavior for other, non-Received transitions handled by the same `Handle` method (e.g. `New→Opened`, `Opened→Reserve`, `Opened→Quarantine`), nor for other callers of `IStockUpProcessingService.CreateOperationAsync` outside this handler (see FR-3 for the shared-service impact).

### FR-2: Idempotent Receive retry
Retrying `ChangeTransportBoxState` to `Received` for a box whose `StockUpOperation` rows were already created in a prior (interrupted) attempt must not fail with a unique-constraint violation, and must not create duplicate operations or double-count inventory.

**Acceptance criteria:**
- Before creating a `StockUpOperation` for a computed `DocumentNumber`, the create path checks whether a row with that `DocumentNumber` already exists (e.g. via `IStockUpOperationRepository.GetByDocumentNumberAsync`, which already exists) and skips creation for that product if so, treating it as already-satisfied rather than an error.
- A retried Receive call that finds all of its `DocumentNumber`s already present (from a prior partial attempt) proceeds to complete the box's state transition normally, without raising an error.
- A retried Receive call where some `DocumentNumber`s exist and others don't (partial creation from a prior attempt) creates only the missing ones, then completes the box's state transition.
- Unique-constraint violations on `StockUpOperation.DocumentNumber` arising from this create path are no longer possible in the normal retry flow described above. (This is a safety net, not the primary correctness mechanism — FR-1's atomicity is what prevents the partial-commit state that made retries dangerous in the first place. FR-2 ensures that even a retry after a failure mode not fully covered by FR-1's chosen mechanism, or a pre-existing wedged box created by the current buggy behavior, degrades to "no-op and continue" rather than "permanent failure.")
- This idempotency check-and-skip must not introduce a race condition that reintroduces duplicate rows under concurrent retries; relying on the existing unique index as a last-resort guard (and catching/interpreting *that specific* constraint-violation as "already exists, continue" rather than a generic error) is acceptable in addition to the pre-check.

### FR-3: No regression to shared services
`StockUpProcessingService.CreateOperationAsync` and `ILogisticsStockOperationService.CreateOperationAsync` are also used by `GiftPackageManufactureService` (a second caller, outside transport boxes). Any change to the create/commit behavior of these shared services must preserve correct, working behavior for that caller too.

**Acceptance criteria:**
- If `CreateOperationAsync`'s "create + immediately `SaveChangesAsync`" contract changes (e.g. `SaveChangesAsync` is removed or made conditional) to support FR-1, `GiftPackageManufactureService`'s call sites are reviewed and, if they depend on the immediate-commit behavior, are either updated accordingly or the change is scoped so it does not alter behavior for that caller (e.g. via a new method/overload used only by the TransportBox Receive path, or a parameter controlling whether to save immediately).
- Existing tests covering `GiftPackageManufactureService`'s stock-up-operation creation continue to pass unmodified in behavior (only in setup/mocking if the interface signature changes).

### FR-4: Error surfacing (secondary, in-scope only if trivial)
The blanket `catch (Exception ex)` in `Handle` (`:202-211`) currently reduces a unique-constraint violation to a generic `TransportBoxStateChangeError`, which is how the "permanently wedged" symptom was diagnosed as opaque. This spec does not require a general overhaul of `Handle`'s exception handling (out of scope — see below), but:

**Acceptance criteria:**
- Once FR-2 is implemented, the specific unique-constraint-violation-on-retry scenario described in the issue should no longer reach this catch block as an unhandled error during normal retries (because it's treated as idempotent skip). No new dedicated exception type or error code is required for this fix.

## Non-Functional Requirements

### NFR-1: Performance
- The fix must not materially change the number of round-trips for the common case (first-time, non-retried Receive of a box with N distinct products): at most one additional existence-check query per distinct product code is acceptable for the idempotency check in FR-2; batching this into a single `WHERE DocumentNumber IN (...)` query is preferred over N individual round-trips when N > 1, but not mandatory for this fix.
- No additional database round-trips should be introduced for transitions other than the ones that call `HandleReceived`.

### NFR-2: Security
- No change to authentication/authorization. The fix touches only internal persistence/transaction handling; it does not change who can call `ChangeTransportBoxState`.
- No new sensitive data is introduced or logged. Do not log full stack traces or connection strings if a transaction-related exception is caught; keep existing logging conventions (`_logger.LogError(ex, ...)`, as done today).

### NFR-3: Data integrity
- The existing unique index `IX_StockUpOperations_DocumentNumber_Unique` must remain in place as a defense-in-depth guard; it is not being relaxed or removed by this fix.
- No migration is required for FR-1/FR-2 as specified (no schema change), unless the chosen transaction mechanism requires one (it should not, since Postgres via Npgsql/EF Core supports ambient transactions via `Database.BeginTransactionAsync` without schema changes).

## Data Model

No schema changes are required. Relevant existing entities:

**`TransportBox`** (`Anela.Heblo.Domain.Features.Logistics.Transport`)
- State machine entity with `State` (`TransportBoxState`: New, Opened, InTransit, Reserve, Quarantine, Received, Stocked, Closed, ...), `Items` (line items with `ProductCode`, `Amount`, `SourceInventoryId`), and `StateLog` (audit trail of transitions).
- Persisted via `ITransportBoxRepository` (`TransportBoxRepository : BaseRepository<TransportBox, int>`), backed by `ApplicationDbContext`.

**`StockUpOperation`** (`Anela.Heblo.Domain.Features.Catalog.Stock`)
- Fields: `DocumentNumber` (unique, deterministic `BOX-{boxId:000000}-{productCode}` for transport-box sources), `ProductCode`, `Amount`, `SourceType` (`StockUpSourceType`: TransportBox, GiftPackageManufacture), `SourceId`, `State` (`StockUpOperationState`: Pending, Submitted, Completed, Failed), `CreatedAt`, `SubmittedAt`, `CompletedAt`, `ErrorMessage`.
- Persisted via `IStockUpOperationRepository` (`StockUpOperationRepository : BaseRepository<StockUpOperation, int>`), backed by the **same** `ApplicationDbContext` instance as `ITransportBoxRepository` within a single request scope (both are `AddScoped`, and `ApplicationDbContext` is `AddDbContext`-registered as Scoped) — this is what makes a single-DbContext transaction sufficient without distributed-transaction machinery.
- `IStockUpOperationRepository.GetByDocumentNumberAsync(string, CancellationToken)` already exists and is the natural primitive for the FR-2 pre-check.
- Unique index: `IX_StockUpOperations_DocumentNumber_Unique` on `DocumentNumber` (`StockUpOperationConfiguration.cs:52-55`) — unchanged by this fix.

No new entities, fields, or indexes are introduced by this fix.

## API / Interface Design

No public/external API surface changes (the `ChangeTransportBoxState` MediatR request/response contract, `ChangeTransportBoxStateRequest` / `ChangeTransportBoxStateResponse`, and the HTTP endpoint that wraps it, are unaffected).

Internal interfaces affected:

- **`ChangeTransportBoxStateHandler.Handle` / `HandleReceived`** (`ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`): `HandleReceived` and the surrounding `Handle` method are restructured so that operation creation and the box's state persistence occur within one atomic unit (see FR-1). The exact shape (explicit transaction vs. single deferred `SaveChangesAsync`) is an implementation decision left to the architect/dev, constrained by FR-1/FR-3.
- **`IStockUpProcessingService.CreateOperationAsync`** (`Anela.Heblo.Application.Features.Catalog.Services`) and **`ILogisticsStockOperationService.CreateOperationAsync`** (`Anela.Heblo.Application.Features.Logistics.Contracts`, implemented by `LogisticsStockOperationAdapter`): may need a signature or behavioral change (e.g. an overload/parameter to control whether `SaveChangesAsync` is called immediately, or removing the immediate `SaveChangesAsync` from this path entirely) to support FR-1 without an enclosing-scope-spanning `SaveChangesAsync` call being made twice. Any such change must satisfy FR-3 (no regression to `GiftPackageManufactureService`).
- **`IStockUpOperationRepository.GetByDocumentNumberAsync`**: reused (not changed) as the idempotency check for FR-2.

## Dependencies
- PostgreSQL (via Npgsql/EF Core) — must support `DbContext.Database.BeginTransactionAsync` / ambient transactions if that mechanism is chosen; this is standard EF Core Npgsql functionality already available in this stack, no new package.
- No external service dependency changes. `IEshopStockDomainService` (Shoptet integration, used by `ProcessPendingOperationsAsync`) is unaffected — this fix concerns only the synchronous Receive path, not the background stock-up processing pipeline.
- `GiftPackageManufactureService` is a consumer dependency to be checked for regressions (FR-3).

## Out of Scope
- Reconciliation of transport boxes that are *already* wedged in the database today due to this bug (i.e. no data-migration/cleanup script for existing corrupted rows is part of this fix; that would be a separate, explicit remediation task if needed).
- General overhaul of `ChangeTransportBoxStateHandler.Handle`'s exception handling / error-code granularity beyond what FR-4 requires.
- Applying the same atomicity/idempotency fix to `GiftPackageManufactureService`'s own call path into `CreateOperationAsync`, beyond ensuring it isn't broken (FR-3). If that path has an analogous non-atomicity issue, it is a separate finding/issue.
- Outbox-pattern or message-queue-based deferred emission of `StockUpOperation` creation (mentioned as one option in the source issue) — an explicit DB transaction or single-`SaveChangesAsync` approach is sufficient given both repositories share one `ApplicationDbContext`, and is simpler to implement and review; introducing an outbox is unnecessary additional infrastructure for this fix.
- Changes to `TransportBoxCompletionService` or the background `ProcessPendingOperationsAsync` pipeline.
- Any UI/frontend changes — this is a backend-only correctness fix; no user-facing behavior change is expected beyond retries now succeeding instead of permanently failing.

## Open Questions
None. Where the brief left implementation-mechanism choice open ("explicit transaction, OR idempotent create, OR outbox"), this spec picks explicit-transaction-or-single-commit plus idempotent-create-as-safety-net (FR-1 + FR-2 together) as the concrete direction, and excludes the outbox option as unnecessary complexity given both repositories already share a single `ApplicationDbContext` per request (see Out of Scope). The exact mechanism for FR-1 (explicit `BeginTransactionAsync` vs. restructuring to a single deferred `SaveChangesAsync`) is left as an implementation detail for the architect, since both satisfy the acceptance criteria equally.

## Status: COMPLETE
