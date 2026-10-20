# Code Review: add-characterization-tests

## Summary
The implementation adds exactly the two characterization tests and the helper method specified in the task context, verbatim, in the correct location. Build and the targeted test run both confirm the tests pass against the current pre-refactor `ApplySorting` implementation, which is the task's acceptance criterion.

## Review Result: PASS

### task: add-characterization-tests
**Status:** PASS

## Docs to Update
(none — this is a test-only change with no public behavior, CLI, or documented concept affected)

## Overall Notes
- `MarginLevel`'s constructor parameter order (`percentage, amount, costTotal, costLevel`) and `MarginData.M0`'s `init` accessor were verified against the actual domain types; the task-context snippet's usage matches.
- `dotnet build`: 0 errors, only pre-existing warnings.
- `dotnet test --filter "FullyQualifiedName~GetProductMarginsHandlerTests"`: 7/7 passed (5 pre-existing + 2 new), confirming the tests characterize existing behavior correctly ahead of the refactor task.
