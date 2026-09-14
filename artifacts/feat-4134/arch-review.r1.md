# Architecture Review: Remove Redundant UpdateAsync Calls in Purchase Order Handlers

## Skip Design: true

Backend-only persistence cleanup. No new or changed UI components, screens, layouts, or API contracts. No design pass is needed.

## Architectural Fit Assessment

The spec's diagnosis is correct and I verified it directly against the code:

- `BaseRepository<TEntity, TKey>.UpdateAsync` (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:70-74`) is a one-line wrapper: `DbSet.Update(entity)`. `DbSet.Update` internally sets `context.Entry(entity).State = EntityState.Modified`, which — for an entity that is *already attached* — flags every scalar property as modified, not just the ones actually changed.
- `BaseRepository.GetByIdAsync` (line 25-28) uses `DbSet.FindAsync(id, ...)`. `FindAsync` always attaches/tracks the returned entity (`EntityState.Unchanged`) unless the context has global no-tracking behavior configured (it doesn't here — nothing in `ApplicationDbContext`/these handlers opts out). So by the time `UpdatePurchaseOrderStatusHandler` and `UpdatePurchaseOrderInvoiceAcquiredHandler` call `_repository.UpdateAsync(purchaseOrder, ...)`, the entity is already tracked, and the call does nothing except downgrade EF's per-property diffing to an all-columns write.
- The reference handler, `UpdatePurchaseOrderHandler.cs`, loads via `GetByIdWithDetailsAsync` (also `FindAsync`-based tracking under the hood) and calls **only** `SaveChangesAsync`, with an explicit comment: `// Entity is already tracked from GetByIdWithDetailsAsync, EF will auto-detect changes`. This is the locally-established correct pattern for in-place mutation of an already-tracked aggregate, and it is what the two handlers in scope should match.

This is a pure internal-implementation fix: no interface, DTO, controller, or MediatR contract changes. It fits entirely inside the existing Vertical Slice / repository-per-module conventions in `docs/architecture/development_guidelines.md` (Persistence Guidelines, ADR-001, ADR-002, ADR-004) — none of which are touched or implicated by this change. `IPurchaseOrderRepository.UpdateAsync` and `BaseRepository.UpdateAsync` are not modified, consistent with the spec's Out of Scope section.

**Context worth flagging (not for this fix):** a repo-wide grep shows the same "load via tracked-Find, mutate, call `UpdateAsync`, then `SaveChangesAsync`" pattern in roughly 25+ other handlers/services (PackingMaterials, Journal, Marketing, BackgroundJobs, Logistics, Manufacture, Dashboard, Bank, InvoiceClassification, Catalog). This confirms the brief's "inconsistency" framing is accurate at the *module* level (three Purchase handlers, three patterns) but understates it at the *codebase* level — the redundant-`UpdateAsync`-after-tracked-load pattern is actually the majority pattern, not the minority one. The spec correctly scopes this fix to only the two named Purchase handlers and defers a broader sweep as a separate concern; I agree with that scoping (see Specification Amendments below for the one addition I'd make).

## Proposed Architecture

No new components. This is a two-line deletion plus matching test updates inside the existing structure.

### Component Overview

```
UpdatePurchaseOrderStatusRequest ──▶ UpdatePurchaseOrderStatusHandler ──▶ IPurchaseOrderRepository
                                          │                                   │
                                          │ GetByIdAsync (tracked)            │
                                          │ purchaseOrder.ChangeStatus(...)   │
                                          │ [DELETE: UpdateAsync(...)]        │
                                          │ SaveChangesAsync(...) ────────────▶ EF change tracker diffs
                                          ▼                                     tracked entity → minimal UPDATE
                                  UpdatePurchaseOrderStatusResponse

UpdatePurchaseOrderInvoiceAcquiredRequest ──▶ UpdatePurchaseOrderInvoiceAcquiredHandler ──▶ IPurchaseOrderRepository
                                                   │  (same shape as above)
                                                   ▼
                                  UpdatePurchaseOrderInvoiceAcquiredResponse

(Reference, unchanged) UpdatePurchaseOrderHandler ──▶ GetByIdWithDetailsAsync (tracked) ──▶ mutate ──▶ SaveChangesAsync only
```

No new classes, interfaces, or files. `IPurchaseOrderRepository` and `BaseRepository<TEntity, TKey>` are untouched.

### Key Design Decisions

#### Decision 1: Delete the call site vs. change `BaseRepository.UpdateAsync` semantics

**Options considered:**
1. Remove the two redundant `_repository.UpdateAsync(...)` call sites; rely on EF's tracked-entity diffing.
2. Change `BaseRepository.UpdateAsync` to only mark explicitly-dirty properties (e.g. via `Entry(entity).State` left as-is if already tracked, or a property-diffing helper), so the call becomes harmless everywhere it's used.
3. Leave the code as-is; treat the extra I/O as an acceptable cost of consistency across all "update" handlers.

