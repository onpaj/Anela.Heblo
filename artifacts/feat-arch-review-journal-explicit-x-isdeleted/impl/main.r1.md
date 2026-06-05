Final review: ✅ APPROVED. The reviewer's TotalCount concern is non-issue since the test class creates a fresh in-memory DB per test instance (Guid.NewGuid() in constructor). Writing the output summary now.

---

# Implementation: Remove Redundant Soft-Delete Predicates in JournalRepository

## What was implemented

Removed 5 duplicated `!x.IsDeleted` predicates from `JournalRepository.cs`. The `JournalEntry` entity already has a global EF Core query filter `HasQueryFilter(x => !x.IsDeleted)` at `JournalEntryConfiguration.cs:53` which EF Core automatically appends to every query (including join sources). The explicit predicates were producing duplicated SQL and creating a maintenance trap for future `IgnoreQueryFilters()` usage. Added 5 targeted regression tests first to lock the behavior contract, then removed each predicate one at a time — each step committed separately with all tests passing.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — 5 surgical predicate removals: `&& !x.IsDeleted` from `GetByIdAsync`, `.Where(x => !x.IsDeleted)` from `GetEntriesAsync` and `SearchEntriesAsync`, `!x.IsDeleted &&` from `GetEntriesByProductAsync`, and `.Where(je => !je.IsDeleted)` from the join source in `GetJournalIndicatorsAsync`
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — 5 new `[Fact]` regression tests (one per affected method), each inserting a soft-deleted entry and asserting it is excluded

## Tests

`backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — 13 total tests pass (7 pre-existing + 5 new):

- `GetByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull`
- `GetEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`
- `SearchEntriesAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`
- `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`
- `GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`

Full solution test suite: 4,378 passed; 38 pre-existing Docker-related integration failures unrelated to this change.

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-journal-explicit-x-isdeleted
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" -v normal
git log --oneline main..HEAD
git diff main..HEAD -- backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
```

Expected: 6 commits, diff shows exactly 5 removed predicate fragments, all 13 Journal tests pass.

## Notes

- Test isolation is safe: `JournalRepositoryIntegrationTests` creates a fresh in-memory database per test instance via `$"JournalTestDb_{Guid.NewGuid()}"` in the constructor — `TotalCount.Should().Be(1)` assertions are not fragile.
- The final reviewer identified a nuance in the `MarketingActionRepository.cs` parallel finding: lines 24 and 124 are genuine redundant predicates (same pattern as fixed here), but line 49 is a different issue — an `IncludeDeleted` flag that is silently non-functional because the global filter overrides it. The PR description should distinguish these two: lines 24/124 as redundant predicate candidates, line 49 as a separate `IncludeDeleted` defect.
- No schema changes, no migration, no API changes, no frontend impact.

## PR Summary

Behavior-preserving refactor. The `HasQueryFilter(x => !x.IsDeleted)` registered at `JournalEntryConfiguration.cs:53` already enforces soft-delete exclusion on every query targeting `DbSet<JournalEntry>`, including join sources. The five `.Where(x => !x.IsDeleted)` / `&& !x.IsDeleted` fragments removed here produced duplicated SQL and obscured the real enforcement point.

Soft-delete enforcement on `JournalEntry` lives in `JournalEntryConfiguration`. To query soft-deleted rows in the future (e.g., admin/audit views), add a new repository method that calls `IgnoreQueryFilters()` — follow the pattern at `MarketingActionRepository.cs:140`. Do not re-add per-method `!IsDeleted` predicates.

The same redundant-predicate pattern exists in `MarketingActionRepository.cs` at lines 24 and 124 (identical anti-pattern). Line 49 in the same file is a distinct issue: an `IncludeDeleted` flag that is silently non-functional because the global filter overrides it regardless. Both are out of scope for this PR; track separately.

### Changes
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — removed 5 redundant `!IsDeleted` predicate fragments
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added 5 regression tests locking soft-delete exclusion per affected method

## Status
DONE