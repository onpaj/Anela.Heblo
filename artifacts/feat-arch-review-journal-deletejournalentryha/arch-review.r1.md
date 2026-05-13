# Architecture Review: Eliminate redundant database fetch in DeleteJournalEntryHandler

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions in this codebase:

- **Sibling pattern already exists.** `UpdateJournalEntryHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:82-83`) already implements the exact pattern the spec proposes: `GetByIdAsync` → mutate aggregate → `UpdateAsync` → `SaveChangesAsync`. The delete handler is the outlier; this change brings it in line with the rest of the Journal feature.
- **No interface widening needed.** `IJournalRepository : IRepository<JournalEntry, int>` (`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:5`) already inherits `UpdateAsync` and `SaveChangesAsync` from the generic contract at `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs:16,22`. The spec's conditional ("if not exposed, widen") collapses to "no widening required."
- **Domain method is sufficient.** `JournalEntry.SoftDelete(userId, username)` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:82-91`) mutates only scalar fields on the aggregate root — it does not touch `ProductAssociations` or `TagAssignments`. The eager includes in `GetByIdAsync` are therefore not strictly required for soft-delete; spec correctly defers that question to a follow-up to keep this diff surgical.
- **Vertical Slice respected.** Change is fully contained within the Journal slice. No cross-module coupling, no shared DbContext concerns, no contract leakage.
- **Marketing parallel.** `MarketingActionRepository.DeleteSoftAsync` has the identical anti-pattern. Spec correctly excludes it — bundling would expand the diff and the Marketing handler's signature differs (`currentUser.Name ?? "Unknown User"`).

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│  DeleteJournalEntryHandler  (Application layer)              │
│                                                              │
│  1. ICurrentUserService.GetCurrentUser()                     │
│     └── unauthenticated? → return UnauthorizedJournalAccess  │
│                                                              │
│  2. IJournalRepository.GetByIdAsync(id, ct)        ──┐       │
│     └── null? → return JournalEntryNotFound          │       │
│                                                      │       │
│  3. entry.SoftDelete(userId, username)  [domain]     │       │
│                                                      ▼       │
│  4. IJournalRepository.UpdateAsync(entry, ct)  ────┐         │
│  5. IJournalRepository.SaveChangesAsync(ct)        │         │
│  6. _logger.LogInformation(...)                    │         │
│  7. return DeleteJournalEntryResponse { Id }       │         │
└────────────────────────────────────────────────────┼─────────┘
                                                     │
                                                     ▼
                          ┌──────────────────────────────────┐
                          │ JournalRepository :              │
                          │   BaseRepository<JournalEntry,…> │
                          │                                  │
                          │ • GetByIdAsync override (kept)   │
                          │ • DeleteSoftAsync (REMOVED)      │
                          │ • UpdateAsync (inherited)        │
                          │ • SaveChangesAsync (inherited)   │
                          └──────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Handler-direct persistence vs. entity-accepting overload

**Options considered:**
- (A) Remove `DeleteSoftAsync`; handler calls `SoftDelete` + `UpdateAsync` + `SaveChangesAsync` directly. *(Spec choice)*
- (B) Keep `DeleteSoftAsync` but add an `(JournalEntry entry, …)` overload that skips the second fetch.
- (C) Modify the existing `DeleteSoftAsync(int, …)` to accept an already-loaded entity through an out-parameter or tuple.

**Chosen approach:** (A) — Handler-direct persistence.

**Rationale:** Option A is consistent with the sibling `UpdateJournalEntryHandler`, eliminates dead code (the silent null branch), and shrinks the repository surface. Option B preserves the repository method but creates two ways to do the same thing, inviting future drift. Option C is awkward and still leaves an ID-based overload that calls `GetByIdAsync` twice. The spec correctly excludes (B) — keeping module-level consistency is more valuable than a thinner handler.

#### Decision 2: Preserve eager-loading in `GetByIdAsync`

**Options considered:**
- (A) Leave includes (`ProductAssociations`, `TagAssignments.Tag`) as-is. *(Spec choice)*
- (B) Add a lightweight `GetByIdForDeleteAsync` without includes.

**Chosen approach:** (A) — preserve current behavior.

**Rationale:** `JournalEntry.SoftDelete` does not touch the related collections, so the includes are not functionally required. However, `GetByIdAsync` is shared with reads that *do* need them, and introducing a delete-specific overload expands the contract for a follow-up question. Scope discipline wins; revisit in a separate ticket if performance telemetry justifies it.

#### Decision 3: Persistence semantics — `UpdateAsync` then `SaveChangesAsync`

**Options considered:**
- (A) Call both `UpdateAsync` and `SaveChangesAsync` in the handler. *(Spec choice, matches sibling pattern)*
- (B) Skip `UpdateAsync` since EF's change tracker already detects mutations on tracked entities.

**Chosen approach:** (A).

**Rationale:** Functionally `UpdateAsync` is redundant for an already-tracked entity (it just calls `DbSet.Update`, marking all properties modified — see `BaseRepository.UpdateAsync` at `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:70-74`). But consistency with `UpdateJournalEntryHandler` matters more than micro-optimization; deviating here would create a new inconsistency to replace the one being fixed. Keep the call.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs` | Replace line 51 with `SoftDelete` + `UpdateAsync` + `SaveChangesAsync` sequence. |
| `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` | Delete line 7 (the `DeleteSoftAsync` method declaration). |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | Delete lines 27-36 (the `DeleteSoftAsync` implementation). |
| `backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs` | Update mock verifications (see Data Flow section). |

