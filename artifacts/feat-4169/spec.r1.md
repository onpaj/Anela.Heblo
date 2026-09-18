# Specification: SmartsuppAgentCache API-failure fallback test coverage

## Summary
`SmartsuppAgentCache` (`backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppAgentCache.cs`) implements a double-checked-locking, TTL-based cache over `ISmartsuppApiClient.GetAgentsAsync`. Its exception-handling fallback logic — return stale cached data if present, otherwise an empty dictionary — is currently untested (20.6% line coverage vs. a 60% threshold). This specification defines the unit test coverage required to lock in that fallback behavior and prevent a silent regression (e.g. an inverted null check) that would surface as spurious "agent not found" errors across all Smartsupp agent-name lookups.

## Background
`SmartsuppAgentCache` is a singleton cache (registered via DI, using `IServiceScopeFactory` to resolve the scoped `ISmartsuppApiClient` per refresh) that exposes agent id -> agent name mappings to callers via `GetAgentNamesAsync`. On each call it checks whether the in-memory cache is populated and still within its 1-hour TTL; if not, it acquires a `SemaphoreSlim` lock, re-checks (double-checked locking), and calls the Smartsupp API to refresh. If the API call throws, the `catch` block silently swallows the exception (logging a warning) and returns:
- the existing `_cache` value, if one exists from a prior successful call (**stale-data fallback**), or
- a fresh empty dictionary, if `_cache` has never been successfully populated (**empty-fallback**).

This is a coverage-gap remediation task, not a new-feature or behavior-change task. The production code in `SmartsuppAgentCache.cs` is not expected to change. The deliverable is a new xUnit test file exercising the untested branches, written against the existing implementation and existing `ISmartsuppApiClient` / `ISmartsuppAgentCache` contracts.

## Functional Requirements

### FR-1: Cold-cache API failure returns empty dictionary
When `GetAgentNamesAsync` is called and the cache has never been successfully populated (`_cache` is `null`), and the underlying `ISmartsuppApiClient.GetAgentsAsync` call throws, the method must return an empty (but non-null) `IReadOnlyDictionary<string, string>`, and must not throw.

**Acceptance criteria:**
- A test constructs `SmartsuppAgentCache` with a mocked `ISmartsuppApiClient` whose `GetAgentsAsync` throws (e.g. `HttpRequestException` or any `Exception`) on its first invocation.
- Calling `GetAgentNamesAsync()` does not throw; the awaited task completes successfully.
- The returned dictionary is non-null and has `Count == 0`.

### FR-2: Warm-cache API failure returns stale cached data
When the cache has been successfully populated at least once and a subsequent refresh attempt's API call throws (whether because the TTL expired or the cache was otherwise invalidated/re-triggered), `GetAgentNamesAsync` must return the previously cached dictionary contents unchanged, and must not throw or clear the cache.

**Acceptance criteria:**
- A test sequences the mocked `ISmartsuppApiClient.GetAgentsAsync` to succeed on the first call (returning a known, non-empty set of `SmartsuppAgentData`) and throw on the second call.
- After the first `GetAgentNamesAsync()` call, the returned dictionary matches the first successful API response (agent id -> agent name, excluding any agents with a null `Name`, matching the production `Where(a => a.Name is not null)` filter).
- Triggering a second refresh whose API call fails (see FR-3 for how TTL expiry is simulated) causes the second `GetAgentNamesAsync()` call to return the same stale dictionary contents as the first call, not an empty dictionary and not a thrown exception.

### FR-3: TTL expiry plus concurrent-with-refresh API failure does not clear or corrupt the stale cache
The cache's `_cachedAt` timestamp is set only on a *successful* refresh. Because the production class does not expose a seam to inject `DateTime.UtcNow` or `CacheTtl`, TTL expiry cannot be directly simulated by mocking time. Instead, this requirement is verified through the two black-box behaviors implied by the implementation:
1. Within the 1-hour TTL, `GetAgentNamesAsync` must not call `ISmartsuppApiClient.GetAgentsAsync` a second time (fast-path / cache-hit behavior) — verifying the double-checked-locking short-circuit is exercised at least once by tests in this suite, so the fallback tests are read in the context of a class whose happy path is also covered.
2. When a refresh is forced to occur again (by using a cache instance/mock arrangement where the first call is a successful populate and every subsequent call to `GetAgentsAsync` throws), repeated calls to `GetAgentNamesAsync` continue to return the stale dictionary from the last successful call — i.e., a failed refresh never overwrites `_cache` with an empty value and never advances `_cachedAt`.

