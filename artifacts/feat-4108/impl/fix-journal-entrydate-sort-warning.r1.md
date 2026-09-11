# Implementation: fix-journal-entrydate-sort-warning

## What was implemented

`JournalRepository.ApplySort` had no `"entrydate"` arm in its `switch`
expression. Since `sortBy = "EntryDate"` is the default value sent by both
`GetEntries` and `SearchEntries` requests, every default-sorted journal
query fell through to `ApplyDefaultSortWithWarning`, which logs a spurious
`LogWarning("Unknown sort key {SortBy} requested on {Repository}", ...)`
on every call — even though the resulting ordering (by `EntryDate`) was
already correct.

Fixed by adding an explicit `"entrydate"` arm that delegates directly to
`ApplyDefaultSort` (no warning), placed before the `"title"` arm exactly as
specified in the task context. `ApplyDefaultSort` and
`ApplyDefaultSortWithWarning` were not touched.

TDD was followed: the new test was written first, run and observed to
fail for the expected reason, then the one-line production fix was added
and the test re-run to confirm it passes, followed by the full test file
to confirm no regressions.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` — added the `"entrydate" => ApplyDefaultSort(query, ascending),` arm to the `ApplySort` switch (2 lines added, nothing removed).
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added `GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate`, inserted immediately before `SearchEntriesAsync_SortsByCreatedByUsername_Ascending` (28 lines added, nothing removed/changed elsewhere).

No signature adaptation was needed — `CreateEntryWithAuthor(string, DateTime, string)` and `_repository.GetEntriesAsync(pageNumber, pageSize, sortBy:, sortDirection:)` in the real file matched the task-context snippet exactly, so the test method was inserted verbatim as specified.

## Tests

`backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`:
- New: `GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate` — seeds three entries with distinct `EntryDate` values, calls `GetEntriesAsync` with `sortBy: "EntryDate"`, asserts the result is still ordered `Alpha, Bravo, Charlie` by `EntryDate` ascending, and asserts `ILogger.LogWarning` was never invoked (`Times.Never`).
- Full file (31 tests) re-run to confirm no regressions, including `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty` (still warns for a genuinely unknown key like `"tags"`), the null/empty/whitespace "does not log" tests, the `createdByUsername` sort tests, and the `SortMatrix`-driven ordering theories.

### Observed test output

**RED (before the fix)** — filter `FullyQualifiedName~JournalRepositoryIntegrationTests.GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate`:

```
Failed Anela.Heblo.Tests.Features.Journal.JournalRepositoryIntegrationTests.GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate [9 s]
  Error Message:
   Moq.MockException :
Expected invocation on the mock should never have been performed, but was 1 times: x => x.Log<It.IsAnyType>(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>())

Performed invocations:
   Mock<ILogger<JournalRepository>:1> (x):
      ILogger.Log<FormattedLogValues>(LogLevel.Warning, 0, Unknown sort key EntryDate requested on JournalRepository, null, Func<FormattedLogValues, Exception, string>)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 9 s
```

This confirms the test failed exactly as predicted in the task context: the ordering assertion passed on its own, only the `Times.Never` warning assertion failed, proving the test correctly detects the missing `"entrydate"` switch arm.

**GREEN (after the fix)** — full `JournalRepositoryIntegrationTests` filter:

```
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    31, Skipped:     0, Total:    31, Duration: 4 s
```

All 31 tests in the file pass, including the new one; no test count decreased and none failed.

## How to verify

From the worktree root:

```
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test  backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
```

Expected: `Passed! - Failed: 0, Passed: 31, Skipped: 0, Total: 31`.

## Notes

- Build check: scoped to `Anela.Heblo.Tests.csproj` (which transitively builds `Anela.Heblo.Persistence`) rather than the whole solution, per the orchestrator's build/test rules. Both builds succeeded with 0 errors (pre-existing nullable-reference warnings only, none introduced by this change).
- `dotnet format` was run scoped to just the two touched files (`dotnet format Anela.Heblo.sln --include <2 files> --verify-no-changes`), bounded to 150s. It completed within budget with exit code 0 (no formatting issues) — not skipped.
- `artifacts/feat-4108/state.json` was already modified in the working tree before this task started (visible in `git status` at session start) and is unrelated to this fix; it was left unstaged/uncommitted per "surgical changes" — the orchestrator handles that file itself.
- No deviations from the task context's Step 3 diff — the switch arm was added verbatim.
- Self-review against the plan's criteria:
  - FR-1: explicit `"entrydate"` arm delegating to `ApplyDefaultSort`, no warning. ✅
  - AC: `sortBy = EntryDate` produces the same ordering as before. ✅ (verified by new test)
  - AC: `sortBy = EntryDate` does not call `ILogger.LogWarning`. ✅ (verified by new test)
  - AC: `title` / `createdByUsername` sorting unaffected. ✅ (existing tests for both still pass)
  - AC: unknown `sortBy` still logs a warning. ✅ (`GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty` still passes)
  - AC: null/empty/whitespace `sortBy` unaffected. ✅ (their tests still pass; they short-circuit before the switch)
  - Out of scope items (broader `ApplySort` refactor, `SortBy` default changes, new sort keys, `IssuedInvoiceRepository` audit) were not touched.

## PR Summary

Fixes a spurious "Unknown sort key" warning that `JournalRepository` logged on every default-sorted journal query, because `sortBy = "EntryDate"` (the default value both request types send) fell through the sort switch's default arm instead of being recognized explicitly.

### Changes
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` — added an explicit `"entrydate"` arm to the `ApplySort` switch that delegates to `ApplyDefaultSort` (no warning), instead of falling through to `ApplyDefaultSortWithWarning`.
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added `GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate`, asserting both the unchanged `EntryDate`-ascending ordering and that no warning is logged.

Verified with TDD: new test written first, confirmed to fail for the expected reason (Moq `Times.Never` violation on the logged warning), then the fix was applied and the full 31-test `JournalRepositoryIntegrationTests` suite passes with no regressions.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01ShBJBzrsondLpmHVcTrKUq

## Status
DONE
