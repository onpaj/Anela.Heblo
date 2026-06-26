All 5 tasks complete. Here is the output summary:

---

# Implementation: Encapsulate JournalEntry update in domain method

## What was implemented

Moved the `JournalEntry` field-update bookkeeping from `UpdateJournalEntryHandler` into a new `JournalEntry.Update(...)` domain method, mirroring the existing `SoftDelete` pattern. The handler is now reduced to orchestration: authenticate → load → call domain method → persist → return. Six direct property assignments and the local `var now` variable were removed from the handler.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — added `public void Update(string? title, string content, DateTime entryDate, string userId, string username)` adjacent to `SoftDelete`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` — replaced 7-line direct-mutation block with single `entry.Update(...)` call
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs` — added 6 domain unit tests in a new `// ----- Update -----` region
- `backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs` — new file, 5 handler-level tests covering all handler paths

## Tests

- `JournalEntryTests.cs` — 6 new tests: happy path, null title, trim, date-strip, deletion fields untouched, collections untouched
- `UpdateJournalEntryHandlerTests.cs` — 5 new tests: unauthenticated, empty userId, entry not found, valid request (trim/audit/collections), null Name → "Unknown User" fallback

All 66 Journal tests pass (11 new + 55 pre-existing).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"
```

Expected: `Passed: 66, Failed: 0`

## Notes

- `ReplaceProductAssociations` and `ReplaceTagAssignments` deliberately remain as separate handler calls per the arch review.
- Pre-existing follow-up (out of scope): `DeleteJournalEntryHandler` passes `currentUser.Name` (a `string?`) directly into `SoftDelete` without the `"Unknown User"` fallback used by the Update path. Two paths, two policies — flagged in the Task 4 commit body for a separate PR.
- No schema changes, no migration, no OpenAPI surface change, no TypeScript client regeneration needed.

## PR Summary

Encapsulates `JournalEntry` update logic into a domain method, mirroring the existing `SoftDelete` pattern. The entity now owns all field assignment, input normalisation (trim, date-strip), and modified-audit trail bookkeeping. `UpdateJournalEntryHandler` is reduced to pure orchestration.

The refactor is behaviour-preserving: request DTOs, response DTOs, HTTP routes, and persisted column values are unchanged. Eleven new tests were added (6 domain unit tests + 5 handler-level tests) to bring touched files to ≥80% coverage.

Pre-existing inconsistency flagged for follow-up: `DeleteJournalEntryHandler` passes a possibly-null `currentUser.Name` into `SoftDelete` without the `"Unknown User"` fallback that the Update path uses. Not addressed here — surgical scope.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs` — added `Update` domain method adjacent to `SoftDelete`
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs` — replaced 7-line direct-mutation block with single domain method call
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs` — added 6 domain unit tests
- `backend/test/Anela.Heblo.Tests/Features/Journal/UpdateJournalEntryHandlerTests.cs` — new file with 5 handler-level tests

## Status
DONE