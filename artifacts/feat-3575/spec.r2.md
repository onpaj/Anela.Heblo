# Specification: Revert tracked entity mutations for failed re-imported invoices

## Summary
`InvoiceImportService.ExecuteImportInvoice` mutates a tracked EF Core entity when re-importing an existing invoice. If a transformation or downstream step throws after the mutation but before that invoice's own `SaveChangesAsync`, the per-invoice error handler in `ImportInvoicesAsync` records the invoice as `Failed` but leaves the mutated entity tracked as `Modified`. Because the repository, `DbContext`, and `InvoiceImportService` are all scoped per-batch (not per-invoice), the next invoice's `SaveChangesAsync` call in the same batch silently flushes the abandoned mutation, corrupting a row that was reported as failed. This spec defines a narrow, invoice-specific fix — a new `IIssuedInvoiceRepository` method that resets the tracked entity's `EntityState` back to `Unchanged` — that reverts this leak before it can flush.

## Background
`ImportInvoicesAsync` (`InvoiceImportService.cs:38-79`) iterates over invoices within a batch using the same `DbContext`/`IIssuedInvoiceRepository` instance for the whole batch (both registered `AddScoped` in `InvoicesModule.cs`). For each invoice, `ExecuteImportInvoice` (`InvoiceImportService.cs:81-125`) loads the invoice via `GetOrCreateAsync`, which internally calls `IssuedInvoiceRepository.GetByIdAsync` (`IssuedInvoiceRepository.cs:22-26`). Unlike every other repository in the codebase (`PurchaseOrderRepository`, `BankStatementImportRepository`, `LeafletDocumentRepository`, `ArticleRepository`, `PackageRepository`, `SmartsuppRepository`, etc. — all of which use `.AsNoTracking()` for reads), this override does not call `.AsNoTracking()`, so the returned `IssuedInvoice` is attached to the change tracker in the `Unchanged` state (for a pre-existing row) for the duration of the batch.

`ExecuteImportInvoice` then unconditionally calls `_mapper.Map(invoiceDetail, invoice)` (line 90) to refresh core fields on the tracked entity, regardless of whether the invoice is new or a re-import. For an existing invoice this transitions the entity to `Modified` in-memory immediately, before any transformation has run and before this invoice's own `UpdateAsync`/`SaveChangesAsync` (lines 115-116). If any of the subsequent transformation steps (`ProductMappingIssuedInvoiceImportTransformation`, `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation`, `GiftWithoutVATIssuedInvoiceImportTransformation`, etc.) throws, control returns to the outer `catch` in `ExecuteImportInvoice` (lines 120-124), which logs and re-throws, and then to the per-invoice `catch` in `ImportInvoicesAsync` (lines 61-66), which records the invoice as `Failed` and continues the loop — without ever touching the change tracker.

Because the entity is still tracked as `Modified`, EF Core's automatic change detection will include it in the next `SaveChangesAsync` call issued by *any other invoice* processed later in the same `foreach` loop (line 116, or the initial-save inside `GetOrCreateAsync` at line 134 for a brand-new invoice). This silently persists a partial/incorrect mutation for an invoice the system reported as failed, with no error surfaced anywhere. This was found during code review of #3566 (a separate, narrower fix for a double-save issue on newly-created invoices); it is filed separately because it is pre-existing and unrelated to that fix's scope.

