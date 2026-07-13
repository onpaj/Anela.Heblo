# Specification: Revert tracked entity mutations for failed re-imported invoices

## Summary
`InvoiceImportService.ExecuteImportInvoice` mutates a tracked EF Core entity when re-importing an existing invoice. If a transformation or downstream step throws after the mutation but before that invoice's own `SaveChangesAsync`, the per-invoice error handler in `ImportInvoicesAsync` records the invoice as `Failed` but leaves the mutated entity tracked as `Modified`. Because the repository, `DbContext`, and `InvoiceImportService` are all scoped per-batch (not per-invoice), the next invoice's `SaveChangesAsync` call in the same batch silently flushes the abandoned mutation, corrupting a row that was reported as failed. This spec defines how to detect this case and revert the tracked entity's state before it can leak into a later flush.

## Background
`ImportInvoicesAsync` (`InvoiceImportService.cs:38-79`) iterates over invoices within a batch using the same `DbContext`/`IIssuedInvoiceRepository` instance for the whole batch (both registered `AddScoped` in `InvoicesModule.cs`). For each invoice, `ExecuteImportInvoice` (`InvoiceImportService.cs:81-125`) loads the invoice via `GetOrCreateAsync`, which internally calls `IssuedInvoiceRepository.GetByIdAsync` (`IssuedInvoiceRepository.cs:22-26`). Unlike other repositories in the codebase, this override does not call `.AsNoTracking()`, so the returned `IssuedInvoice` is attached to the change tracker in the `Unchanged` state (for a pre-existing row) for the duration of the batch.

`ExecuteImportInvoice` then unconditionally calls `_mapper.Map(invoiceDetail, invoice)` (line 90) to refresh core fields on the tracked entity, regardless of whether the invoice is new or a re-import. For an existing invoice this transitions the entity to `Modified` in-memory immediately, before any transformation has run and before this invoice's own `UpdateAsync`/`SaveChangesAsync` (lines 115-116). If any of the subsequent transformation steps (`ProductMappingIssuedInvoiceImportTransformation`, `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation`, `GiftWithoutVATIssuedInvoiceImportTransformation`, etc.) throws, control returns to the outer `catch` in `ExecuteImportInvoice` (lines 120-124), which logs and re-throws, and then to the per-invoice `catch` in `ImportInvoicesAsync` (lines 61-66), which records the invoice as `Failed` and continues the loop — without ever touching the change tracker.

Because the entity is still tracked as `Modified`, EF Core's automatic change detection will include it in the next `SaveChangesAsync` call issued by *any other invoice* processed later in the same `foreach` loop (line 116, or the initial-save inside `GetOrCreateAsync` at line 134 for a brand-new invoice). This silently persists a partial/incorrect mutation for an invoice the system reported as failed, with no error surfaced anywhere. This was found during code review of #3566 (a separate, narrower fix for a double-save issue on newly-created invoices); it is filed separately because it is pre-existing, unrelated to that fix's scope, and requires its own architecture decision about where tracking-state control should live.

## Functional Requirements

