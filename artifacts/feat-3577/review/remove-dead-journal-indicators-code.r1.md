# Code Review: Remove dead journal indicators code

## Summary
The implementation matches the task spec exactly: `GetJournalIndicatorsAsync` (interface + implementation),
the `RecentEntriesDays` constant, `JournalIndicatorSnapshot`, `JournalIndicatorDto`, and the 4 dead
integration tests are all removed in a single commit (`f8330df`) touching exactly the 5 named files, with
no collateral changes elsewhere. Independent verification of the diff and a repo-wide grep confirm zero
residual references.

## Review Result: PASS

### task: remove-dead-journal-indicators-code
**Status:** PASS

Verification performed:
- `git show HEAD --stat` shows exactly 5 files changed, all deletions only (213 deletions, 0 insertions),
  matching the spec's file list precisely:
  - `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` (modified)
  - `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` (modified)
  - `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` (modified)
  - `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs` (deleted)
  - `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` (deleted)
- `IJournalRepository.cs` diff: removed exactly the `GetJournalIndicatorsAsync` signature block; the other
  three method signatures (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`) and the
  `using` statement are untouched. Read the resulting file — interface now declares 3 members, matching
  the "Definition of done."
- `JournalRepository.cs` diff: removed exactly the `private const int RecentEntriesDays = 30;` line and
  the `GetJournalIndicatorsAsync` method body (including trailing blank line), leaving
  `GetEntriesByProductAsync`'s closing brace directly followed by `ApplySort` as specified. No other
  method in the file was touched.
- `JournalIndicatorSnapshot.cs` and `JournalIndicatorDto.cs` confirmed absent from the working tree
  (`test -f` → not found for both).
- `JournalRepositoryIntegrationTests.cs` diff: both blocks removed cleanly and match the spec's text
  boundaries exactly — Block A (3 tests) removed between
  `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries` and the
  `// ---------- Sort matrix tests (FR-1 / FR-4) ----------` comment, with exactly one blank line
  preserved; Block B (1 test) removed between
  `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults` and
  `CreateEntryWithFamily`, with spacing preserved. No other test, helper, or fixture in the file was
  modified.
- Repo-wide grep `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/`
  returns zero matches (exit code 1 / no output) — independently re-run, not just trusted from the impl
  summary.
- `git status --porcelain` shows only `artifacts/feat-3577/state.json` as uncommitted (pipeline
  bookkeeping, unrelated to the code change) — no stray edits left in the working tree outside the commit.
- Journal-scoped test run reported in the impl summary (93 passed, 0 failed, 4 fewer than before) is
  consistent with removing exactly 4 `[Fact]` tests and no others.
- The unrelated full-solution `dotnet test` failures (66/72/13 in `Anela.Heblo.Tests`,
  `Anela.Heblo.Adapters.Flexi.Tests`, `Anela.Heblo.Adapters.Shoptet.Tests`) are documented sandbox
  limitations (missing Docker/Testcontainers, missing live Shoptet secrets) and, per task instructions,
  are treated as informational context rather than a correctness signal for this change.

All "Definition of done" criteria in the task spec are satisfied by direct inspection of the diff and
current file state.

## Docs to Update
None — this is a pure dead-code removal with no behavioral or contract change requiring documentation
updates.

## Overall Notes
Clean, surgical deletion. The commit message accurately describes the change and the developer's summary
matches the actual diff in every particular checked. No further action needed.

**Status:** PASS
