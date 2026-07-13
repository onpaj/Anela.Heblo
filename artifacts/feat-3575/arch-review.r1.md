# Architecture Review: Revert tracked entity mutations for failed re-imported invoices

## Skip Design: true

## Architectural Fit Assessment

This is a narrow, well-isolated bug fix that aligns cleanly with existing patterns in the codebase and requires no architectural change beyond one interface addition.

The root cause is a genuine anomaly, not a systemic issue: `IssuedInvoiceRepository.GetByIdAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:22-26`) is confirmed to be the **only** repository override in the codebase that returns a tracked entity instead of using `.AsNoTracking()`. I spot-checked this claim against the general repository pattern: `BaseRepository<TEntity, TKey>.GetByIdAsync` (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:25-28`) uses `DbSet.FindAsync`, which also returns/attaches a tracked entity by default — so the "tracked read" behavior is actually the *generic base's* default too, and `IssuedInvoiceRepository` doesn't even need its override to get tracking; it overrides `GetByIdAsync` only to swap `FindAsync` for `FirstOrDefaultAsync` (no behavioral difference relevant here). The spec's claim that "every other repository already uses `.AsNoTracking()`" refers to repositories that added *explicit* overrides for AsNoTracking reads — the base method itself is tracked-by-default. This doesn't change the fix, but it's worth noting for the amendment below: the underlying tracked-read hazard is the base repository's default, not unique to Invoices — it simply hasn't bitten any other importer yet because (per the spec's own scan) `MarketingInvoiceImportService` never mutates a loaded tracked entity in a loop with per-item `SaveChangesAsync`.

The chosen fix — adding `RevertTrackedChangesAsync` to `IIssuedInvoiceRepository` only, not to the generic `IRepository<TEntity, TKey>` — is the right call for this change. It's minimal, reversible, and doesn't force every repository consumer to reason about change-tracker cleanup. Promoting it to the base is explicitly and correctly deferred until a second consumer appears (YAGNI, consistent with `ADR-002`'s "generic repository in Xcc, extended per feature" model).

Integration point: `InvoiceImportService.ExecuteImportInvoice`'s outer `catch` (`InvoiceImportService.cs:120-124`). This is a single, well-understood call site — no other code path touches this method.

## Proposed Architecture

### Component Overview

```
ImportInvoicesAsync (foreach invoice in batch, SAME DbContext/repo instance)
  │
  ├─ ExecuteImportInvoice(invoiceDetail)
  │    │
  │    ├─ GetOrCreateAsync ──► IIssuedInvoiceRepository.GetByIdAsync (tracked read)
  │    │                       └─ if new: AddAsync + SaveChangesAsync  (isNew = true)
  │    │                       └─ if existing: return tracked entity  (isNew = false)
  │    │
  │    ├─ _mapper.Map(invoiceDetail, invoice)      ← mutates tracked entity (Modified)
  │    ├─ foreach transformation: TransformAsync   ← can throw
  │    ├─ inner try: client.SaveAsync / SyncSucceeded|SyncFailed
  │    ├─ UpdateAsync + SaveChangesAsync            ← normal flush point
  │    │
  │    └─ outer catch (ex):
  │         if (!isNew) await _repository.RevertTrackedChangesAsync(invoice, ct);  ← NEW
  │         log; throw;
  │
  └─ per-invoice catch (ImportInvoicesAsync) ── records Failed, continues loop
       (next invoice's SaveChangesAsync would otherwise flush the abandoned Modified entity)
```

No new components, no new module, no new persistence concept — this is a single method added to an existing repository interface/implementation pair, plus a control-flow tweak in one private method.

### Key Design Decisions

#### Decision 1: Where does the revert capability live — `IIssuedInvoiceRepository` vs. generic `IRepository<TEntity, TKey>`

**Options considered:**
- (a) Add `RevertTrackedChangesAsync` to `IRepository<TEntity, TKey>`/`BaseRepository`.
- (b) Add it only to `IIssuedInvoiceRepository`/`IssuedInvoiceRepository`.
- (c) Give `InvoiceImportService` direct access to `ApplicationDbContext` and call `Entry(...).State` itself.

**Chosen approach:** (b), as specified.

**Rationale:** (a) is broader than the confirmed problem — one consumer exists today, and generic repository consumers that use `.AsNoTracking()` reads have no use for this method at all (calling `Context.Entry()` on an untracked entity throws `InvalidOperationException` in the strictest interpretation, or silently attaches it as `Unchanged` depending on read path — either way it's a foot-gun to expose universally). (c) violates the module boundary this codebase enforces deliberately: `InvoiceImportService` lives in `Anela.Heblo.Application`, and `ApplicationDbContext` is a `Anela.Heblo.Persistence` concern reached only through repository interfaces defined in `Anela.Heblo.Domain` — giving the Application layer a raw `DbContext` reference breaks that layering and is not how any other service in this codebase is written (confirmed: `InvoiceImportService` currently depends only on `IIssuedInvoiceRepository`, `IIssuedInvoiceSource`, `IIssuedInvoiceClient` — all interfaces). (b) is the narrowest change that respects both boundaries and matches `ADR-004`'s expectation that a repository's owning module controls its own contract surface.

#### Decision 2: `EntityState.Unchanged` reset vs. `ReloadAsync()` vs. `Detach()`

**Options considered:** `Context.Entry(entity).State = EntityState.Unchanged` (in-memory, no I/O), `Context.Entry(entity).ReloadAsync()` (re-queries the DB), `Context.Entry(entity).State = EntityState.Detached` (removes from tracker entirely).

**Chosen approach:** `EntityState.Unchanged`, as specified.

**Rationale:** This is architecturally sound for the stated goal — preventing the abandoned mutation from being flushed by a later `SaveChangesAsync` in the same batch — but the review surfaced one nuance worth calling out explicitly for whoever implements this (not a reason to change the approach, a reason to document it):

Setting `Entry(entity).State = EntityState.Unchanged` does not roll the CLR object's property values back to what was loaded from the DB. EF Core's state-transition implementation treats this assignment as "accept these current in-memory values as the new baseline" (equivalent to an implicit `AcceptChanges()` for that entry) — it clears the `Modified` flag by making Original == Current, not by making Current == Original. The net effect for *this* bug is exactly correct: because the DB row was never written with the mutated values (no `SaveChangesAsync` happened between the mutation and the revert), the persisted row is untouched, and `ChangeTracker.DetectChanges()` on a later `SaveChangesAsync` call finds no delta for this entity and skips it — which is precisely FR-2's and FR-4's acceptance criteria (DB row unchanged). But the in-memory `IssuedInvoice` object handed back from `ExecuteImportInvoice`'s exception path (and any other in-scope code that might resolve the same entity from the DbContext's identity map by the same `Id` later in the batch, e.g. if the same invoice code appeared twice) would still observe the mutated field values, now falsely labeled `Unchanged`. Today nothing in `ImportInvoicesAsync`/`ExecuteImportInvoice` re-reads a failed invoice later in the same batch, so this has no observable consequence given current call patterns — but it's a latent trap for a future change (e.g., a retry-within-batch feature) and should be a one-line code comment at the `RevertTrackedChangesAsync` call site, not just spec prose.

`ReloadAsync()` was correctly rejected by the spec on cost grounds (extra `SELECT` per failure) and because it targets a documented risk only on the exception path (not the happy path), so the trade-off is fine.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Modify in place:

- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` — add the `RevertTrackedChangesAsync` signature next to the other Invoices-specific members (after `GetHeadersByDateAsync` is fine; keep read/write members grouped, this is a write-adjacent operation).
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — add the implementation. Use the inherited `Context` field (protected on `BaseRepository<TEntity, TKey>`) rather than introducing a new field — it's already available to this subclass.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs` — modify `ExecuteImportInvoice` to track `isNew` and call the revert in the outer `catch`.
- New test file alongside the existing ones in `backend/test/Anela.Heblo.Tests/Features/Invoices/` — see Data Flow / test note below. Follow the `PackageRepositoryAddMissingTests.cs`-style pattern already used in this codebase (`backend/test/Anela.Heblo.Tests/Features/Packaging/PackageRepositoryAddMissingTests.cs`): construct `ApplicationDbContext` directly with `UseInMemoryDatabase(Guid.NewGuid().ToString())`, new up the real repository against it, no `WebApplicationFactory`/HTTP layer needed. `InvoiceImportIntegrationTests.cs` in this same folder is HTTP-endpoint-level (job enqueue/status) and mocks `IInvoiceImportService` entirely — it is *not* the right pattern to extend for this test; the in-memory-DbContext-plus-real-repository pattern from `PackageRepositoryAddMissingTests.cs` is the closer match to what FR-4 needs (a real change tracker, no HTTP, but the full `InvoiceImportService` wired against real repository + mocked `IIssuedInvoiceSource`/`IIssuedInvoiceClient`/transformations).

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository
Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
```

```csharp
// Anela.Heblo.Persistence.Invoices.IssuedInvoiceRepository
public Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
{
    Context.Entry(entity).State = EntityState.Unchanged;
    return Task.CompletedTask;
}
```

`ExecuteImportInvoice` control-flow change — introduce a local `isNew` flag from `GetOrCreateAsync`'s branch (the spec is correct that this is "already effectively available," but today `GetOrCreateAsync` doesn't surface it to the caller — it must be threaded out, e.g. by changing `GetOrCreateAsync`'s return to `(IssuedInvoice invoice, bool isNew)` or an out-parameter):

```csharp
var (invoice, isNew) = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);
...
catch (Exception ex)
{
    if (!isNew)
    {
        await _repository.RevertTrackedChangesAsync(invoice, cancellationToken);
    }
    _logger.LogError(ex, "Error occurred while importing invoice: {InvoiceNumber}", invoiceDetail.Code);
    throw;
}
```

Note the `invoice` variable must be visible in the `catch` block, which means it has to be declared/assigned before the `try`'s scope ends — a `bool isNew` and `IssuedInvoice? invoice` declared above the `try` (assigned inside) is the straightforward shape; a bare `try { var invoice = ... }` won't compile with `invoice` referenced from `catch`.

### Data Flow

Happy path (re-import, success): unchanged from today — `GetByIdAsync` (tracked) → `_mapper.Map` mutates → transformations → client save → `UpdateAsync`/`SaveChangesAsync` flushes. No revert call.

Failure path (re-import, exception before final save): `GetByIdAsync` (tracked, `isNew=false`) → `_mapper.Map` mutates (entity now `Modified` in-memory) → transformation throws → outer `catch` calls `RevertTrackedChangesAsync` (entity flips to `Unchanged`, current mutated values become the new "original" baseline in-memory, but nothing has been written to the DB yet) → exception re-thrown → `ImportInvoicesAsync`'s per-invoice `catch` records `Failed`, loop continues → next invoice's own `SaveChangesAsync` runs `DetectChanges()`, finds no delta for the reverted entity (since Original==Current now), skips it → DB row for the failed invoice remains exactly as it was before the batch started touching it.

Failure path (new invoice, exception after creation): unaffected — `isNew=true`, no revert call, matching the spec's explicit Out of Scope carve-out.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `EntityState.Unchanged` does not restore CLR property values — only prevents persistence, doesn't "undo" the mutation in memory | Low | Document with an inline code comment at the call site (see Decision 2); no behavior change needed since no code currently re-reads a failed invoice later in the same batch scope. Called out here so a future change (e.g., duplicate invoice codes within one batch, or a retry-within-batch feature) doesn't silently reintroduce a variant of this bug by trusting the in-memory object post-revert. |
| `GetOrCreateAsync`'s signature change (surfacing `isNew`) is a breaking change to a private method, but any other private/internal caller could be missed in a hasty diff | Low | `GetOrCreateAsync` is `private` and has exactly one call site (`ExecuteImportInvoice`) — confirmed by reading the file in full; safe to change its signature. |
| FR-4's test requires wiring a real `IssuedInvoiceRepository` + `InvoiceImportService` with mocked `IIssuedInvoiceSource`/`IIssuedInvoiceClient`/transformations against an EF Core InMemory `ApplicationDbContext` — this is more setup than the existing mocked `InvoiceImportServiceTests.cs`, and no existing test in this codebase wires `InvoiceImportService` end-to-end against a real DbContext today | Medium | Budget extra implementation time for this harness; reuse `PackageRepositoryAddMissingTests.cs`'s `UseInMemoryDatabase` + direct `ApplicationDbContext` construction pattern (proven in this codebase) rather than inventing a new one or reaching for `HebloWebApplicationFactory` (unnecessary HTTP/DI overhead for this test). |
| EF Core InMemory provider has known differences from the relational provider in transaction/concurrency semantics | Low | Not relevant here — the test only needs to observe `SaveChangesAsync`'s decision of *whether* to emit an update for a given tracked entity, which InMemory faithfully replicates via the same `ChangeTracker`/`DetectChanges` machinery as the relational provider. |

## Specification Amendments

None required to proceed — the spec is implementation-ready. One clarification worth folding in if the spec is touched again before implementation: the "Background" section's claim that `IssuedInvoiceRepository.GetByIdAsync` is uniquely tracked because other repositories "use `.AsNoTracking()`" slightly understates the mechanism — the generic `BaseRepository.GetByIdAsync` (via `DbSet.FindAsync`) is *also* tracked by default; other repositories simply don't hit the tracked-load-then-mutate-then-conditionally-fail pattern that `InvoiceImportService` does. This doesn't change the fix's scope or design, but a future reader auditing "is this the only tracked repository" should know the invariant is about *usage pattern*, not about `IssuedInvoiceRepository` being uniquely tracked among repositories.

## Prerequisites

None beyond what's already in the repo. No new infrastructure, no config, no migration (confirmed: no schema change, per spec's Data Model section — verified `IssuedInvoice` entity requires no changes). The EF Core InMemory test pattern needed for FR-4 already exists in this codebase (`PackageRepositoryAddMissingTests.cs` and similar `*RepositoryTests.cs` files under `backend/test/Anela.Heblo.Tests/Features/`/`Persistence/`), so no new test infrastructure needs to be built — only reused.