### FR-1: Detect and classify the failure point relative to entity tracking
`ExecuteImportInvoice` must know, at the point an exception is caught, whether the in-scope `IssuedInvoice` entity:
1. Was newly created in this call (added via `GetOrCreateAsync`'s `AddAsync` + `SaveChangesAsync`, i.e. a real row now exists but was created by this same call), or
2. Was loaded as a pre-existing, tracked entity (a re-import) and has since been mutated in-memory by `_mapper.Map(invoiceDetail, invoice)` and/or `invoice.SyncSucceeded`/`invoice.SyncFailed`.

This classification must be available in the exception handler regardless of which step threw (mapping, transformation pipeline, `_issuedInvoiceClient.SaveAsync`, or the final `UpdateAsync`/`SaveChangesAsync`).

**Acceptance criteria:**
- Given a re-imported invoice whose transformation step throws, the handler can determine `isNew == false` for that invoice.
- Given a brand-new invoice whose transformation step throws, the handler can determine `isNew == true` for that invoice.
- The classification does not depend on inspecting `EntityState` directly inside `InvoiceImportService` if that violates the chosen repository abstraction (see FR-2); it may be tracked via a local variable/flag returned from `GetOrCreateAsync`.

### FR-2: Revert tracked mutations for a failed re-imported (existing) invoice
When `ExecuteImportInvoice` catches an exception for an invoice classified as pre-existing (`isNew == false`), it must revert the tracked entity's in-memory state to match the database before returning control to `ImportInvoicesAsync`, so that no later `SaveChangesAsync` in the same batch can flush the abandoned mutation.

The mechanism must not use `DeleteAsync`/`Remove()` for this case, since the entity has a real persisted row and `Remove()` would mark it for deletion instead of discarding the in-memory edit.

Acceptable approaches (final choice is an architecture decision — see Open Questions / Dependencies):
- Resetting the tracked entry's state to `Unchanged` (discarding in-memory property changes, keeping the original DB-loaded values) — e.g. `context.Entry(invoice).State = EntityState.Unchanged` after re-copying original values, or
- Reloading the entity from the database via `context.Entry(invoice).ReloadAsync()`, or
- Detaching the entity (`EntityState.Detached`) if nothing else in the batch will reference this instance again.

Whichever mechanism is chosen, it must be exposed through a boundary consistent with existing module layering (`docs/architecture/development_guidelines.md`) — either as a new method on `IIssuedInvoiceRepository` (e.g. `RevertTrackedChangesAsync(IssuedInvoice entity)` / `ReloadAsync(IssuedInvoice entity)`), or via direct, already-accessible access to `ApplicationDbContext` from `InvoiceImportService` if such access already exists without violating layering. `InvoiceImportService` must not take a direct dependency on `Microsoft.EntityFrameworkCore` types if the codebase's layering forbids Application-layer code from referencing EF Core directly.

**Acceptance criteria:**
- After `ExecuteImportInvoice` catches an exception for a re-imported invoice and returns/re-throws, the corresponding entity's `EntityState` in the shared `DbContext` is `Unchanged` or `Detached` (never `Modified` or `Added`).
- A subsequent invoice in the same batch that calls `SaveChangesAsync` does not persist any property change made to the failed invoice's entity.
- `invoice.SyncFailed(...)` / `invoice.SyncSucceeded(...)` calls made before the failure point (if any) are also reverted — the revert must occur after all such mutations, immediately before the exception propagates out of `ExecuteImportInvoice`.
- No change of behavior for the successful path: a re-imported invoice that completes without error is still updated and saved exactly as today.
- No change of behavior for a genuinely new invoice that fails after creation (`isNew == true`): this case is explicitly out of scope for the revert logic added here (its handling, if any, is covered by #3566 or a separate future fix) — do not add `Remove()`/delete logic for the new-invoice path as part of this change unless it is already required to prevent the same class of bug; if it is, flag it as a dependency (see Dependencies).

### FR-3: Preserve existing failure reporting behavior
The revert operation itself must not change what gets reported to the caller: the invoice must still be added to `resultDto.Failed`, the batch must still be marked failed (`_issuedInvoiceSource.FailAsync(batch)`), and the original exception/log message must still surface exactly as today. The revert is purely a side-effect cleanup of the change tracker and must not swallow, wrap, or replace the original exception.

**Acceptance criteria:**
- The exception type and message logged by `ImportInvoicesAsync`'s per-invoice `catch` (line 65) is unchanged for this failure case.
- `resultDto.Failed` still contains the invoice code for this case.
- If the revert operation itself throws (e.g. reload against a deleted row), that secondary exception must not mask the original import failure — the original exception must still be the one recorded/logged, or both must be surfaced without losing the original's information (implementation detail, but must not result in an unhandled crash of the whole batch).

### FR-4: Regression test coverage requiring a real change tracker
Add integration-style test coverage that exercises a real EF Core change tracker (InMemory provider or Testcontainers-backed, matching the pattern used for the #3566 regression test), since the existing mocked `InvoiceImportServiceTests.cs` cannot observe change-tracker leakage.

Test scenario (per the brief's "Suggested test"):
1. Seed the database with an existing invoice A (a prior successful import).
2. Run `ImportInvoicesAsync` on a batch containing two invoices in order: A (re-import, whose transformation step or refresh-map is made to throw) followed by B (new or existing, which succeeds).
3. Assert invoice A's row in the database is byte-for-byte unchanged from its pre-import state after the batch completes.
4. Assert invoice B still imports and saves successfully (i.e., the fix does not break the batch's ability to continue processing after a failure).
5. Assert `resultDto.Failed` contains A's code and `resultDto.Succeeded` contains B's code.

**Acceptance criteria:**
- The new test fails against the current (unfixed) code and passes after the fix is applied (verified by running it against a stashed/reverted version of the fix during development).
- The test uses a real change tracker (EF Core InMemory or Testcontainers), not a mocked repository.
- The test lives alongside `InvoiceImportServiceTests.cs` / `InvoiceImportIntegrationTests.cs` in `backend/test/Anela.Heblo.Tests/Features/Invoices/`, following the existing pattern from the #3566 regression test for the new-invoice case.

## Non-Functional Requirements

### NFR-1: Performance
The revert operation (state reset, reload, or detach) must add negligible overhead to the happy path — it must only execute on the exception path for a re-imported invoice, never on every invoice. No additional database round-trip should be introduced for successful imports. If `EntityState.Unchanged` reset (in-memory, no DB round-trip) is chosen over `.ReloadAsync()` (which issues a `SELECT`), prefer the in-memory approach unless correctness requires re-reading the DB (e.g., original values are not reliably available for full field-by-field reset).

### NFR-2: Security
No new attack surface: this is an internal data-integrity fix with no external interface changes. No new secrets, auth, or data exposure are introduced. Existing invoice data (customer names, prices) already flows through this code path; the fix must not log full invoice payloads that aren't already logged today.

### NFR-3: Reliability / Data integrity
This fix directly addresses a silent-data-corruption defect. The primary success measure is: **no batch, under any failure sequence, may persist a mutation for an invoice that batch reports as `Failed`.** This must hold for batches of any size and for any position of the failing invoice within the batch (first, middle, last).

## Data Model
No schema changes. Relevant existing entity: `IssuedInvoice` (`backend/src/Anela.Heblo.Domain/Features/Invoices/`), tracked by `ApplicationDbContext` via `DbSet<IssuedInvoice>`. No new tables, columns, or migrations are required. The fix operates purely on EF Core's in-memory change-tracker state (`EntityState`) for entities already loaded during a batch.

## API / Interface Design
No public HTTP API changes. Internal interface changes only, pending the architecture decision noted in Dependencies/Open Questions:

- **Option A — repository-level method:** Add a method to `IIssuedInvoiceRepository` (or a more general base, e.g. `IRepository<TEntity, TKey>`, if this pattern is expected to recur for other importers), such as:
  ```csharp
  Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
  ```
  implemented in `IssuedInvoiceRepository`/`BaseRepository` using `Context.Entry(entity).State = EntityState.Unchanged` or `Context.Entry(entity).ReloadAsync(cancellationToken)`.

- **Option B — direct `ApplicationDbContext` access:** If `InvoiceImportService` (or a lower layer it can call into) already has an approved path to reach `ApplicationDbContext` directly without breaking the Application → Persistence layering rules in `docs/architecture/development_guidelines.md`, perform the revert there instead of extending the repository interface.

`ExecuteImportInvoice`'s internal control flow changes to track `isNew` (already effectively available from `GetOrCreateAsync`'s branch) and to call the revert operation from the outer `catch` block when `isNew == false`, before re-throwing.

## Dependencies
- Requires an architecture decision (explicitly flagged by the brief as needing "an architecture pass") on whether tracking-state control is exposed via `IIssuedInvoiceRepository`/`IRepository<TEntity,TKey>` or accessed directly against `ApplicationDbContext` from the Application layer. This spec does not prescribe the answer; see Open Questions.
- Depends on the existing `GetOrCreateAsync` / `ExecuteImportInvoice` control flow in `InvoiceImportService.cs` remaining structurally similar; if #3566 or any other in-flight change restructures this method before this fix lands, the diff must be re-verified against the current file state at implementation time.
- Test infrastructure: reuses whatever EF Core InMemory/Testcontainers test harness was introduced for the #3566 regression test (referenced in the brief) — confirm that harness is merged/available before writing FR-4's test, or stand up an equivalent one.

## Out of Scope
- The new-invoice (`isNew == true`) failure path is not addressed by this spec unless investigation during implementation shows it shares the identical leak mechanism and is trivially covered by the same fix (flag as a dependency/open question if so — do not silently expand scope).
- The double-save behavior for newly-created invoices addressed by #3566 is explicitly out of scope; this spec assumes that fix (or its absence) does not change the mutation-leak mechanics described here, per the brief's confirmation that the relevant code paths are byte-identical before/after #3566.
- Broader refactoring of `IssuedInvoiceRepository.GetByIdAsync` to use `.AsNoTracking()` by default is out of scope — that would be a larger behavioral change (the entity is intentionally mutated and tracked for the *successful* path) and needs its own analysis of all callers.
- Any change to batch-level transaction semantics (e.g., wrapping the whole batch in a single DB transaction so a failed invoice can never be individually flushed) is out of scope; this spec fixes the tracked-entity leak specifically, not the broader question of whether per-invoice `SaveChangesAsync` inside a shared scope is the right transactional model.
- UI/reporting changes are out of scope; `ImportResultDto`'s `Succeeded`/`Failed` shape does not change.

## Open Questions
- Should the tracking-state-revert capability be added as a new method on `IIssuedInvoiceRepository` (narrow, invoice-specific) or on the shared generic `IRepository<TEntity, TKey>`/`BaseRepository<TEntity, TKey>` (reusable for other importers with the same scoped-repository-plus-batch-loop pattern)? Assumption for planning purposes: start narrow on `IIssuedInvoiceRepository` since no other importer in the codebase was identified as having this pattern during this investigation; promote to the generic base later if a second consumer appears.
- Is `EntityState.Unchanged` reset sufficient (in-memory only, cheap), or does correctness require `ReloadAsync()` (an extra `SELECT`) to guard against cases where original property values aren't reliably restorable by a plain state flip (e.g., if EF's original-values snapshot was itself altered)? Assumption: `EntityState.Unchanged` reset is sufficient because EF Core retains original values for change-tracked entities separately from current values, and flipping state back to `Unchanged` is EF's standard idiom for discarding in-memory edits without a DB round-trip; this should be validated with a unit test asserting the original values are what's persisted, not the mutated ones.
- Does the new-invoice (`isNew == true`) failure path leak in the same way, given that `GetOrCreateAsync` already commits the new row via its own `SaveChangesAsync` before transformations run? If so, should this spec's fix be extended to cover it, or should that be filed as yet another separate issue? Assumption: filed separately, consistent with the brief's explicit framing of this issue as scoped to "existing (re-imported)" invoices only — flag during implementation if investigation shows the new-invoice case is trivially covered by the same code change.
- Should the revert also apply if the failure happens inside the *inner* try/catch around `_issuedInvoiceClient.SaveAsync` (lines 99-113), which already catches its own exception and calls `invoice.SyncFailed(...)` — meaning that path does not currently reach the outer catch at all for that specific failure, but does still call `UpdateAsync`/`SaveChangesAsync` immediately after (line 115-116) so does not currently exhibit the leak? Assumption: no revert needed there since that path proceeds to its own `SaveChangesAsync` immediately, which is a legitimate, intentional persist of the "sync failed" status — this spec's revert applies only when an exception escapes to the outer catch (line 120) without reaching the final `SaveChangesAsync`.

## Status: HAS_QUESTIONS