**Chosen approach:** Option 1, exactly as the spec defines it — delete the call in both handlers, keep `SaveChangesAsync`.

**Rationale:** `BaseRepository.UpdateAsync` is a generic, shared primitive used by ~25+ other call sites across unrelated modules, some of which *do* attach not-yet-tracked entities (its documented legitimate use, per the spec's Out of Scope section) where `DbSet.Update` is the correct/necessary call. Changing its semantics (option 2) is a cross-cutting behavior change to shared infrastructure that needs its own review and test coverage across every consumer — far outside a "tiny tech-debt fix" and explicitly out of scope per the brief and spec. Option 3 leaves a known, cheap-to-fix defect in place for no benefit. Option 1 is a minimal, local, behavior-preserving change that brings these two handlers in line with the already-proven-correct `UpdatePurchaseOrderHandler` pattern.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Edit in place:
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusHandler.cs` — delete the `UpdateAsync` line inside the `try` block, immediately before `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderInvoiceAcquired/UpdatePurchaseOrderInvoiceAcquiredHandler.cs` — same deletion.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderStatusHandlerTests.cs` — remove/adjust the `UpdateAsync` verification in `Handle_ShouldCallRepositoryMethods`.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderInvoiceAcquiredHandlerTests.cs` — remove/adjust the `UpdateAsync` verification in `Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist`, and rewire `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` to throw from the mocked `SaveChangesAsync` instead of `UpdateAsync`.

### Interfaces and Contracts

None change. `IPurchaseOrderRepository`, `BaseRepository<TEntity, TKey>`, `UpdatePurchaseOrderStatusRequest/Response`, `UpdatePurchaseOrderInvoiceAcquiredRequest/Response` are all unchanged in shape and semantics — confirmed by reading both handler files and `BaseRepository.cs` in full.

### Data Flow

Unchanged at the observable level. Internally: `GetByIdAsync` → `DbSet.FindAsync` attaches the entity as `Unchanged` → domain method (`ChangeStatus` / `SetInvoiceAcquired`) mutates in-memory properties, which EF's snapshot-based change tracker marks dirty per-property as they're set → `SaveChangesAsync` calls `DetectChanges()` and emits an `UPDATE` containing only the properties actually mutated (e.g. `Status`, `UpdatedAt`, `UpdatedBy` for the status handler). Removing `UpdateAsync` removes the intermediate step that was overriding this per-property state with a blanket `Modified` on every scalar property.

For the test rewrite in `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError`: since these are Moq-based unit tests against an `IPurchaseOrderRepository` mock (not a real `DbContext`/in-memory provider), the assertion of "no all-columns write" is architectural (verified above by reading `BaseRepository.cs`), not something the existing unit-test style can directly assert against a mock. The spec's own acceptance criteria for FR-1/FR-2 already account for this by asking for assertions on final entity/property state and call counts rather than SQL shape — no change needed to that approach; it matches the existing test style in this test class (Moq + FluentAssertions, no EF in-memory provider used elsewhere in these two files).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Deleting the `UpdateAsync` call silently breaks persistence if the entity were ever loaded no-tracking (e.g. future `AsNoTracking()` added upstream in `GetByIdAsync`) | Low | `GetByIdAsync` uses `DbSet.FindAsync`, which always tracks; verified directly in `BaseRepository.cs`. The spec already flags this dependency explicitly (Dependencies section) — no code change needed now, but a future PR that adds `AsNoTracking()` to `GetByIdAsync` must audit all callers that skip `UpdateAsync`/`Attach`, not just these two. |
| `Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError` rewrite accidentally weakens coverage of the `catch (Exception ex)` path | Low | Spec FR-2/FR-3 already require the rewritten test to throw from the mocked `SaveChangesAsync` with the same exception/message and assert the same error code — straightforward 1:1 swap of which mock throws. |
| Broader codebase has ~25+ other handlers with the same redundant-`UpdateAsync` pattern; a future contributor may assume this fix "already covers" the codebase | Low | Explicitly scoped out in the spec's Out of Scope section; call this out in the PR description so reviewers don't conflate this fix's small footprint with a full sweep (see Specification Amendments). |

## Specification Amendments

The spec is architecturally sound and needs no functional changes. One documentation-only addition:

- **PR description note:** Recommend the implementer's PR description explicitly state that the same redundant-`UpdateAsync`-after-tracked-load pattern exists in ~25+ other handlers across other modules (PackingMaterials, Journal, Marketing, BackgroundJobs, Logistics, Manufacture, Dashboard, Bank, InvoiceClassification, Catalog — confirmed via grep during this review) and that fixing those is intentionally out of scope here. This isn't a spec change, just makes the deliberate scope boundary visible to the reviewer so it isn't mistaken for an oversight. No new arch-review issue needs to be filed proactively — leave that to a future dedicated finding if/when someone chooses to pursue it, per the spec's own Out of Scope wording ("it could be raised as a separate arch-review finding if desired").

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — this is a same-behavior code deletion plus test updates, buildable and testable in isolation. Implementation can start immediately.
