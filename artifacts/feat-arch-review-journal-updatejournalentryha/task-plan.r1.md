Plan saved to `docs/superpowers/plans/2026-06-04-encapsulate-journal-entry-update.md`.

**Summary of the 5-task plan:**

1. **Task 1** — Write failing domain tests for `JournalEntry.Update` (6 tests: happy path, null title, trim, date-strip, deletion-fields untouched, collections untouched). Verified RED via build error referencing the missing method.

2. **Task 2** — Implement `Update(...)` adjacent to `SoftDelete` in `JournalEntry.cs` (~line 153). Six assignments, no guard clauses, mirrors `SoftDelete` style.

3. **Task 3** — Write `UpdateJournalEntryHandlerTests.cs` (new file, 5 tests modelled on `DeleteJournalEntryHandlerTests`). Tests pass against the **pre-refactor** handler — locking current behaviour before changing it. Resolves arch-review Spec Amendment 2.

4. **Task 4** — Replace the 7-line block at `UpdateJournalEntryHandler.cs:51-59` with a single `entry.Update(...)` call. The `?? "Unknown User"` fallback stays in the handler. `ReplaceProductAssociations` / `ReplaceTagAssignments` calls remain untouched (Spec Amendment 1). Commit body flags the pre-existing Delete-handler nullability inconsistency as a separate follow-up (Spec Amendment 3).

5. **Task 5** — Final verification: full backend test suite, diff scope check (no API surface change → no OpenAPI client regeneration), confirm follow-up note in commit.

Each step has either exact code blocks or exact commands with expected output. No placeholders.