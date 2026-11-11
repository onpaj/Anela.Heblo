# Implementation: failure-path-tests r1

## What was implemented
Added four [Fact] tests to PollyExecutionStrategyTests.cs plus a shared AssertFailure helper:
Execute/ExecuteAsync _FailsAfterRetries_SetsDataRecordsFailureLogsAndRethrowsSameInstance (TimeoutException, 3 attempts) and
Execute/ExecuteAsync _NonRetryableFailureOnFirstAttempt_SetsAttempts1 (InvalidOperationException, 1 attempt).
Each asserts same-instance rethrow, Data["Anela.DbRetryAttempts"], exactly one db.retry.failure with exception.type and total_attempts tags, no db.retry.success, and one Error log with the exception.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs`

## Tests
Filtered run of PollyExecutionStrategyTests: 9 passed, 0 failed.

## How to verify
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PollyExecutionStrategyTests"

## Notes
None.

## PR Summary
Added failure-path unit tests for PollyExecutionStrategy (sync and async): retry exhaustion and non-retryable first-attempt failure.

### Changes
- `backend/test/Anela.Heblo.Tests/Persistence/Resilience/PollyExecutionStrategyTests.cs` -- four failure-path tests and an assertion helper

## Status
DONE
