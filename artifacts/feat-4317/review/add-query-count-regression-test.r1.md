# Code Review: add-query-count-regression-test

## Summary
The implementation adds exactly the `CountingRepositoryWrapper` class and regression test specified in the task context, in the specified locations. The test was run and fails against the current implementation with the expected assertion (`GetAllAsyncCallCount` expected 1, actual 0), proving the N+1 pattern exists today.

## Review Result: PASS

### task: add-query-count-regression-test
**Status:** PASS

## Docs to Update
(none — internal test-only change, no public behaviour or docs impact)

## Overall Notes
- `CountingRepositoryWrapper` correctly implements all four `IRecurringJobConfigurationRepository` members, delegating to the wrapped repository and counting `GetAllAsync`/`GetByJobNameAsync` calls — matches the interface signature in `IRecurringJobConfigurationRepository.cs`.
- The test uses `Assert.Equal` (xUnit), consistent with every other test already in `RecurringJobSeederTests.cs`, rather than the FluentAssertions style used in the cross-feature reference (`PackingMaterialsListQueryCountTests`) — appropriate, since matching the local file's existing convention takes priority over matching a different feature's file.
- Verified by running `dotnet test --filter "FullyQualifiedName~RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups"`: build succeeded (pre-existing nullable warnings only, unrelated to this change), test ran and failed exactly as expected (`Expected: 1, Actual: 0`).
- Task 2 (`batch-load-existing-configurations`) is expected to make this test pass by replacing the per-job `GetByJobNameAsync` loop with a single `GetAllAsync` call.

**Status:** PASS
