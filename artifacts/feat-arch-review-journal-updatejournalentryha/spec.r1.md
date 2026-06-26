# Specification: Encapsulate JournalEntry update in domain method

## Summary
Refactor `JournalEntry` update logic so the entity owns its own audit-trail bookkeeping and input normalisation, mirroring the existing `SoftDelete` pattern. The `UpdateJournalEntryHandler` will be reduced to orchestration only (load, authorize, call domain method, persist).

## Background
The `JournalEntry` aggregate already encapsulates audit-trail bookkeeping for deletion via `SoftDelete(userId, username)` at `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:153`. However, the `UpdateJournalEntryHandler` at `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:51-59` directly mutates `Title`, `Content`, `EntryDate`, `ModifiedAt`, `ModifiedByUserId`, and `ModifiedByUsername` from the application layer. It also performs input normalisation (`Trim()`, `.Date`) outside the entity.

This split has two concrete consequences:
1. Audit-trail rules are duplicated across layers — the entity owns them for deletion, the handler owns them for updates.
2. Future invariants (e.g. "only the original author may edit", "content may not be blank", "EntryDate cannot be in the future") would land in the handler rather than the entity, drifting further from the Clean Architecture / DDD pattern this codebase already establishes for the same aggregate.

The fix is small, low-risk, and aligns the update path with the deletion path.

## Functional Requirements

### FR-1: Add `Update` domain method to `JournalEntry`
Introduce a new instance method on `JournalEntry` that performs all field changes and audit-trail bookkeeping atomically.

**Signature:**
```csharp
public void Update(string? title, string content, DateTime entryDate, string userId, string username)
```

**Behaviour:**
- Assign `Title` from the trimmed value of `title` (null-preserving: if `title` is null, `Title` becomes null).
- Assign `Content` from the trimmed value of `content`.
- Assign `EntryDate` from `entryDate.Date` (time component stripped).
- Set `ModifiedAt` to `DateTime.UtcNow`.
- Set `ModifiedByUserId` to `userId`.
- Set `ModifiedByUsername` to `username`.
- Do **not** mutate creation-audit fields (`CreatedAt`, `CreatedByUserId`, `CreatedByUsername`) or deletion fields (`IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`).
- Style and placement should mirror `SoftDelete` in the same file.

**Acceptance criteria:**
- Method exists on `JournalEntry` with the signature above.
- After calling `entry.Update(...)`, all six listed fields reflect the new values and the three audit fields are updated.
- `CreatedAt`, `CreatedByUserId`, `CreatedByUsername`, and all deletion fields are unchanged by `Update`.
- A unit test covers the happy path (all fields set, audit trail recorded).
- A unit test covers `title == null` (resulting `Title` is null, no `NullReferenceException`).
- A unit test covers trimming for both `title` (with surrounding whitespace) and `content`.
- A unit test covers `entryDate` time-stripping (passing `2026-06-04 14:30:00` results in `2026-06-04 00:00:00`).

### FR-2: Replace direct field assignments in `UpdateJournalEntryHandler`
Replace the block at `UpdateJournalEntryHandler.cs:51-59` (the seven-line sequence beginning with `var now = DateTime.UtcNow;` through `entry.ModifiedByUsername = ...`) with a single call to `entry.Update(request.Title, request.Content, request.EntryDate, currentUser.Id, currentUser.Name ?? "Unknown User")`.

**Acceptance criteria:**
- The handler no longer reads or writes `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`, `Title`, `Content`, or `EntryDate` directly.
- The handler still performs all surrounding work it did before: loading the entity by id, authorization / not-found checks, persisting via the repository / unit-of-work, returning the response DTO.
- The `"Unknown User"` fallback for a missing `currentUser.Name` is preserved (kept in the handler, since it is application-layer policy about how identity is presented).
- The local `var now = DateTime.UtcNow;` is removed if no longer needed; if other code in the handler still uses it, it is retained.
- All existing handler-level tests continue to pass without modification beyond test-double / mock setup adjustments required by the call-site change.

### FR-3: Preserve observable behaviour
The refactor must be behaviour-preserving from the API consumer's perspective.

**Acceptance criteria:**
- Request DTO, response DTO, HTTP route, status codes, and validation error shapes are unchanged.
- Persisted column values for an update are byte-for-byte equivalent to the pre-refactor implementation given the same input (modulo `DateTime.UtcNow` drift).
- No new public API surface is added beyond the `JournalEntry.Update` method.
- No database migration is required.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The refactor moves six assignments and two `Trim()` calls from one layer to another. No new allocations, queries, or I/O.

### NFR-2: Security
No change to auth, input validation, or data exposure. The handler retains responsibility for authorization and for resolving the current user; the entity only records what it is told.

### NFR-3: Maintainability
After the refactor, any future update-time invariant or normalisation rule must be added to `JournalEntry.Update` rather than to the handler. This is the principal goal of the change.

### NFR-4: Test coverage
Per project standards (`~/.claude/rules/testing.md`), maintain ≥80% coverage on touched files. Both `JournalEntry` and `UpdateJournalEntryHandler` must retain or improve their existing coverage after the change.

## Data Model
No schema changes. Existing `JournalEntry` columns continue to be used:
- `Title` (nullable string)
- `Content` (string)
- `EntryDate` (date)
- `ModifiedAt` (UTC timestamp)
- `ModifiedByUserId` (string)
- `ModifiedByUsername` (string)

## API / Interface Design

### Domain (new)
```csharp
// backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs
public void Update(string? title, string content, DateTime entryDate, string userId, string username)
{
    Title = title?.Trim();
    Content = content.Trim();
    EntryDate = entryDate.Date;
    ModifiedAt = DateTime.UtcNow;
    ModifiedByUserId = userId;
    ModifiedByUsername = username;
}
```
Placed adjacent to `SoftDelete` for discoverability.

### Application (changed call site)
```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs
entry.Update(
    request.Title,
    request.Content,
    request.EntryDate,
    currentUser.Id,
    currentUser.Name ?? "Unknown User");
```

### HTTP API
Unchanged.

## Dependencies
- `Anela.Heblo.Domain.Features.Journal.JournalEntry` (modified)
- `Anela.Heblo.Application.Features.Journal.UseCases.UpdateJournalEntry.UpdateJournalEntryHandler` (modified)
- Existing unit tests for both classes (added to / kept green)
- No new packages, services, or configuration

## Out of Scope
- Adding new invariants (e.g. "only original author may edit", "content non-empty", "EntryDate not in future"). The refactor enables these but does not introduce them.
- Refactoring other handlers in the Journal module (e.g. `CreateJournalEntryHandler`) to a similar pattern. Out of scope unless the architecture review separately flags them.
- Changing input validation (currently performed by FluentValidation / request DTO validators).
- Renaming or repurposing the existing `SoftDelete` method.
- Database schema changes or migrations.
- Changes to the OpenAPI-generated client.

## Open Questions
None.

## Status: COMPLETE