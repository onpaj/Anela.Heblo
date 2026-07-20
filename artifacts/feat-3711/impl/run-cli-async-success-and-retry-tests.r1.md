# Implementation: run-cli-async-success-and-retry-tests

## What was implemented
Closed the remaining coverage gap in `PlaudCliClient.RunCliAsync`'s plain success path by adding a new `FakeTokenRefresher` fake and a new test `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput`, and strengthened the assertions of the two existing AUTH_FAILED-path tests (`RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` and `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException`) to assert exact wrapping/no-retry-loop behavior instead of only the thrown exception's type. No production code was changed.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` — added `FakeTokenRefresher : IPlaudTokenRefresher` nested class (tracks `RefreshCallCount`/`SyncCallCount`, throws if `RefreshAsync` is ever called); added new test `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` exercising the plain success branch (`SyncToKeyVaultAsync` called once, `RefreshAsync` never called, output returned); strengthened `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` to assert `InnerException` is reference-equal to the exact exception thrown by the fake refresh client and that the outer message contains `"token refresh failed"`; strengthened `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` to have the shim script log one line per invocation to a counter file, asserting exactly 2 invocations (initial call + one retry, no runaway loop).

## Tests
- `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` (new) — covers `RunCliAsync` lines 89-90: on a first-try CLI success, `SyncToKeyVaultAsync` is invoked exactly once, `RefreshAsync` is never invoked, and the parsed (empty) output is returned.
- `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` (strengthened) — now also asserts the outer `PlaudAuthExpiredException.InnerException` is reference-equal to the exact `HttpRequestException` thrown by the fake refresh client, and that the message contains "token refresh failed".
- `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` (strengthened) — now also asserts the CLI shim process was invoked exactly twice (initial call + one retry), confirming no runaway retry loop.
- Other 3 existing tests in the file (`RunCli_WhenAuthFails_RefreshesTokenAndRetries_ReturnsOutput`, `RunCli_WhenAuthFailsAndTokensFileMissing_ThrowsPlaudAuthExpiredException`, `RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException`) were left untouched.

Test run: `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj` — **Passed! Failed: 0, Passed: 28, Skipped: 0, Total: 28** (run on Linux, so `[SkippableFact]` tests actually executed rather than being skipped).

`dotnet format test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --verify-no-changes` — exit code 0, no formatting changes needed.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
dotnet format test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --verify-no-changes
```
Confirm 28 tests pass with 0 failed/skipped, and that `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` appears in the results.

## Notes
- No repository-level `.sln` file exists under `backend/`, so `dotnet format --verify-no-changes` was run directly against the test project's `.csproj` instead (equivalent result).
- Implementation followed the task-context file verbatim; no deviations from the specified code.
- `artifacts/feat-3711/state.json` was also modified (by the pipeline's own bookkeeping, not by hand) between reading the task and committing — included in the commit since it's a tracked pipeline state file, not an unrelated code change.

## PR Summary
This change closes a test-coverage gap in `PlaudCliClient.RunCliAsync`'s plain success path, which previously had no test exercising a first-try CLI success — leaving the best-effort `SyncToKeyVaultAsync` call and the return-output line entirely uncovered. A new `FakeTokenRefresher` fake (implementing `IPlaudTokenRefresher` directly) lets a new test, `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput`, verify that on success the client syncs to Key Vault exactly once and never touches the refresh/retry branch. Additionally, the two existing AUTH_FAILED-path tests were strengthened: the "refresh throws" test now asserts the outer `PlaudAuthExpiredException`'s `InnerException` is the exact exception instance thrown during refresh (not just any exception of the right type) and that the wrapped message is preserved; the "retry also fails" test now asserts the underlying CLI shim process is invoked exactly twice (initial attempt + one retry), proving there's no runaway retry loop. All changes are additive/in-place edits to the test file only; no production code changed.

### Changes
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` — added `FakeTokenRefresher` fake, added `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` test, strengthened `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` (inner-exception identity + message assertion) and `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` (exactly-twice CLI invocation assertion).

## Status
DONE
