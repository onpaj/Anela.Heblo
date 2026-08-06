# Plan — TransportBoxes: Receive must not split stock-up creation and box-state commit across two transactions

## Summary
`ChangeTransportBoxStateHandler.HandleReceived` creates and immediately commits one `StockUpOperation` row per product (via `StockUpProcessingService.CreateOperationAsync` → `SaveChangesAsync`) *before* the box's own state transition is saved in a second, later `SaveChangesAsync`. If the second save fails or the process crashes between the two, inventory gets stocked up but the box is stuck in `InTransit`/`Reserve`/`Quarantine` forever, and retrying the receive throws a unique-constraint violation on `DocumentNumber` that gets swallowed into a generic error. This plan makes Receive atomic and idempotent so a partial failure is either fully rolled back or safely retryable.

## Context
`TransportBox` and `StockUpOperation` are persisted through two repositories (`ITransportBoxRepository`, `IStockUpOperationRepository`) that both wrap the *same* `ApplicationDbContext` instance (confirmed: both derive from `BaseRepository<TEntity,TKey>`, whose `SaveChangesAsync` calls `Context.SaveChangesAsync`, and the context is DI-scoped per request). There is no ambient transaction anywhere in the app (`grep BeginTransaction`/`TransactionScope` over `backend/src` is empty) and no MediatR pipeline behavior wraps handlers in a transaction. Because both writers share one DbContext, an explicit EF Core transaction spanning both `SaveChangesAsync` calls is directly achievable without introducing a distributed transaction or new service boundary — this is the natural fix, not an outbox or dedup-only patch (though idempotency is *also* needed regardless, since retries after a rollback must not blow up on the unique index).

## Functional requirements

**FR-1 — Receive is atomic.**
Creating the `StockUpOperation` rows for a box and persisting the box's state transition to `Received` must commit together or not at all.
- Acceptance: a forced failure of the box-side `SaveChangesAsync` (e.g. simulated exception/concurrency conflict injected in a test) after `StockUpOperation` rows were staged results in **zero** `StockUpOperation` rows and the box remaining in its pre-transition state — verified by an integration test that inspects the DB after the failure.
- Acceptance: on the success path, both the box row and all `StockUpOperation` rows are visible in the same read-after-write query with no window where one exists without the other.

**FR-2 — Receive is idempotent on retry.**
Retrying `ChangeTransportBoxState` (InTransit/Reserve/Quarantine → Received) for a box that already has `StockUpOperation` rows for some or all of its products (e.g. after a rollback under FR-1, or an old already-committed operation from a prior partial failure predating this fix) must not throw an unhandled unique-constraint violation, and must not create duplicate operations for products already recorded under the same deterministic `DocumentNumber`.
- Acceptance: unit/integration test calls `HandleReceived` twice for the same box/product set; second call creates no new/duplicate `StockUpOperation` rows and the handler returns success (not a generic `TransportBoxStateChangeError`).
- Acceptance: test seeds a pre-existing `StockUpOperation` with a `DocumentNumber` matching what `HandleReceived` would generate, then runs Receive — no exception, no duplicate row.

