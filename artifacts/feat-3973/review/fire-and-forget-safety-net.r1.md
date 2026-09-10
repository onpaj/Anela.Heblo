# Code Review: fire-and-forget-safety-net

## Summary

The implementation correctly wraps the fire-and-forget `Task.Run` body in a try/catch safety net that captures exceptions and persists them as `Failed` status on the `DqtRun` entity instead of silently swallowing them. The test is properly structured to verify the new behavior by forcing a divergence between the pre-check scope (which finds a runner) and the background task scope (which has no runners), ensuring the exception handler path is exercised. All functional requirements are met.

## Review Result: PASS

### task: fire-and-forget-safety-net
**Status:** PASS

**Verification Details:**

1. **Handler Implementation** — Correctly placed try/catch wrapper around the runner lookup and `RunAsync` call (lines 71-86). Catch block:
   - Logs error with context (line 81)
   - Obtains scoped repository from the same service scope (line 82)
   - Re-fetches run by ID with null-safe access (line 83)
   - Calls `.Fail(ex.Message, utcNow)` to record failure (line 84)
   - Persists change via `SaveChangesAsync` (line 85)

2. **Test Implementation** — `Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun` (lines 222-285):
   - Asserts `response.Success == true` (line 280): handler returns success since run was persisted synchronously
   - Asserts `run != null` (line 281): run entity exists
   - Asserts `run.Status == DqtRunStatus.Failed` (line 282): status is failed due to catch block
   - Asserts `run.ErrorMessage.Contains("IssuedInvoiceComparison")` (line 283): exception message contains test type name
   - Asserts `SaveChangesAsync` called at least once (line 284): verifies persistence

3. **Mock Strategy** — Clever two-scope divergence:
   - First `CreateScope()` call returns normal scope with runner (pre-check passes)
   - Second `CreateScope()` call returns empty-runner scope (background task throws)
   - This models the race condition the safety net guards against

4. **Compile & Build** — Code compiles without errors (handled as part of `dotnet test` build). No compilation issues in the modified files.

## Docs to Update

None required for this change. The inline comment in the handler (lines 64-67) adequately explains the safety net's purpose.

## Overall Notes

The implementation is defensive in design—it does not change the synchronous behavior of `Handle()` but adds protection for the asynchronous background work. The pre-check is still the authoritative guard for whether a runner exists, but the catch block handles the edge case where state diverges between synchronous and asynchronous execution. The approach of re-fetching the run and calling `.Fail()` is consistent with existing domain patterns (as evidenced by the `.Fail()` method being available on `DqtRun`). 

The `.ReturnsAsync(1)` setup in the test (line 236) correctly matches the actual `SaveChangesAsync` return type (`Task<int>`), resolving the compile issue noted in the implementation summary.

**Status:** PASS