Do **not** touch:
- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs:12`
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:27`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs:94`
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs`

### Interfaces and Contracts

`IJournalRepository` after change (Journal-specific methods only — base methods inherited):

```csharp
public interface IJournalRepository : IRepository<JournalEntry, int>
{
    // DeleteSoftAsync REMOVED — handler uses inherited UpdateAsync + SaveChangesAsync
    Task<PagedResult<JournalEntry>> GetEntriesAsync(JournalQueryCriteria criteria, CancellationToken cancellationToken = default);
    Task<PagedResult<JournalEntry>> SearchEntriesAsync(JournalSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<List<JournalEntry>> GetEntriesByProductAsync(string productCode, CancellationToken cancellationToken = default);
    Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(IEnumerable<string> productCodes, CancellationToken cancellationToken = default);
}
```

External contracts unchanged:
- `DeleteJournalEntryRequest` / `DeleteJournalEntryResponse` — unchanged.
- HTTP endpoint signature — unchanged.
- Error codes (`UnauthorizedJournalAccess`, `JournalEntryNotFound`) — unchanged.
- Log message format — unchanged.

### Data Flow

**Success path (single DB fetch):**
1. Handler reads `CurrentUser` from `ICurrentUserService`.
2. Handler calls `_journalRepository.GetByIdAsync(request.Id, ct)` → **single SELECT** with includes.
3. Handler invokes `entry.SoftDelete(currentUser.Id, currentUser.Name)` → mutates tracked entity in memory.
4. Handler calls `_journalRepository.UpdateAsync(entry, ct)` → marks state as Modified (no DB I/O).
5. Handler calls `_journalRepository.SaveChangesAsync(ct)` → **single UPDATE**.
6. Handler logs and returns `DeleteJournalEntryResponse { Id }`.

**Unauthenticated path:** Early return; **zero DB calls.**

**Not-found path:** One `GetByIdAsync` call; **no UPDATE issued.**

**Test mock surface after change:**
```csharp
_repositoryMock.Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(existingEntry);
_repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
_repositoryMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

// Verify on success:
_repositoryMock.Verify(x => x.UpdateAsync(It.Is<JournalEntry>(e => e.IsDeleted && e.DeletedByUserId == userId), It.IsAny<CancellationToken>()), Times.Once);
_repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

// Verify on unauthorized/not-found:
_repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()), Times.Never);
_repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
```

