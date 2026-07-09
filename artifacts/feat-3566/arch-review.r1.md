# Architecture Review: Remove redundant `SaveChangesAsync` in `InvoiceImportService.ExecuteImportInvoice`

## Skip Design: true

## Architectural Fit Assessment
This is a one-method, one-class correctness/performance fix inside an existing MediatR-free application service (`InvoiceImportService`, `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs`). It touches no controller, no DTO, no MediatR handler, and no public interface — `GetOrCreateAsync` is a private method. It fits cleanly into the existing Vertical Slice/repository pattern already used throughout `Features/Invoices`: `IIssuedInvoiceRepository` extends the generic `IRepository<TEntity, TKey>` via `BaseRepository<TEntity, TKey>` (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs`), and `IssuedInvoiceRepository` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`) only overrides `AddAsync`/`UpdateAsync` to stamp audit/concurrency fields — it does not change EF Core state-tracking semantics. No architectural pattern is introduced or broken; the fix works entirely within the existing Unit-of-Work-per-request-ish convention (repository methods mutate the change tracker, a single `SaveChangesAsync` flushes it).

I confirmed the two concrete claims in the spec against the current source:
- `GetOrCreateAsync` (lines 127-138) does call `SaveChangesAsync` immediately after `AddAsync` when the invoice is new — this is the redundant round trip.
- `ExecuteImportInvoice` (lines 115-116) unconditionally calls `_repository.UpdateAsync(invoice, cancellationToken)` followed by `SaveChangesAsync` — and `BaseRepository.UpdateAsync` (line 70-74) is `DbSet.Update(entity)`, which is exactly the call that flips a still-`Added`-and-unflushed entity to `Modified`. The spec's finding — that naively deleting the inner `SaveChangesAsync` without also guarding the trailing `UpdateAsync` breaks new-invoice imports — is real and matches how `DbSet.Update` behaves on an already-tracked entity in EF Core. The spec's proposed `(Invoice, IsNew)` tuple fix is the minimal correct change.

## Proposed Architecture

### Component Overview
No new components. Change is confined to two private methods in one existing class:

```
InvoiceImportService
 ├─ ImportInvoicesAsync (public, unchanged)
 │    └─ foreach invoice → ExecuteImportInvoice (loop body unchanged)
 ├─ ExecuteImportInvoice (private) ── MODIFIED: branches on IsNew before calling UpdateAsync
 └─ GetOrCreateAsync (private)     ── MODIFIED: drops inner SaveChangesAsync, returns (Invoice, IsNew)
        │
        ▼
 IIssuedInvoiceRepository (unchanged interface, unchanged implementation)
        │
        ▼
 ApplicationDbContext / EF Core change tracker (unchanged)
```

### Key Design Decisions

#### Decision 1: How the caller learns "was this newly created"
**Options considered:**
1. Have `GetOrCreateAsync` return a tuple `(IssuedInvoice Invoice, bool IsNew)`.
2. Check `invoice.CreationTime`/`LastModificationTime` heuristically in the caller after the fact.
3. Inspect EF Core's `EntityEntry.State` (`Context.Entry(invoice).State == EntityState.Added`) directly in `ExecuteImportInvoice`.

**Chosen approach:** Option 1 — explicit tuple return from `GetOrCreateAsync`, exactly as the spec's illustrative code shows.

**Rationale:** Option 2 is fragile (relies on incidental field values, and `IssuedInvoiceRepository.AddAsync` already sets `CreationTime`, so it would work but for the wrong reason — a future change to that side-effect silently breaks this check). Option 3 leaks EF Core-specific concerns (`EntityEntry`, `EntityState`) into the application-layer service, which currently depends only on the repository abstraction (`IIssuedInvoiceRepository`) and never touches `DbContext`/`ChangeTracker` directly — introducing that here would violate the existing repository-abstraction boundary documented in `docs/architecture/development_guidelines.md`. Option 1 is explicit, keeps the service decoupled from EF Core, and is a private-method signature change with zero blast radius (confirmed: `GetOrCreateAsync` has no other callers).

