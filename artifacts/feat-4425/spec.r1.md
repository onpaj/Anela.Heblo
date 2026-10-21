# Specification: JournalEntry clock injection for Update and SoftDelete

## Summary
`JournalEntry.Create` takes a `DateTime now` parameter, but `Update` and `SoftDelete` read `DateTime.UtcNow` directly. This change makes all three mutation methods take the timestamp from the caller, so the domain entity no longer depends on the system clock.

## Background
Arch-review finding (issue #4425). In `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`, `Update` sets `ModifiedAt = DateTime.UtcNow`, and `SoftDelete` sets `DeletedAt` and `ModifiedAt` from two separate `DateTime.UtcNow` calls. Tests can only assert a before/after window (see `JournalEntryTests.Update_AssignsAllFieldsAndAuditTrail` and `UpdateJournalEntryHandlerTests`).

## Functional Requirements

### FR-1: Update accepts an injected timestamp
`JournalEntry.Update(title, content, entryDate, userId, username, DateTime now)` sets `ModifiedAt = now`.
**Acceptance criteria:**
- The signature has a trailing `DateTime now` parameter.
- After `Update`, `ModifiedAt == now` exactly.
- No `DateTime.UtcNow` call remains in `Update`.

### FR-2: SoftDelete accepts an injected timestamp
`JournalEntry.SoftDelete(userId, username, DateTime now)` sets `DeletedAt = now` and `ModifiedAt = now`.
**Acceptance criteria:**
- The signature has a trailing `DateTime now` parameter.
- After `SoftDelete`, `DeletedAt == ModifiedAt == now`.
- No `DateTime.UtcNow` call remains in `SoftDelete`.

### FR-3: Handlers supply the timestamp
`UpdateJournalEntryHandler` and `DeleteJournalEntryHandler` capture `var now = DateTime.UtcNow;` once and pass it, as `CreateJournalEntryHandler` already does.
**Acceptance criteria:**
- Handler behaviour and response payloads are unchanged. `UpdateJournalEntryResponse.ModifiedAt` still comes from the entity.

### FR-4: Tests assert exact values
Existing entity and handler tests are updated for the new signatures. Entity tests assert exact `ModifiedAt`/`DeletedAt` equality with a fixed `now`.

## Non-Functional Requirements
### NFR-1: Performance
No impact.
### NFR-2: Security
No change.

## Data Model
No schema or migration change.

## API / Interface Design
No HTTP/OpenAPI change. Only the domain method signatures change (internal; callers are the two handlers and tests).

## Dependencies
None. Callers found: `UpdateJournalEntryHandler`, `DeleteJournalEntryHandler`, `JournalEntryTests.cs`, `SimpleJournalTests.cs` (line 80), `UpdateJournalEntryHandlerTests.cs`, and possibly `DeleteJournalEntryHandlerTests`.

## Out of Scope
- Introducing `TimeProvider` or `IClock` into the handlers.
- `CreateJournalTagHandler` use of `DateTime.UtcNow`.
- Other modules.

## Open Questions
None.

## Status: COMPLETE
