# Specification: Remove embedded SaveChangesAsync from `IMarketingActionRepository.DeleteSoftAsync`

## Summary
Refactor the soft-delete path for `MarketingAction` so the repository no longer commits internally and no longer re-loads an entity the handler already has in memory. Align the delete flow with the existing caller-controlled `SaveChangesAsync` pattern used by Create and Update handlers in the Marketing module.

## Background
The Marketing module's `IMarketingActionRepository.DeleteSoftAsync` violates the project's caller-controlled persistence convention by calling `SaveChangesAsync` inside the repository method. This creates two concrete problems:

1. **Hidden side effect.** A unit-of-work-style caller cannot batch a soft-delete with other operations because the repository forces a mid-sequence commit. The method signature gives no hint of this behaviour.
2. **Double database load.** `DeleteMarketingActionHandler` loads the entity at line 47 (to read `OutlookEventId`) and then calls `DeleteSoftAsync`, which loads it again at line 29 via `GetByIdAsync`. Every delete pays for two round trips.

Three other handlers in the same module (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`, and the existing read pattern) follow the "handler owns the save" convention. The soft-delete is the only outlier, making it a maintenance trap and a discoverability hazard.

## Functional Requirements

### FR-1: Remove `DeleteSoftAsync` from `IMarketingActionRepository`
Delete the `DeleteSoftAsync` method from both the interface (`IMarketingActionRepository`) and its implementation (`MarketingActionRepository`). The soft-delete lifecycle becomes the handler's responsibility, eliminating the second DB load and the embedded save.

**Acceptance criteria:**
- `IMarketingActionRepository.DeleteSoftAsync` no longer exists.
- `MarketingActionRepository.DeleteSoftAsync` no longer exists.
- The solution compiles after removal (no orphaned callers).
- No new abstraction is introduced to replace it.

### FR-2: `DeleteMarketingActionHandler` owns the soft-delete sequence
The handler performs the soft-delete inline using the entity it already loaded for the Outlook event lookup. Order of operations is preserved: load entity → delete Outlook event (if any) → soft-delete entity → update → save.

**Acceptance criteria:**
- The handler loads the entity exactly **once** per delete request.
- After the existing Outlook delete step, the handler calls `action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User", now)`.
- The handler then calls `_repository.UpdateAsync(action, cancellationToken)` followed by `_repository.SaveChangesAsync(cancellationToken)`.
- All existing pre-conditions, authorization checks, error handling, response shape, and logging in `DeleteMarketingActionHandler` are preserved unchanged.
- If the entity is not found, the handler returns its existing "not found" response (no behavioural change).

### FR-3: Preserve existing soft-delete semantics on the domain entity
The `MarketingAction.SoftDelete(userId, username, utcNow)` domain method continues to set the audit fields and `IsDeleted` flag exactly as it does today. No domain-level behaviour is changed.

**Acceptance criteria:**
- `MarketingAction.SoftDelete` signature and behaviour are unchanged.
- The handler passes the same `userId`, `username`, and `utcNow` values that the repository method currently passes.

### FR-4: Test coverage parity
All existing tests for the delete flow continue to pass. Tests that mocked or verified `DeleteSoftAsync` are updated to verify the new sequence (`UpdateAsync` + `SaveChangesAsync`) on the repository mock.

**Acceptance criteria:**
- Existing `DeleteMarketingActionHandler` unit tests are updated and pass.
- A test asserts the entity is loaded exactly once per delete (regression guard against the double-load).
- Repository-level tests for `DeleteSoftAsync` are removed (the method no longer exists).
- `dotnet build` succeeds; `dotnet format` reports clean; `dotnet test` for the Marketing module passes.

## Non-Functional Requirements

### NFR-1: Performance
Each delete request issues one entity load instead of two. No new queries are introduced. The change should produce a measurable reduction in DB round trips on the delete path (50% fewer reads for this operation).

### NFR-2: Consistency
The soft-delete path matches the established Marketing module pattern: handlers compose repository mutations and explicitly call `SaveChangesAsync`. This eliminates the discoverability problem and removes the maintenance trap.

### NFR-3: Backwards compatibility
- **API surface:** The HTTP/MediatR contract for `DeleteMarketingAction` is unchanged (same request, same response, same status codes).
- **Database state:** Soft-deleted rows look identical to before (same fields populated with the same values).
- **Repository interface:** `DeleteSoftAsync` is removed; no callers outside `DeleteMarketingActionHandler` exist (verify during implementation).

### NFR-4: Security / Auditing
The audit trail (`DeletedBy`, `DeletedByName`, `DeletedAt` or whatever fields `SoftDelete` populates) remains populated with the same values from `ICurrentUser` and the current UTC timestamp. No change to authorization checks in the handler.

## Data Model
No schema changes. `MarketingAction` retains its existing soft-delete fields and `SoftDelete` domain method.

## API / Interface Design

**Removed:**
```csharp
// IMarketingActionRepository
Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);
```

**Handler change (conceptual):**
```csharp
// DeleteMarketingActionHandler — after existing Outlook delete step
action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User", now);
await _repository.UpdateAsync(action, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);
```

No new endpoints, no new MediatR requests, no UI changes.

## Dependencies
- `MarketingAction.SoftDelete` — existing domain method, unchanged.
- `IMarketingActionRepository.GetByIdAsync` / `UpdateAsync` / `SaveChangesAsync` — already used elsewhere; no changes.
- `ICurrentUser` (or equivalent) — already injected into the handler.
- `DateTime` provider currently used by the handler — already in place.

## Out of Scope
- Refactoring Create/Update handlers (already follow the correct pattern).
- Introducing a generic Unit-of-Work abstraction.
- Hard-delete functionality.
- Changes to other repositories or modules — this fix is scoped to `IMarketingActionRepository` and `DeleteMarketingActionHandler` only.
- Changes to `MarketingAction.SoftDelete` domain logic.
- Performance work beyond eliminating the redundant load.

## Open Questions
None.

## Status: COMPLETE