#### Decision 2: Where to branch on `IsNew`
**Options considered:**
1. Skip `UpdateAsync` entirely for new invoices (call only `SaveChangesAsync`).
2. Keep calling `UpdateAsync` for both paths but make `IssuedInvoiceRepository.UpdateAsync` itself detect `Added` state and no-op the `DbSet.Update` call.

**Chosen approach:** Option 1, in `ExecuteImportInvoice` — `if (!isNew) await _repository.UpdateAsync(invoice, cancellationToken);` then always `await _repository.SaveChangesAsync(cancellationToken);`.

**Rationale:** Option 2 pushes new/existing branching logic down into the generic-looking repository method, which is also reused by `AddAsync`/`UpdateAsync` overrides shared with unrelated call sites (any future caller of `IssuedInvoiceRepository.UpdateAsync`). Silently no-op'ing `Update` for `Added`-state entities inside the repository could mask genuine bugs elsewhere (a caller mistakenly calling `UpdateAsync` on a still-unflushed entity it just added would get no error, no warning). Keeping the branch in the one call site that has the context (`ExecuteImportInvoice`, which just learned `IsNew` from `GetOrCreateAsync`) keeps the fix local and legible, per the "surgical changes" project convention.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit only:
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — `GetOrCreateAsync` and `ExecuteImportInvoice`.