A repo-wide scan (performed to resolve this spec's architecture question) confirmed two things that shape the fix below: (1) `IssuedInvoiceRepository.GetByIdAsync` is the *only* tracked-read override in the codebase — every other repository already uses `.AsNoTracking()` — so this leak has exactly one consumer today; and (2) the codebase's other batch importer, `MarketingInvoiceImportService`, uses `ExistsAsync` + always-`AddAsync` rather than a tracked `GetByIdAsync`-then-mutate flow, so it does not share this risk. The fix is therefore scoped narrowly to `IIssuedInvoiceRepository` rather than the generic repository base.

## Functional Requirements

### FR-1: Detect and classify the failure point relative to entity tracking
`ExecuteImportInvoice` must know, at the point an exception is caught, whether the in-scope `IssuedInvoice` entity:
1. Was newly created in this call (added via `GetOrCreateAsync`'s `AddAsync` + `SaveChangesAsync`, i.e. a real row now exists but was created by this same call), or
2. Was loaded as a pre-existing, tracked entity (a re-import) and has since been mutated in-memory by `_mapper.Map(invoiceDetail, invoice)`.

This classification must be available in the exception handler regardless of which step threw (mapping, transformation pipeline, or `_issuedInvoiceClient.SaveAsync`'s outer failure path — see FR-2 for the precise boundary).

**Acceptance criteria:**
- Given a re-imported invoice whose transformation step throws, the handler can determine `isNew == false` for that invoice.
- Given a brand-new invoice whose transformation step throws, the handler can determine `isNew == true` for that invoice.
- The classification is tracked via a local variable/flag already effectively available from `GetOrCreateAsync`'s branch (no new EF Core dependency is introduced in `InvoiceImportService` to determine this).

### FR-2: Revert tracked mutations for a failed re-imported (existing) invoice
When `ExecuteImportInvoice`'s outer `catch` (line 120) catches an exception for an invoice classified as pre-existing (`isNew == false`) — i.e., an exception that escaped to the outer catch without the invoice's own final `UpdateAsync`/`SaveChangesAsync` (lines 115-116) having run — it must revert the tracked entity's in-memory state to `Unchanged` before returning control to `ImportInvoicesAsync`, so that no later `SaveChangesAsync` in the same batch can flush the abandoned mutation.

**Chosen mechanism:** Add a narrow method to `IIssuedInvoiceRepository`:
```csharp
Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
```
implemented in `IssuedInvoiceRepository` as an in-memory state reset:
```csharp
Context.Entry(entity).State = EntityState.Unchanged;
```
This is a synchronous, in-memory operation with no database round-trip (the `Task`-returning signature is for interface consistency with the rest of the repository, not because it awaits I/O). `ReloadAsync()` was considered and rejected — see rationale below.

This method must **not** use `DeleteAsync`/`Remove()`: the entity has a real persisted row, and `Remove()` would mark it for deletion instead of discarding the in-memory edit.

`ExecuteImportInvoice` calls `RevertTrackedChangesAsync` from its outer `catch` block when `isNew == false`, immediately before re-throwing (so the revert always happens before the exception propagates, regardless of which downstream step threw).

**Rationale for `EntityState.Unchanged` over `ReloadAsync()`:** The only mutation applied before the outer catch is `_mapper.Map(invoiceDetail, invoice)` on scalar properties (line 90). `IssuedInvoice.SyncHistory` (`IssuedInvoice.cs:50`) is populated only by `SyncSucceeded`/`SyncFailed`, which (per FR-2's scope below) run after the transformation pipeline and are excluded from this revert; `GetByIdAsync` does not `Include` `SyncHistory`, so no child-collection tracking is in play. EF Core retains original values for tracked entities separately from current values, so flipping `EntityState` back to `Unchanged` is EF's standard idiom for discarding in-memory edits without an extra `SELECT`. This keeps the fix minimal, consistent with NFR-1, and requires no new test infrastructure beyond what FR-4 already needs.

**Scope boundary (inner vs. outer catch):** The inner try/catch around `_issuedInvoiceClient.SaveAsync` (lines 99-113) already catches its own exception, calls `invoice.SyncFailed(...)`, and then falls through to `UpdateAsync`/`SaveChangesAsync` (lines 115-116) in the same call. That path does not reach the outer catch for that specific failure and does not exhibit the leak — `SyncFailed(...)` is an intentional, immediately-persisted status update, not an abandoned mutation. The revert defined here applies **only** when an exception escapes to the outer catch (line 120) without the final `UpdateAsync`/`SaveChangesAsync` having run.

**Acceptance criteria:**
- After `ExecuteImportInvoice` catches an exception at the outer catch for a re-imported invoice (`isNew == false`) and re-throws, the corresponding entity's `EntityState` in the shared `DbContext` is `Unchanged`.
- A subsequent invoice in the same batch that calls `SaveChangesAsync` does not persist any property change made to the failed invoice's entity.
- No revert is invoked for a failure caught by the *inner* try/catch (lines 99-113) around `_issuedInvoiceClient.SaveAsync` — that path's `invoice.SyncFailed(...)` write and its subsequent `SaveChangesAsync` are unaffected by this change.
- No change of behavior for the successful path: a re-imported invoice that completes without error is still updated and saved exactly as today.
- No change of behavior for a genuinely new invoice that fails after creation (`isNew == true`): confirmed out of scope (see Out of Scope) — no `RevertTrackedChangesAsync` call, no `Remove()`/delete logic, is added for the new-invoice path as part of this change.

### FR-3: Preserve existing failure reporting behavior
The revert operation itself must not change what gets reported to the caller: the invoice must still be added to `resultDto.Failed`, the batch must still be marked failed (`_issuedInvoiceSource.FailAsync(batch)`), and the original exception/log message must still surface exactly as today. The revert is purely a side-effect cleanup of the change tracker and must not swallow, wrap, or replace the original exception.

**Acceptance criteria:**
- The exception type and message logged by `ImportInvoicesAsync`'s per-invoice `catch` (line 65) is unchanged for this failure case.
- `resultDto.Failed` still contains the invoice code for this case.
- Since `RevertTrackedChangesAsync` is a synchronous, in-memory `EntityState` flip with no I/O, it cannot itself throw under normal operation; no additional exception-masking handling is required around the call.

### FR-4: Regression test coverage requiring a real change tracker
Add integration-style test coverage that exercises a real EF Core change tracker (InMemory provider or Testcontainers-backed, matching the pattern used for the #3566 regression test), since the existing mocked `InvoiceImportServiceTests.cs` cannot observe change-tracker leakage.

Test scenario (per the brief's "Suggested test"):
1. Seed the database with an existing invoice A (a prior successful import).
2. Run `ImportInvoicesAsync` on a batch containing two invoices in order: A (re-import, whose transformation step or refresh-map is made to throw) followed by B (new or existing, which succeeds).
3. Assert invoice A's row in the database is byte-for-byte unchanged from its pre-import state after the batch completes.
4. Assert invoice B still imports and saves successfully (i.e., the fix does not break the batch's ability to continue processing after a failure).
5. Assert `resultDto.Failed` contains A's code and `resultDto.Succeeded` contains B's code.
6. Additionally, assert directly that the original (pre-mutation) field values are what's persisted for invoice A, not the mutated values that `_mapper.Map` applied in-memory — confirming the `EntityState.Unchanged` reset actually discards the in-memory edit rather than merely appearing to.

**Acceptance criteria:**
- The new test fails against the current (unfixed) code and passes after the fix is applied (verified by running it against a stashed/reverted version of the fix during development).
- The test uses a real change tracker (EF Core InMemory or Testcontainers), not a mocked repository.
- The test lives alongside `InvoiceImportServiceTests.cs` / `InvoiceImportIntegrationTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Invoices/`, following the existing pattern from the #3566 regression test for the new-invoice case.

## Non-Functional Requirements

### NFR-1: Performance
The revert operation is an in-memory `EntityState` flip (`Context.Entry(entity).State = EntityState.Unchanged`) — no additional database round-trip. It executes only on the exception path for a re-imported invoice, never on every invoice, so it adds no measurable overhead to the happy path.

### NFR-2: Security
No new attack surface: this is an internal data-integrity fix with no external interface changes. No new secrets, auth, or data exposure are introduced. Existing invoice data (customer names, prices) already flows through this code path; the fix must not log full invoice payloads that aren't already logged today.

### NFR-3: Reliability / Data integrity
This fix directly addresses a silent-data-corruption defect. The primary success measure is: **no batch, under any failure sequence, may persist a mutation for an invoice that batch reports as `Failed`.** This must hold for batches of any size and for any position of the failing invoice within the batch (first, middle, last).

## Data Model
No schema changes. Relevant existing entity: `IssuedInvoice` (`backend/src/Anela.Heblo.Domain/Features/Invoices/`), tracked by `ApplicationDbContext` via `DbSet<IssuedInvoice>`. No new tables, columns, or migrations are required. The fix operates purely on EF Core's in-memory change-tracker state (`EntityState`) for entities already loaded during a batch.

## API / Interface Design
No public HTTP API changes. Internal interface change:

- Add to `IIssuedInvoiceRepository`:
  ```csharp
  Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
  ```
  Implemented in `IssuedInvoiceRepository` as:
  ```csharp
  public Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
  {
      Context.Entry(entity).State = EntityState.Unchanged;
      return Task.CompletedTask;
  }
  ```
- `IRepository<TEntity, TKey>` / `BaseRepository<TEntity, TKey>` are **not** modified — this method is added only to `IIssuedInvoiceRepository`, since it is the only repository in the codebase with a tracked (non-`AsNoTracking()`) read path, and no second consumer of this capability was identified. If a second importer with the same scoped-repository-plus-batch-loop-plus-tracked-read pattern appears in the future, promoting this method to the generic base can be revisited then.
- `ExecuteImportInvoice`'s internal control flow changes to track `isNew` (already effectively available from `GetOrCreateAsync`'s branch) and to call `RevertTrackedChangesAsync` from the outer `catch` block (line 120) when `isNew == false`, before re-throwing.

## Dependencies
- Depends on the existing `GetOrCreateAsync` / `ExecuteImportInvoice` control flow in `InvoiceImportService.cs` remaining structurally similar; if #3566 or any other in-flight change restructures this method before this fix lands, the diff must be re-verified against the current file state at implementation time.
- Test infrastructure: reuses whatever EF Core InMemory/Testcontainers test harness was introduced for the #3566 regression test (referenced in the brief) — confirm that harness is merged/available before writing FR-4's test, or stand up an equivalent one.

## Out of Scope
- The new-invoice (`isNew == true`) failure path: confirmed out of scope. Tracing `ExecuteImportInvoice`, for a new invoice `GetOrCreateAsync` commits the row via `AddAsync` + `SaveChangesAsync` (lines 132-134) before the outer mapping runs, transitioning the entity from `Added` to `Unchanged`; the subsequent `_mapper.Map(invoiceDetail, invoice)` (line 90) then re-applies the *same* `invoiceDetail` values onto the *same* entity via the same mapping profile — an idempotent no-op refresh, not a data-changing mutation (unlike the re-import case, where the pre-existing row holds genuinely different previously-synced values that get overwritten). While the entity does technically flip to `Modified` and could leak into a later `SaveChangesAsync`, the persisted values are indistinguishable from what was already legitimately committed for that new row, so there is no silent-corruption risk. This is a structurally similar but practically harmless flush, and is left as a separate, lower-priority follow-up if pursued at all — not addressed by this spec.
- The double-save behavior for newly-created invoices addressed by #3566 is explicitly out of scope; this spec assumes that fix (or its absence) does not change the mutation-leak mechanics described here, per the brief's confirmation that the relevant code paths are byte-identical before/after #3566.
- Broader refactoring of `IssuedInvoiceRepository.GetByIdAsync` to use `.AsNoTracking()` by default is out of scope — that would be a larger behavioral change (the entity is intentionally mutated and tracked for the *successful* path) and needs its own analysis of all callers.
- Any change to batch-level transaction semantics (e.g., wrapping the whole batch in a single DB transaction so a failed invoice can never be individually flushed) is out of scope; this spec fixes the tracked-entity leak specifically, not the broader question of whether per-invoice `SaveChangesAsync` inside a shared scope is the right transactional model.
- UI/reporting changes are out of scope; `ImportResultDto`'s `Succeeded`/`Failed` shape does not change.
- Extending `RevertTrackedChangesAsync` (or an equivalent) onto the generic `IRepository<TEntity, TKey>`/`BaseRepository<TEntity, TKey>` base is out of scope for this change; it is narrowly added to `IIssuedInvoiceRepository` only (see API / Interface Design).
- Reverting tracked state for failures caught by the inner try/catch around `_issuedInvoiceClient.SaveAsync` (lines 99-113) is out of scope: that path's `invoice.SyncFailed(...)` write is an intentional status update immediately followed by its own `SaveChangesAsync`, not an abandoned mutation, and reverting it would incorrectly discard a legitimate, currently-relied-upon behavior.

## Open Questions
None.

## Status: COMPLETE