The success test should assert on `e.IsDeleted == true && e.DeletedByUserId == userId && e.DeletedByUsername == "Test User"` — these are the fields `JournalEntry.SoftDelete` toggles per `JournalEntry.cs:82-91`. This resolves the spec's FR-4 open ambiguity about "whatever flag `SoftDelete` toggles."

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden caller of `IJournalRepository.DeleteSoftAsync` outside the inspected scope. | Low | `grep` confirms only `DeleteJournalEntryHandler` consumes it (per spec FR-5). Compiler will catch any missed caller; the build gate forces resolution. |
| Test fakes other than `Mock<IJournalRepository>` (e.g., hand-rolled fakes, integration test stubs) still implement `DeleteSoftAsync`. | Low | Removing the method from the interface forces compile errors on every implementation. Sweep with `grep -rn DeleteSoftAsync backend/test` and remove from any fakes. |
| EF change tracker behavior: with `UpdateAsync` (which calls `DbSet.Update`), all properties are marked Modified, potentially triggering an `UPDATE` that writes every column rather than only the soft-delete fields. | Low | Matches existing behavior of `UpdateJournalEntryHandler` and the removed `DeleteSoftAsync`. No regression. |
| Silent drift between Journal and Marketing modules (Marketing keeps the anti-pattern). | Low | Spec explicitly defers Marketing to a follow-up ticket. Architecture review notes this for tracking; no action in this PR. |
| Integration test asserting "exactly one SELECT" (FR-1 acceptance) may be flaky if EF emits relationship-fixup queries. | Medium | If the unit-test mock verification (`GetByIdAsync` Times.Once) is sufficient evidence, prefer that over a SQL-counting integration test. Spec offers both — pick the unit-test verification to avoid EF-version-coupled brittleness. |

## Specification Amendments

1. **FR-3 conditional clause is moot — drop it.** The spec says "If the existing `IRepository<,>` abstraction does not expose `UpdateAsync` / `SaveChangesAsync` publicly, … add them to the interface contract." Verified at `backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs:16,22` — both methods are already public on the generic contract, inherited transitively by `IJournalRepository`. **No interface widening is required.** The handler can call them directly.

2. **FR-4 open ambiguity resolved.** The spec parenthetically asks "whatever flag `SoftDelete` toggles." Confirmed at `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:82-91`: `SoftDelete` sets `IsDeleted = true`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`. Tests should assert on `IsDeleted` and `DeletedByUserId` at minimum.

3. **NFR-1 follow-up clarified.** `JournalEntry.SoftDelete` does *not* touch `ProductAssociations` or `TagAssignments`, so the eager includes in `GetByIdAsync` are confirmed unnecessary for the delete path. The spec correctly defers this to a follow-up; the architecture review endorses that scope boundary.

4. **FR-1 acceptance criterion — prefer the unit test.** The dual acceptance ("mock verifies `GetByIdAsync` Times.Once" **or** "integration test counts SELECT statements") is redundant. The unit-test form is sufficient, less brittle, and consistent with the existing test class style. Implementers should not add a Testcontainers-based SQL-counting test for this change.

5. **FR-4 verification helper.** Replace the spec's vague "the entity passed to `UpdateAsync` reflects the soft-delete state" with the explicit Moq assertion: `Verify(x => x.UpdateAsync(It.Is<JournalEntry>(e => e.IsDeleted), It.IsAny<CancellationToken>()), Times.Once)`. Document this in the implementation prompt to avoid weak assertions.

6. **Out of Scope addition.** Add: "Removing the eager-loading includes from `JournalRepository.GetByIdAsync` for the delete path." The spec already implies this in NFR-1; making it explicit prevents scope creep during implementation.

## Prerequisites

None. All dependencies exist:

- ✅ `JournalEntry.SoftDelete` exists (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:82`).
- ✅ `IRepository<JournalEntry, int>.UpdateAsync` and `SaveChangesAsync` are public and inherited (`backend/src/Anela.Heblo.Xcc/Persistance/IRepository.cs:16,22`).
- ✅ `BaseRepository<JournalEntry, int>` implements both (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:70,97`).
- ✅ Sibling handler proves the pattern works in this codebase (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:82-83`).
- ✅ No migrations, no configuration, no NuGet, no DI registration changes.

Implementation can begin immediately.