Test files to update/add (no new directories):
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` — mechanical updates to call-count/verify assertions (e.g. `ImportInvoicesAsync_WithSuccessfulBatch_ReturnsSuccessResult` currently doesn't assert `UpdateAsync` call count for the new-invoice path, but any test that does must be updated to `Times.Never` for new invoices).
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — **use this file's existing pattern**, not `InvoiceImportIntegrationTests.cs` (see Specification Amendments below), as the template for a new EF Core InMemory-backed test.

### Interfaces and Contracts
No public interface, DTO, or API contract changes. `IIssuedInvoiceRepository` and `IInvoiceImportService` are untouched. Internal signature change only:

```csharp
private async Task<(IssuedInvoice Invoice, bool IsNew)> GetOrCreateAsync(
    string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
{
    var found = await _repository.GetByIdAsync(key, cancellationToken);
    if (found == null)
    {
        found = factory();
        await _repository.AddAsync(found, cancellationToken);
        return (found, true);
    }
    return (found, false);
}
```

`ExecuteImportInvoice` changes:
```csharp
var (invoice, isNew) = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);
...
if (!isNew)
{
    await _repository.UpdateAsync(invoice, cancellationToken);
}
await _repository.SaveChangesAsync(cancellationToken);
```
The return type of `ExecuteImportInvoice` itself (`Task<IssuedInvoice>`) is unchanged.

### Data Flow
**New invoice:** `GetByIdAsync` → miss → `factory()` maps detail → `AddAsync` (tracked as `Added`, audit fields stamped, not flushed) → mapper re-applies detail onto the tracked entity → transformations run → ERP sync (`SyncSucceeded`/`SyncFailed` mutate the still-`Added` entity in memory) → **`UpdateAsync` skipped** → single `SaveChangesAsync` → one `INSERT` with final synced state.

**Re-imported invoice:** unchanged — `GetByIdAsync` → hit (tracked as `Unchanged`/queried) → mapper refresh → transformations → ERP sync → `UpdateAsync` (`DbSet.Update`, flips to `Modified`, stamps `LastModificationTime`/new `ConcurrencyStamp`) → `SaveChangesAsync` → one `UPDATE`.

Net effect: `N` new invoices in a batch cost `N` round trips instead of `2N`; re-imports are behaviorally and cost-wise identical to today.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Naive fix (delete inner `SaveChangesAsync` only, keep unconditional `UpdateAsync`) reintroduces a state-tracking bug (`Added`→`Modified` flip → failed `UPDATE` on nonexistent row) | High | Implement the `(Invoice, IsNew)` branch exactly as specified; cover with a real-`DbContext` test (InMemory provider), not a fully-mocked one — mocks cannot observe entity-state transitions. |
| Existing `InvoiceImportServiceTests.cs` assertions (fully Moq-based) pass even with the buggy naive fix, giving false confidence | Medium | Add a new test class using `ApplicationDbContext` + `UseInMemoryDatabase`, following the pattern already established in `IssuedInvoiceRepositoryTests.cs`, that drives `InvoiceImportService` (or at minimum `GetOrCreateAsync`'s new/existing branching via the real repository) through an add-then-flush cycle and asserts exactly one `SaveChangesAsync` and no `DbUpdateConcurrencyException`. |
| Losing crash-safety if an exception is thrown between `AddAsync` and the final `SaveChangesAsync` (e.g. ERP call throws unhandled) | Low | Already handled: `ExecuteImportInvoice`'s outer `try/catch` (lines 83/120-124) logs and rethrows; the batch loop in `ImportInvoicesAsync` catches per-invoice and never calls `SaveChangesAsync` for a failed invoice, so no partial row is persisted — this is unchanged behavior and is in fact the crash-safety improvement FR-3 asks to preserve. |
| Regression in the `IssuedInvoiceRepository.AddAsync`/`UpdateAsync` audit-field stamping (`CreationTime`, `ConcurrencyStamp`, `LastModificationTime`) for the new-invoice path, since `UpdateAsync` (which stamps `LastModificationTime`) is now skipped | Low | Working as intended — a newly created invoice should not have `LastModificationTime` set; `AddAsync` already stamps `CreationTime` and `ConcurrencyStamp`. Add an explicit assertion in the new EF-backed test that `LastModificationTime` is null after a new-invoice import and `CreationTime`/`ConcurrencyStamp` are set, to lock in this as intended (not accidental) behavior. |

## Specification Amendments
1. **Correct the spec's claim about `InvoiceImportIntegrationTests.cs`.** The file exists, but it is not a "real-context integration fixture" suitable for FR-2's concurrency-safety criterion — it is an HTTP-level test (`InvoiceImportTestFactory : HebloWebApplicationFactory`) that fully mocks `IInvoiceImportService` itself (see `services.AddSingleton(_invoiceImportServiceMock.Object)` in `InvoiceImportIntegrationTests.cs`). It exercises the `enqueue-async`/`job-status` HTTP endpoints only and never calls `ExecuteImportInvoice` or touches `ApplicationDbContext`. It is unrelated to this fix and should not be extended for it.
2. **Use `IssuedInvoiceRepositoryTests.cs`'s existing pattern instead.** That file already demonstrates the correct approach: a real `ApplicationDbContext` backed by `UseInMemoryDatabase(databaseName: $"...{Guid.NewGuid()}")`, exercising `IssuedInvoiceRepository.AddAsync`/`UpdateAsync`/`SaveChangesAsync` against a genuine EF Core change tracker. The new test for this fix should follow this same pattern — either as new `[Fact]`s appended to `IssuedInvoiceRepositoryTests.cs` if scoped to repository behavior, or as a new small test class in `backend/test/Anela.Heblo.Tests/Features/Invoices/` (e.g. `InvoiceImportServiceStateTrackingTests.cs`) that constructs `InvoiceImportService` with a real `IssuedInvoiceRepository` + InMemory `ApplicationDbContext` and mocks for the other four constructor dependencies (`IIssuedInvoiceSource`, `IIssuedInvoiceClient`, transformations, `IMapper`, `ILogger`). Recommend the latter, since it directly exercises the exact code path (`ExecuteImportInvoice`/`GetOrCreateAsync`) the spec is protecting, rather than only the repository in isolation.
3. **FR-2's "real EF Core change tracker" test requirement should reference this corrected target file**, not `InvoiceImportIntegrationTests.cs`.
4. No other functional changes to the spec are needed; FR-1 through FR-4, NFR-1 through NFR-3, and the illustrative interface shape are all consistent with the codebase as it exists today.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The fix is a same-PR, single-class change plus test additions; it can start immediately.
