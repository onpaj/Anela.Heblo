# Specification: Eliminate redundant database fetch in DeleteJournalEntryHandler

## Summary
The `DeleteJournalEntryHandler` performs two database round-trips per delete operation: one to verify the entry exists, and a second one performed silently inside `JournalRepository.DeleteSoftAsync` to refetch the same entity. Each fetch eagerly loads two relationship graphs (`ProductAssociations` and `TagAssignments.Tag`). This spec defines a refactor that collapses the operation to a single fetch while preserving externally observable behavior (response shapes, logging, authorization, error codes).

## Background
`DeleteJournalEntryHandler.Handle` (at `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/DeleteJournalEntry/DeleteJournalEntryHandler.cs:39`) fetches the `JournalEntry` to perform a not-found check and to return a structured error response. It then calls `IJournalRepository.DeleteSoftAsync(int id, ...)` (at `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:27`) which itself invokes `GetByIdAsync(id)` a second time. The repository's `GetByIdAsync` override (lines 18–25) `Include`s `ProductAssociations` and `TagAssignments.Tag`, producing a multi-join SQL statement for every call.

Consequences:
1. **Two DB round-trips with eager loading** for every delete.
2. **Silently swallowed not-found** inside `DeleteSoftAsync` — once the handler has guarded against it, the inner null check is dead code and obscures intent.
3. **Reasoning hazard:** the `entry` variable read by the handler is not the entity that actually gets mutated, so any caller-side decision based on that instance is meaningless to persistence.

This finding was filed by the daily architecture review routine on 2026-05-12 against module Journal.

## Functional Requirements

### FR-1: Single fetch per delete
The delete flow MUST execute exactly one `SELECT … FROM JournalEntries …` query against the database to load the entity prior to soft-delete (excluding any audit/save queries Entity Framework Core issues as part of `SaveChanges`). The current double-fetch pattern MUST be removed.

**Acceptance criteria:**
- A unit/integration test asserts the repository's `GetByIdAsync` is invoked exactly once per successful delete (e.g. `Mock<IJournalRepository>.Verify(r => r.GetByIdAsync(...), Times.Once)`).
- An integration test using an in-memory or test-container database confirms a single `SELECT` against `JournalEntries` is issued prior to the `UPDATE`.

### FR-2: Preserve externally observable handler behavior
The `DeleteJournalEntryHandler` MUST continue to:
- Return `ErrorCodes.UnauthorizedJournalAccess` with `resource = "journal_entry"` when the current user is unauthenticated or has no Id.
- Return `ErrorCodes.JournalEntryNotFound` with `entryId = request.Id.ToString()` when the entry does not exist (including when soft-deleted, since `GetByIdAsync` already filters `!IsDeleted`).
- On success, return `new DeleteJournalEntryResponse { Id = request.Id }` and log `"Journal entry {EntryId} deleted by user {UserId}"` at Information level.
- Apply soft-delete semantics via `JournalEntry.SoftDelete(userId, username)` on the domain entity, using `currentUser.Id` and `currentUser.Name`.

**Acceptance criteria:**
- All existing tests in `backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs` pass without changes to their expected response shapes, error codes, or message dictionaries.
- The success-path test continues to assert exactly one log entry containing the entry Id and user Id.

### FR-3: Repository surface refactor
The redundant `IJournalRepository.DeleteSoftAsync(int id, string userId, string username, CancellationToken)` method MUST be removed from both the interface (`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs:7`) and the implementation (`backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs:27`). The handler MUST instead:
1. Call `JournalEntry.SoftDelete(currentUser.Id, currentUser.Name)` on the already-fetched entity.
2. Persist the change via the inherited `BaseRepository<JournalEntry, int>` methods `UpdateAsync(entry, ct)` followed by `SaveChangesAsync(ct)` exposed through `IJournalRepository`/`IRepository<JournalEntry, int>`.

If the existing `IRepository<,>` abstraction does not expose `UpdateAsync` / `SaveChangesAsync` publicly, the implementation MUST verify they are accessible from `IJournalRepository` callers and add them to the interface contract if missing — without widening the contract beyond what is necessary for this use case.

**Acceptance criteria:**
- `grep -rn "DeleteSoftAsync" backend/src/Anela.Heblo.Domain/Features/Journal backend/src/Anela.Heblo.Persistence/Catalog/Journal backend/src/Anela.Heblo.Application/Features/Journal` returns no matches.
- The handler file shows a single `await _journalRepository.GetByIdAsync(...)` followed by `entry.SoftDelete(...)`, `UpdateAsync`, and `SaveChangesAsync` calls.
- The `Marketing` feature's identical `DeleteSoftAsync(int, ...)` method is **out of scope** for this change (see Out of Scope) and must remain untouched.

### FR-4: Test coverage updates
The existing test class `backend/test/Anela.Heblo.Tests/Features/Journal/DeleteJournalEntryHandlerTests.cs` MUST be updated to reflect the new repository interaction:
- Tests that previously verified `DeleteSoftAsync` was called/not called MUST now verify the new persistence path (e.g. `UpdateAsync` + `SaveChangesAsync` were called Once for success cases, Never for unauthorized/not-found cases).
- The mock setup must allow `entry.SoftDelete(...)` to mutate the entity in memory; the test must assert the entity passed to `UpdateAsync` has `IsDeleted == true` (or whatever flag `SoftDelete` toggles — see Open Questions).

**Acceptance criteria:**
- All tests in `DeleteJournalEntryHandlerTests.cs` pass.
- At least one positive test asserts the entity passed to `UpdateAsync` reflects the soft-delete state.
- Negative tests (`UnauthenticatedUser_ReturnsUnauthorized`, `NonexistentEntry_ReturnsNotFound`) verify `UpdateAsync` and `SaveChangesAsync` are called `Times.Never`.

