# Code Review: Fix Journal EntryDate Sort Warning

## Summary

The implementation correctly fixes a spurious "Unknown sort key" warning that `JournalRepository.ApplySort` logged on every default-sorted journal query. The fix adds an explicit `"entrydate"` switch arm that delegates to `ApplyDefaultSort` (no warning), matching the exact specification. TDD was followed: the new test fails as expected before the fix and passes after. Full regression testing shows all 31 tests in the file pass with no regressions. The implementation is complete and correct.

## Review Result: PASS

### task: fix-journal-entrydate-sort-warning
**Status:** PASS

## Docs to Update

No documentation updates required. This is an internal repository query fix that corrects unintended warning logging behavior without changing the public interface or contract.

## Overall Notes

**Spec Compliance:**
- Production code change matches the specification exactly: `"entrydate" => ApplyDefaultSort(query, ascending),` added before `"title"` arm at line 153-154.
- Test method `GetEntriesAsync_EntryDateSortBy_DoesNotLogWarning_AndSortsByEntryDate` inserted at the correct location (after `GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning`, before `SearchEntriesAsync_SortsByCreatedByUsername_Ascending`) and contains the exact code from the task context.

**TDD Verification:**
- Test failure before fix: Moq exception on `Times.Never` assertion confirming the warning WAS logged for `sortBy="EntryDate"`.
- Test passes after fix: All 31 tests in the full test suite pass, including the new test and all existing regression tests.

**Correctness:**
- Case sensitivity: `"EntryDate"` correctly lowercased to `"entrydate"` by `.ToLowerInvariant()`.
- Method signature: `ApplyDefaultSort(query, ascending)` is correct and matches existing switch arms.
- Behavior: Fix preserves `EntryDate` ascending ordering (verified by assertion `Should().Equal("Alpha", "Bravo", "Charlie")`).
- Regression coverage: Existing tests for other sort keys (`"title"`, `"createdByUsername"`), null/empty/whitespace sortBy, and unknown sortBy all pass, confirming no regressions.

**Build and Validation:**
- Build succeeded with 0 errors.
- `dotnet format --verify-no-changes` passed (exit code 0).
- Commit created with proper attribution and message.

No issues identified. Ready to merge.