**Acceptance criteria:**
- A test that performs at least two successful-then-failing-refresh cycles (via repeated `GetAgentNamesAsync` calls against a mock whose `GetAgentsAsync` throws on all calls after the first) asserts the dictionary returned is identical (by value) each time, and equals the original successful payload.
- If achieving a genuine time-based TTL expiry in-process is impractical without a production-code seam (the class does not accept an injectable clock), the test suite documents this constraint via a code comment and instead asserts the equivalent behavior through direct repeated invocation against a mock that always fails after the first success — this satisfies "TTL expires, API throws -> returns stale dict" from the issue's suggested approach without requiring a production-code change, which is out of scope (see Out of Scope).

### FR-4: Existing happy-path behavior is not broken by new tests
While not itself a coverage gap called out in the issue, a minimal happy-path test (successful `GetAgentsAsync` call returning `Name is null` and non-null entries) should exist or be reused to establish the baseline the stale-fallback test (FR-2/FR-3) builds on, confirming the `Where(a => a.Name is not null)` filter is correctly reflected in the cached dictionary before a failure is introduced.

**Acceptance criteria:**
- At least one test verifies that agents with a `null` `Name` are excluded from the dictionary returned after a successful `GetAgentsAsync` call.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a unit-test-only change with no production code or runtime performance impact. Tests must run fast (no real `Task.Delay`/`Thread.Sleep` to simulate TTL; no real HTTP calls) and must not introduce flakiness (no reliance on wall-clock timing races).

### NFR-2: Security
N/A — no auth, no sensitive data. Mocked `ISmartsuppApiClient` and `ISmartsuppAgentData` test doubles only; no real Smartsupp credentials or endpoints are involved.

## Data Model
No data model changes. Relevant existing types (unchanged):
- `ISmartsuppAgentCache.GetAgentNamesAsync(CancellationToken) : Task<IReadOnlyDictionary<string, string>>`
- `ISmartsuppApiClient.GetAgentsAsync(CancellationToken) : Task<IReadOnlyList<SmartsuppAgentData>>`
- `SmartsuppAgentData { string Id; string? Name; string? Email; }`

## API / Interface Design
No API surface changes. Test-only additions:
- New test file: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` (matches the existing convention of colocating Smartsupp unit tests under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/`, alongside `SmartsuppNameHelperTests.cs`, `SmartsuppContactEnricherTests.cs`, etc.)
- Test class constructs `SmartsuppAgentCache` directly with:
  - a mocked `IServiceScopeFactory` (via Moq) whose `CreateScope()` returns a mocked `IServiceScope` wrapping a mocked `IServiceProvider` that resolves `ISmartsuppApiClient` to a mocked instance (mirroring the pattern the production constructor requires, since the class resolves `ISmartsuppApiClient` through a fresh scope per call rather than taking it directly).
  - a mocked `ILogger<SmartsuppAgentCache>` (Moq, `NullLogger<SmartsuppAgentCache>.Instance` from `Microsoft.Extensions.Logging.Abstractions` is an acceptable simpler alternative to a full mock, since the tests do not need to assert log content per the issue's suggested approach).
- Uses xUnit `[Fact]` tests and Moq's `Mock<T>` / `SetupSequence` (or manual call-count-based `Returns`/`Throws` setups) to control success-then-failure sequencing of `GetAgentsAsync`, consistent with existing test conventions observed in `MaterialCostCacheTests.cs` and other cache tests in the repo.

## Dependencies
- xUnit (existing test framework, already used throughout `backend/test/Anela.Heblo.Tests`)
- Moq (existing mocking library, already used throughout the test suite, e.g. `MaterialCostCacheTests.cs`)
- `Microsoft.Extensions.Logging.Abstractions` (`NullLogger<T>`) — already an existing transitive dependency of the test project
- No new NuGet packages required.

## Out of Scope
- Any change to `SmartsuppAgentCache.cs` production code (including adding a clock abstraction/seam for deterministic TTL-expiry testing). If achieving true TTL-expiry testing later requires such a seam, that is a separate follow-up, not part of this coverage-gap task.
- Testing the DI registration/wiring of `ISmartsuppAgentCache` in `SmartsuppModule.cs` or `IServiceScopeFactory` behavior itself.
- Concurrency/thread-safety stress testing of the `SemaphoreSlim`-based double-checked locking under true parallel load (the issue's "concurrent API failure" language is interpreted as sequential warm-then-fail scenarios per FR-2/FR-3, not multi-threaded race testing, since the existing test suite conventions in this repo do not include multi-threaded stress tests for other caches such as `MaterialCostCacheTests.cs` or `PhotobankTagsCacheTests.cs`).
- Raising the file's line coverage to exactly 60% as a hard numeric target; the goal is to cover the specific untested branches (empty-fallback, stale-fallback) called out in the issue. If coverage remains below 60% after these tests due to other untested lines (e.g. the fast-path cache-hit branch, if not otherwise exercised), FR-4 and the TTL fast-path assertion in FR-3 are included specifically to close that gap too.

## Open Questions
None.

## Status: COMPLETE