### FR-5: Backward compatibility for callers
A repository-wide grep MUST confirm no production code outside the Journal feature references `IJournalRepository.DeleteSoftAsync`. The current call graph (per investigation) shows only `DeleteJournalEntryHandler` consumes this method.

**Acceptance criteria:**
- `grep -rn "_journalRepository.DeleteSoftAsync\|IJournalRepository.*DeleteSoftAsync" backend/src` returns no matches after the change.
- The solution builds (`dotnet build`) with no compilation errors.

## Non-Functional Requirements

### NFR-1: Performance
- The delete operation MUST issue at most one entity-loading `SELECT` (with its includes) per request, down from two.
- No new N+1 patterns are introduced; the existing include set (`ProductAssociations`, `TagAssignments.Tag`) is preserved for the single fetch since `JournalEntry.SoftDelete` may transitively need to mark related entities (to be confirmed — see Open Questions). If the domain `SoftDelete` does not touch the related collections, the includes should remain for now (scope discipline) and a separate follow-up may revisit them.
- No measurable regression in p99 latency for the delete endpoint.

### NFR-2: Security
- Authorization behavior is unchanged: unauthenticated requests are rejected with `ErrorCodes.UnauthorizedJournalAccess` before any DB access.
- No new attack surface: the handler still operates on `request.Id` only and does not expose internal entity state to the caller.
- Soft-delete audit trail (`userId`, `username`) MUST continue to be recorded on the entity through `JournalEntry.SoftDelete`.

### NFR-3: Maintainability
- The handler's local `entry` variable MUST be the same instance that is mutated and persisted, eliminating the current cognitive trap.
- Dead code (the silent null branch in `DeleteSoftAsync`) is removed, not relocated.
- The change is surgical: no incidental refactors of unrelated handlers, no formatting churn in untouched files.

### NFR-4: Testability
- Test doubles for `IJournalRepository` no longer need to mock `DeleteSoftAsync`. The mock surface shrinks to `GetByIdAsync`, `UpdateAsync`, `SaveChangesAsync`.

## Data Model
No schema or domain-model changes.

Affected types (read-only reference):
- `JournalEntry` (aggregate root, identity `int`) — already exposes `SoftDelete(string userId, string username)`.
- `JournalEntryProduct` — child of `JournalEntry`, loaded via `Include(ProductAssociations)`.
- `JournalEntryTagAssignment` + `JournalEntryTag` — loaded via `Include(TagAssignments).ThenInclude(Tag)`.

## API / Interface Design

### External API
No change. The MediatR request/response contract (`DeleteJournalEntryRequest`, `DeleteJournalEntryResponse`) and the HTTP endpoint that wraps it are untouched. Response payloads, status codes, and error codes remain identical.

### Internal interface change
`IJournalRepository` loses one method:

```csharp
// REMOVED
Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);
```

The handler depends instead on methods already exposed via `IRepository<JournalEntry, int>` (`UpdateAsync`, `SaveChangesAsync`) plus `GetByIdAsync`. If those base-interface methods are not currently surfaced through `IJournalRepository`, this spec accepts the minimal interface widening required.

### Reference implementation sketch

```csharp
public async Task<DeleteJournalEntryResponse> Handle(
    DeleteJournalEntryRequest request,
    CancellationToken cancellationToken)
{
    var currentUser = _currentUserService.GetCurrentUser();
    if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
    {
        return new DeleteJournalEntryResponse(ErrorCodes.UnauthorizedJournalAccess,
            new Dictionary<string, string> { { "resource", "journal_entry" } });
    }

    var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
    if (entry == null)
    {
        return new DeleteJournalEntryResponse(ErrorCodes.JournalEntryNotFound,
            new Dictionary<string, string> { { "entryId", request.Id.ToString() } });
    }

    entry.SoftDelete(currentUser.Id, currentUser.Name);
    await _journalRepository.UpdateAsync(entry, cancellationToken);
    await _journalRepository.SaveChangesAsync(cancellationToken);

    _logger.LogInformation(
        "Journal entry {EntryId} deleted by user {UserId}",
        request.Id, currentUser.Id);

    return new DeleteJournalEntryResponse { Id = request.Id };
}
```

## Dependencies
- **`JournalEntry.SoftDelete`** domain method — already exists per the repository's current implementation.
- **`BaseRepository<JournalEntry, int>`** — must expose (directly or via `IRepository<,>`) the `UpdateAsync` and `SaveChangesAsync` methods used by the handler.
- **`ICurrentUserService`** — unchanged.
- **No new NuGet packages, migrations, or configuration changes.**

## Out of Scope
- **Marketing module's parallel issue.** `MarketingActionRepository.DeleteSoftAsync` and `DeleteMarketingActionHandler` follow the same anti-pattern but are explicitly out of scope for this change to keep the diff small and reviewable. A separate ticket should be opened.
- **Reducing the include set in `GetByIdAsync`.** Whether `ProductAssociations` and `TagAssignments.Tag` are actually required for soft-delete is a follow-up question — not part of this refactor.
- **Hard-delete semantics**, ownership enforcement, audit-log persistence beyond `SoftDelete`, and the placeholder "for now, allow all authenticated users to delete" comment in the handler.
- **Adding an entity-accepting overload** of `DeleteSoftAsync` (the brief mentioned this as an alternative). This spec selects the handler-direct approach instead, because it eliminates the dead null check entirely and keeps the persistence concern in one place.
- **General `IRepository<,>` contract redesign.** Any interface tweak must be the minimum required to compile the new handler.

## Open Questions
None.

## Status: COMPLETE
