I have enough context. The Journal module's `DeleteJournalEntryHandler` already implements exactly the proposed pattern — this is the precedent. Now I'll write the review.

# Architecture Review: Remove embedded `SaveChangesAsync` from `IMarketingActionRepository.DeleteSoftAsync`

## Skip Design: true

Backend-only refactor. No new endpoints, no contract changes, no UI surface touched.

## Architectural Fit Assessment

The proposal aligns cleanly with what the codebase already establishes:

- **Caller-controlled persistence** is the dominant pattern in this solution. `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` already use `UpdateAsync` + `SaveChangesAsync` from the handler.
- **A direct precedent exists**: `DeleteJournalEntryHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs:51-53`) implements the exact sequence proposed — `entry.SoftDelete(...) → UpdateAsync → SaveChangesAsync`. This refactor brings Marketing into parity with Journal and removes the outlier.
- **Generic `IRepository<,>`** (`BaseRepository<TEntity,TKey>`) does not expose a `DeleteSoftAsync`. The current Marketing-specific addition is a leak of caller concerns into the repository abstraction. Removing it makes `IMarketingActionRepository` lean and conventional.
- **No new abstraction** (no Unit-of-Work, no `IUnitOfWork`, no service wrapper) is needed — the handler already holds all required collaborators (`ICurrentUserService`, repository, `_outlookSync`, options, logger).

Integration points are narrow: one repository interface, one repository implementation, one handler, two test files.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ DeleteMarketingActionHandler  (Application)                     │
│                                                                 │
│   1. GetByIdAsync(id)            ◄── single load (was 2 loads) │
│   2. if (OutlookEventId) DeleteEventAsync(outlookSync)         │
│   3. action.SoftDelete(userId, username)   (domain mutation)   │
│   4. UpdateAsync(action)         (EF state marker)             │
│   5. SaveChangesAsync()          (commit)                      │
└──────────────────┬──────────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────────┐
│ IMarketingActionRepository  (Domain)                            │
│   - GetByIdAsync, UpdateAsync, SaveChangesAsync  (inherited)    │
│   - GetPagedAsync, GetForCalendarAsync, ...                     │
│   ─ DeleteSoftAsync   ❌ REMOVED                                │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Remove `DeleteSoftAsync` entirely (Option B), do not keep a thinned wrapper (Option A)

**Options considered:**
- **A.** Keep `DeleteSoftAsync` on the interface, drop only the embedded `SaveChangesAsync` and accept the entity instead of an id.
- **B.** Delete `DeleteSoftAsync` from interface + implementation. Handler inlines the three lines.

**Chosen approach:** **B** (matches the spec).

**Rationale:** A one-line "wrapper" that just calls `entity.SoftDelete(...)` + `UpdateAsync(...)` adds an abstraction with zero behaviour. The Journal module's precedent inlines it. Keeping the wrapper would re-introduce a discoverability hazard (developers expect a save). Deletion is also the only way to eliminate the double DB load — keeping the method by id forces the second `GetByIdAsync`.

#### Decision 2: Keep `MarketingAction.SoftDelete(string userId, string username)` as-is (2 args, internal `DateTime.UtcNow`)

**Options considered:**
- **A.** Leave signature unchanged (per spec FR-3).
- **B.** Refactor to `SoftDelete(userId, username, utcNow)` for consistency with `UpdateDetails` and the ctor.

**Chosen approach:** **A**, matching FR-3 strictly.

**Rationale:** FR-3 is explicit that the domain method signature does not change. The 3-arg call shown in the spec's "API / Interface Design" example is a spec defect (see Specification Amendments). The Journal precedent also uses the 2-arg form. Refactoring `SoftDelete` to accept `utcNow` is a worthwhile consistency cleanup, but is out of scope for this task and should be filed separately if pursued.

#### Decision 3: Wrap `UpdateAsync` + `SaveChangesAsync` in the existing `try/catch` that returns `ErrorCodes.DatabaseError`

**Options considered:**
- **A.** Wrap both calls.
- **B.** Wrap only `SaveChangesAsync` (since `BaseRepository.UpdateAsync` returns `Task.CompletedTask` and never throws today).

**Chosen approach:** **A** — wrap both.

**Rationale:** Preserves the existing handler's behaviour and the existing "already deleted" log message verified by `Handle_ReturnsDatabaseError_WhenDbDeleteFails`. Keeps the change surgical and tolerant of future repository overrides that could make `UpdateAsync` non-trivial.

#### Decision 4: Call `UpdateAsync` even though the entity is already EF-tracked

**Rationale:** Both `UpdateMarketingActionHandler` (line 115) and `DeleteJournalEntryHandler` (line 52) call `UpdateAsync` on already-tracked entities. Skipping it would break the established pattern with no behavioural benefit. `BaseRepository.UpdateAsync` is `DbSet.Update(entity)` — harmless on a tracked entity.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits to:

