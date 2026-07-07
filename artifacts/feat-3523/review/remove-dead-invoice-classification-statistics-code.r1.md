# Code Review: Remove orphaned InvoiceClassification statistics dead code

## Summary
The commit removes exactly the dead code specified: the `GetStatisticsAsync` repository method, the two domain types, the two DTOs, the two AutoMapper `CreateMap` registrations, and the unused frontend query-key entry. Independent verification of `git show --stat`, the full diff, and a source grep confirms scope was respected and nothing else was touched.

## Review Result: PASS

### task: remove-dead-invoice-classification-statistics-code
**Status:** PASS

Verification performed directly in the worktree (commit `4081622`):
- `git show --stat HEAD` confirms exactly 7 files changed (4 deletions, 3 edits, 104 deletions total), matching the required file list precisely.
- `grep -rn "GetStatisticsAsync|ClassificationStatistics|RuleUsageStatistic|ClassificationStatisticsDto|RuleUsageStatisticDto" backend/src backend/test frontend/src` produced zero matches in source (only unrelated binary matches in `bin/` build artifacts, which are not source files and pre-date this change).
- `ClassificationHistoryRepository.cs` diff shows only the `GetStatisticsAsync` method body removed (the exact block described in the spec); `AddAsync`/`GetHistoryAsync`/`GetHistoryByInvoiceIdAsync`/`GetPagedHistoryAsync` are untouched. The trailing "no newline at end of file" note is pre-existing (confirmed via `git show HEAD~1`), not introduced by this change.
- `InvoiceClassificationMappingProfile.cs` diff shows only the two `CreateMap` lines removed; all other mappings intact.
- `useInvoiceClassification.ts` diff shows only the `statistics` entry removed from `CLASSIFICATION_QUERY_KEYS`.
- `IClassificationHistoryRepository.cs` has no diff in this commit, confirming it was correctly left untouched per the "must not touch" list.
- Reported build/test/lint results (dotnet build 0 errors, dotnet format no diffs, full test suite passing with only pre-existing Testcontainers failures, npm build/lint clean) are consistent with the diff scope and were not independently re-run, but nothing in the diff would be expected to break compilation or tests given the target was confirmed orphaned/unreferenced.

## Docs to Update
None.

## Overall Notes
Clean, surgical removal exactly matching the spec. No scope creep, no unrelated touches, no regressions introduced.