**FR-3 — Failure is observable, not swallowed into a generic error.**
If Receive still fails for a genuine reason (e.g. DB unavailable) after the fix, the box must remain in a consistent, reconcilable state (covered by FR-1) and the existing error response (`TransportBoxStateChangeError`) plus the existing `catch (Exception)` logging behavior is acceptable — no new requirement to change the response contract, but the fix must not make failures *silently* leave orphaned inventory (that's what FR-1 prevents).

## Non-functional requirements
- No new external dependencies (no distributed transaction coordinator, no message broker/outbox infra) — solution must use the existing single-DbContext-per-request setup.
- Transaction scope should be as narrow as correctness allows (wrap only the `HandleReceived` operation-creation + final box save, not the entire request pipeline) to avoid holding DB locks longer than necessary.
- No behavior change for the other five state transitions handled by the same `Handle` method (New→Opened, Opened→Reserve, Opened→Quarantine, and the non-Received paths) — they must keep their current single-`SaveChangesAsync` semantics.
- No performance regression for boxes with many product lines — batching considerations for `StockUpOperation` inserts should be considered if `CreateOperationAsync` moves from per-item `SaveChangesAsync` to a single save, but this is not the primary goal.

## Data model
No schema changes required. Relevant existing entities:
- `TransportBox` (state machine: New → Opened → Reserve/Quarantine → Received → Stocked/Closed), persisted via `ITransportBoxRepository`.
- `StockUpOperation` (Pending → Submitted → …), persisted via `IStockUpOperationRepository`, unique index on `DocumentNumber` (`BOX-{box.Id:000000}-{productCode}`) per `StockUpOperationConfiguration.cs:52-55`.
- Both entities are tracked by the same `ApplicationDbContext` within a request scope — this is the mechanism the fix relies on.

## Interfaces
- No new endpoints. Existing `ChangeTransportBoxStateRequest`/`ChangeTransportBoxStateResponse` contract (MediatR request/response via `POST` transport-box state-change endpoint) is unchanged.
- Internal service surface likely changes:
  - `ILogisticsStockOperationService.CreateOperationAsync` / `StockUpProcessingService.CreateOperationAsync` — needs a variant (or parameter) that stages the entity (`AddAsync`) without immediately calling `SaveChangesAsync`, so the handler controls the commit point. Must preserve current behavior for other callers (`GiftPackageManufactureService` also calls this — confirm it still gets its immediate-commit semantics, since that call site is not part of this finding and should not be forced into a shared transaction it doesn't need).
  - Idempotency check: either a `GetByDocumentNumberAsync`/`ExistsByDocumentNumberAsync` lookup on `IStockUpOperationRepository` before insert, or a catch/ignore around the unique-constraint violation with a translated benign result (a pre-check is preferable — clearer intent, avoids relying on DB-specific exception shape).

## Dependencies and scope
- In scope: `ChangeTransportBoxStateHandler.HandleReceived` and the box-save path in `Handle` (:126-135); `StockUpProcessingService.CreateOperationAsync`; `ILogisticsStockOperationService` contract if it needs a new method/overload; `IStockUpOperationRepository` if a lookup-by-document-number method is added.
- Out of scope: `TransportBoxCompletionService` reconciliation logic (mentioned in the finding as a symptom, not a cause — could be a follow-up hardening item to reconcile any *pre-existing* wedged boxes from before this fix, but that's a data-cleanup/ops concern, not part of this change). `GiftPackageManufactureService`'s own transactional integrity is out of scope unless the shared service's signature change affects it (verify only, don't fix unless the same non-atomicity bug is confirmed there too — flag as a candidate for a separate finding if so, don't silently expand scope).
- Depends on: EF Core's `DbContext.Database.BeginTransactionAsync` (or `ExecutionStrategy`-aware transaction helper, since the app may already use a retrying execution strategy — check `ApplicationDbContext` configuration for `EnableRetryOnFailure` before picking the transaction API, as retry strategies require `IExecutionStrategy.ExecuteAsync` wrapping rather than a bare `BeginTransactionAsync`).

## Rough plan
1. **Architecture/design step**: decide the exact transaction mechanism — explicit `BeginTransactionAsync`/`CommitAsync` around `HandleReceived` + the final `SaveChangesAsync` in `Handle`, wrapped in the correct execution-strategy pattern if retry-on-failure is enabled on the DbContext. Decide where the idempotency check lives (repository pre-check vs. unique-constraint catch) and confirm it composes correctly with the transaction (a pre-check inside the same transaction avoids TOCTOU only insofar as the unique index still backstops it under concurrent receives of the same box, which shouldn't happen but the index stays as a safety net regardless).
2. Change `StockUpProcessingService.CreateOperationAsync` (or add a new method) so entity creation can be staged without an immediate `SaveChangesAsync`, without breaking `GiftPackageManufactureService`'s existing immediate-commit usage.
3. Add an idempotency/dedup check for `DocumentNumber` before staging each `StockUpOperation` in `HandleReceived`.
4. Wrap `HandleReceived`'s operation staging + the subsequent `transition.ChangeStateAsync` + final `_repository.SaveChangesAsync(cancellationToken)` in one transaction/commit, so both succeed or both roll back.
5. Add integration tests: (a) atomicity — forced failure after staging operations leaves no `StockUpOperation` rows and box state unchanged; (b) idempotent retry — calling Receive twice, or with a pre-existing colliding `DocumentNumber`, succeeds without duplicates or unhandled exceptions; (c) regression — other five transitions and the non-Received happy path still behave identically (single save, no transaction overhead change in observable behavior).
6. Verify `GiftPackageManufactureService` call site still works unchanged (existing tests must pass; add a quick check that it still gets one-row-per-call immediate persistence if that's relied upon elsewhere).
7. Run `dotnet build` + `dotnet format` + full backend test suite touched by the change per repo validation rules.

## Open questions
- **Does `ApplicationDbContext` have `EnableRetryOnFailure` (a SQL execution strategy) configured?** If yes, `BeginTransactionAsync` cannot be used bare — it must go through `IExecutionStrategy.ExecuteAsync(() => ...)`, which changes the shape of the fix materially. Defaulting to: assume yes and use execution-strategy-safe transaction wrapping; architecture step should confirm from the actual DI/DbContext configuration.
- **Does `GiftPackageManufactureService` have the same atomicity bug** (staging inventory-affecting operations outside its own aggregate's transaction)? Out of scope for this finding, but the shared-service refactor in step 2 will touch its call path — flagging as a candidate follow-up finding rather than fixing here, to keep this change surgical per the "don't touch what's not requested" rule.
- **Should the idempotency check be a pre-check query or a caught-and-ignored unique-constraint violation?** Defaulting to a pre-check (`ExistsByDocumentNumberAsync`) for clarity and portability across DB providers, with the unique index remaining as the actual safety net for races — architecture step should confirm this is preferred over catching a provider-specific exception type.
- **Should pre-existing wedged boxes from before this fix be reconciled** (a data-fix job cross-referencing `Received`-eligible boxes stuck in earlier states against already-created `StockUpOperation` rows)? Treating this as out of scope / a manual-ops follow-up rather than part of the code fix, since the finding is about the defect going forward, not a data migration.