- `backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs` — remove line 11 (`DeleteSoftAsync` declaration).
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs` — remove lines 27–36 (`DeleteSoftAsync` implementation).
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — replace lines 75–86 with the inlined sequence (see below).
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — update all `DeleteSoftAsync` setups/verifications.
- `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` — update lines 286, 305, 330 (see Spec Amendments).

### Interfaces and Contracts

**Removed from `IMarketingActionRepository`:**
```csharp
Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);
```

**Handler delete block** — replace the existing try/catch around `DeleteSoftAsync`:
```csharp
action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User");

try
{
    await _repository.UpdateAsync(action, cancellationToken);
    await _repository.SaveChangesAsync(cancellationToken);
}
catch (Exception ex)
{
    _logger.LogError(ex,
        "DB soft-delete failed after Outlook delete for MarketingAction {ActionId}; Outlook event {EventId} already deleted — DB row still present",
        request.Id, action.OutlookEventId);
    return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError);
}
```

Everything else in the handler (auth check, `GetByIdAsync`, not-found branch, Outlook block, success log, response shape) stays byte-for-byte unchanged.

### Data Flow

**Before** (per delete request): `GetById` → Outlook.Delete → `DeleteSoftAsync` { `GetById` → mutate → `Update` → `Save` } = **2 reads, 1 write**.

**After**: `GetById` → Outlook.Delete → mutate (in memory) → `Update` → `Save` = **1 read, 1 write**.

The entity is already EF-tracked from the first `GetByIdAsync`, so the in-memory `SoftDelete` mutation is captured by the change tracker; `UpdateAsync` is a no-op state marker; `SaveChangesAsync` produces a single `UPDATE`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Tests in `MarketingActionHandlerSyncTests.cs` still mock `DeleteSoftAsync` and will break compilation. | Medium | Spec FR-4 must cover this file too — see Spec Amendments. Update all three call sites (lines 286, 305, 330). |
| Spec example uses 3-arg `SoftDelete(...)` but domain method is 2-arg → implementer may add a third parameter speculatively. | Medium | Treat the 3-arg form as a spec typo; do not modify `MarketingAction.SoftDelete`. Match FR-3 verbatim. |
| Existing test `Handle_ReturnsDatabaseError_WhenDbDeleteFails` mocks `DeleteSoftAsync.ThrowsAsync`. | Low | Re-target the throw to `SaveChangesAsync` (Moq on `IMarketingActionRepository.SaveChangesAsync`). The verified log message ("already deleted") is preserved by the handler. |
| Tests verify call ordering between Outlook and the DB step (`callOrder.Add("db")` via `DeleteSoftAsync` callback). | Low | Re-anchor the "db" callback on `SaveChangesAsync` (the commit) — that's the operation that "closes" the DB step from a caller perspective. |
| Removing `DeleteSoftAsync` could leak callers outside the Marketing module. | Low | `grep -r "DeleteSoftAsync"` (already done) confirms no callers outside `MarketingActionRepository` itself, `IMarketingActionRepository`, the handler, and the two test files. Solution must compile cleanly. |
| `BaseRepository.UpdateAsync` returns immediately without saving — easy to mistake for a no-op and remove it. | Low | Keep the call for pattern consistency with `UpdateMarketingActionHandler` and `DeleteJournalEntryHandler`. A code comment is not warranted. |

## Specification Amendments

The following corrections to `spec.r1.md` are required before implementation:

1. **§ API / Interface Design — handler change snippet.** Change the example from `action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User", now);` to `action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User");` The current `MarketingAction.SoftDelete` is a 2-arg method that captures `DateTime.UtcNow` internally (`MarketingAction.cs:129`). FR-3 explicitly says signature stays unchanged — the example contradicts FR-3.

2. **§ FR-2 acceptance criteria — `SoftDelete` call.** Same fix: drop the `now` argument from the prescribed call.

3. **§ FR-2 — drop the "load `now`" requirement.** No need for the handler to capture `DateTime.UtcNow`. The domain method owns the timestamp.

4. **§ FR-4 — broaden test scope.** Add `backend/test/Anela.Heblo.Tests/Features/Marketing/MarketingActionHandlerSyncTests.cs` to the list of test files that must be updated. The `DeleteHandler_CallsDeleteEvent_WhenOutlookEventIdExists` and `DeleteHandler_ReturnsError_WhenOutlookThrowsNonNotFound` tests both mock/verify `DeleteSoftAsync` (lines 286, 305, 330).

5. **§ NFR-1 wording.** The "50% fewer reads" is correct for the delete path's read count (2 → 1), but the path still issues 1 write. Suggest rewording to: "Each delete request issues one entity load instead of two; total DB round-trips reduce from 3 to 2 (one read + one commit, plus the Outlook call which is unrelated)."

## Prerequisites

None. All collaborators (`ICurrentUserService`, repository, `_outlookSync`, options, logger) are already injected into `DeleteMarketingActionHandler`. No migrations, no config, no infrastructure changes. The Journal module's `DeleteJournalEntryHandler` already proves the pattern compiles and runs in this codebase.