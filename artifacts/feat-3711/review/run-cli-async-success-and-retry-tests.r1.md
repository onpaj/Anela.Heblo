# Code Review: run-cli-async-success-and-retry-tests

## Summary
The implementation matches the task spec essentially verbatim: it adds `FakeTokenRefresher` implementing `IPlaudTokenRefresher`, a new `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput` test that genuinely exercises the previously-uncovered success path (lines 89-90 of `PlaudCliClient.RunCliAsync`), and strengthens the two AUTH_FAILED-path tests with real, non-superficial assertions. I traced the exception-identity and invocation-count assertions through `PlaudTokenRefresher.RefreshAsync` and `RunCliAsync` to confirm they are correct and not false positives, and ran the test file directly — all 6 tests pass.

## Review Result: PASS

### task: run-cli-async-success-and-retry-tests
**Status:** PASS

## Overall Notes
Verification performed:

- **`FakeTokenRefresher`**: correctly implements `IPlaudTokenRefresher` (`RefreshAsync`/`SyncToKeyVaultAsync` with matching signatures), placed exactly where specified (between `FakeRefreshClient` and the `// ── Helpers ──` banner). `RefreshAsync` throws if invoked, `SyncToKeyVaultAsync` counts calls — appropriate for isolating the success path.

- **New success-path test**: shim exits 0 with a header-only line, so `RunCliCoreAsync` returns normally without raising `PlaudAuthExpiredException`. This drives execution through the previously-uncovered `await _tokenRefresher.SyncToKeyVaultAsync(ct); return output;` lines. Assertions (`SyncCallCount == 1`, `RefreshCallCount == 0`, empty result) correctly distinguish the success path from the retry path — this is a real, non-vacuous exercise of the target branch, not a copy of an existing AUTH_FAILED test.

- **Inner-exception identity assertion (FR-5)**: traced `PlaudTokenRefresher.RefreshAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefresher.cs` line 65) — `_refreshClient.RefreshAsync(...)` is called with no surrounding try/catch that would wrap or replace the exception, so the `HttpRequestException` instance captured in `expectedInner` propagates unchanged up to `RunCliAsync`'s `catch (Exception ex)` block, which wraps it via `throw new PlaudAuthExpiredException("token refresh failed", ex)`. `thrown.Which.InnerException.Should().BeSameAs(expectedInner)` is therefore a valid, meaningful reference-equality check, not a tautology — it would fail if the wrapping logic changed to construct a new/different exception.

- **Exactly-twice invocation count (FR-6)**: shim appends one line to `countFile` per invocation before failing with AUTH_FAILED every time. First invocation → caught, refresh succeeds (fake refresh client), retry invoked via the un-caught `return await RunCliCoreAsync(args, ct);` at line 108 of `PlaudCliClient.cs` → second invocation → AUTH_FAILED propagates uncaught (no retry loop). `invocationLines.Should().HaveCount(2)` correctly proves no runaway retry; it would catch a regression that removed the retry (1 invocation) or introduced a loop (>2 invocations). The counter file lives inside the per-test `dir`, cleaned up by the existing `finally` block — no flakiness risk (sequential process invocation, no timing dependency).

- **Untouched tests**: confirmed `RunCli_WhenAuthFails_RefreshesTokenAndRetries_ReturnsOutput`, `RunCli_WhenAuthFailsAndTokensFileMissing_ThrowsPlaudAuthExpiredException`, and `RunCli_WhenCliExitsNonZeroWithoutAuthFailed_ThrowsInvalidOperationException` are unchanged, and `CreateClient` was not modified, as required.

- **No production code changes**: confirmed via `git show 8d455a3` — only the test file and the pipeline's own `artifacts/feat-3711/state.json` bookkeeping file changed.

- **Test run verification**: ran `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --filter "FullyQualifiedName~PlaudCliClientRunTests"` directly — result: `Passed! Failed: 0, Passed: 6, Skipped: 0, Total: 6`, consistent with 5 original + 1 new test in this file, and consistent with the developer's claim of the full project suite passing (28 total across the whole test project). The developer's summary claims are plausible and independently confirmed.

No issues found. No documentation updates required for this task.
