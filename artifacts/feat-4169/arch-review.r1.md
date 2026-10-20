# Architecture Review: SmartsuppAgentCache API-failure fallback test coverage

## Skip Design: true

Backend-only, test-file-only change. No new or changed UI components, screens, layouts, or visual design decisions are involved.

## Architectural Fit Assessment

This is a pure test-addition task against an existing, unchanged production class (`SmartsuppAgentCache`). It aligns cleanly with established repo conventions:

- Colocated unit tests already exist for every other Smartsupp collaborator under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/` (24 files: `SmartsuppNameHelperTests.cs`, `SmartsuppContactEnricherTests.cs`, `SmartsuppApiClientTests.cs`, etc.), but `SmartsuppAgentCache` itself has none — confirmed by `find`, no `SmartsuppAgentCacheTests.cs` exists anywhere in the tree.
- `docs/architecture/testing-strategy.md` mandates xUnit + Moq + **FluentAssertions** for backend unit tests. The spec (`spec.r1.md`) under-specifies this: it allows plain xUnit `Assert` as if that were the house style. It is not — every single existing test file under `Features/Smartsupp/` (`SmartsuppContactEnricherTests.cs` and 23 others) imports and uses `FluentAssertions`, none use bare `Xunit.Assert`. **This is a spec amendment** (see below): tests must use FluentAssertions (`result.Should().BeEmpty()`, etc.), not `Assert.Empty(...)`.
- The class is registered as `services.AddSingleton<ISmartsuppAgentCache, SmartsuppAgentCache>()` in `SmartsuppModule.cs`, using `IServiceScopeFactory` specifically because `ISmartsuppApiClient` is scoped and the cache is a singleton. This scope-factory indirection is not a novel pattern in this codebase — it is already tested elsewhere (`RunDqtHandlerTests.cs`, `TierBasedHydrationOrchestratorTests.cs`, `ProductEnrichmentCacheTests.cs`, `BackgroundRefreshSchedulerServiceTests.cs`, `HebloFeatureProviderTests.cs`, `DownloadResilienceServiceTests.cs` all mock `IServiceScopeFactory` the same way). The spec's proposed construction approach (mock `IServiceScopeFactory` -> mock `IServiceScope` -> mock `IServiceProvider` -> resolve `ISmartsuppApiClient`) is correct and matches the established idiom exactly — verified directly against `RunDqtHandlerTests.cs` lines 16, 34-40.
- No production code, DI wiring, or module boundaries are touched. No migrations, config, or infrastructure prerequisites.

## Proposed Architecture

### Component Overview

```
SmartsuppAgentCacheTests (new, test-only)
        |
        | constructs directly (no DI container)
        v
SmartsuppAgentCache (unchanged, System Under Test)
        |
        | GetCacheAsync() -> _scopeFactory.CreateScope()
        v
Mock<IServiceScopeFactory> --CreateScope()--> Mock<IServiceScope>
                                                     |
                                                     | .ServiceProvider
                                                     v
                                              Mock<IServiceProvider>
                                                     |
                                                     | .GetService(typeof(ISmartsuppApiClient))
                                                     v
                                              Mock<ISmartsuppApiClient>
                                                     |
                                                     | .GetAgentsAsync(...)  <-- SetupSequence: success, then Throws
                                                     v
                                        List<SmartsuppAgentData> | throws Exception
```

No new components. The test class is a leaf consumer of the existing public `ISmartsuppAgentCache` contract plus direct construction of the concrete `SmartsuppAgentCache` type (test-only use of the concrete class is unavoidable and consistent with sibling cache tests such as `MaterialCostCacheTests.cs`, which also constructs the concrete cache type directly rather than going through DI).

### Key Design Decisions

#### Decision 1: Assertion library — FluentAssertions, not bare xUnit Assert
**Options considered:** (a) plain `Xunit.Assert.*` as drafted in `spec.r1.md`'s "Dependencies" section; (b) FluentAssertions, matching every existing file in `Features/Smartsupp/`.
**Chosen approach:** FluentAssertions (`result.Should().NotBeNull()`, `.Should().BeEmpty()`, `.Should().BeEquivalentTo(expected)`, `.Should().NotThrowAsync()` via `Func<Task>` where relevant).
**Rationale:** `docs/architecture/testing-strategy.md` specifies FluentAssertions as the house assertion library, and 100% of existing Smartsupp test files use it. Mixing bare `Assert` into this one new file would be an unjustified style deviation in an otherwise uniform folder. This is a required spec amendment.

#### Decision 2: Logger test double — `NullLogger<SmartsuppAgentCache>.Instance`, not `Mock<ILogger<T>>`
**Options considered:** (a) `Mock<ILogger<SmartsuppAgentCache>>` as spec.r1.md lists as the primary option; (b) `NullLogger<SmartsuppAgentCache>.Instance` from `Microsoft.Extensions.Logging.Abstractions`.
**Chosen approach:** `NullLogger<SmartsuppAgentCache>.Instance`.
**Rationale:** The spec already lists this as "an acceptable simpler alternative" — promote it to the primary/required choice. The tests in this task do not assert on log content (out of scope per spec), and `SmartsuppContactEnricherTests.cs` and others in the same folder already use `NullLogger` for exactly this kind of pass-through logger dependency, avoiding needless `Mock<ILogger<T>>.Setup` boilerplate that adds no verification value here.

#### Decision 3: Controlling success-then-failure sequencing on `GetAgentsAsync`
**Options considered:** (a) Moq's `SetupSequence(...).ReturnsAsync(...).ThrowsAsync(...)`; (b) a manual call-counter closure inside `.Returns(...)`.
**Chosen approach:** Moq `SetupSequence`, e.g.:
```csharp
apiClientMock.SetupSequence(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(agents)
    .ThrowsAsync(new HttpRequestException("boom"));
```
For the "throws on every call after the first" scenario needed by FR-3, chain additional `.ThrowsAsync(...)` entries (Moq repeats the last configured behavior once the sequence is exhausted, so a single trailing `.ThrowsAsync(...)` after `.ReturnsAsync(...)` already covers "fails on all subsequent calls").
**Rationale:** `SetupSequence` is the idiomatic Moq mechanism for this exact "succeed once, then fail" shape and keeps the test declarative; a manual counter is unnecessary complexity the codebase does not otherwise use for this pattern.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:

- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs`

No other files are created or modified. This matches the flat, one-class-per-collaborator layout already used throughout `Features/Smartsupp/`.

### Interfaces and Contracts

No new or changed interfaces. Tests exercise only the existing public contract:

```csharp
public interface ISmartsuppAgentCache
{
    Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default);
}
```

constructed via the concrete `SmartsuppAgentCache(IServiceScopeFactory, ILogger<SmartsuppAgentCache>)` constructor, with `ISmartsuppApiClient` resolved through the mocked scope chain as shown in the Component Overview.

Required test-helper shape (mirrors `RunDqtHandlerTests.cs`'s established pattern, adapted to a single steady scope since `SmartsuppAgentCache` calls `CreateScope()` fresh on every refresh attempt, not just once):

```csharp
private static (Mock<IServiceScopeFactory> factory, Mock<ISmartsuppApiClient> apiClient) BuildScopeFactory()
{
    var apiClientMock = new Mock<ISmartsuppApiClient>();
    var providerMock = new Mock<IServiceProvider>();
    providerMock.Setup(p => p.GetService(typeof(ISmartsuppApiClient))).Returns(apiClientMock.Object);

    var scopeMock = new Mock<IServiceScope>();
    scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);

    var factoryMock = new Mock<IServiceScopeFactory>();
    factoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

    return (factoryMock, apiClientMock);
}
```

Note: `GetRequiredService<T>()` (an extension method) internally calls `IServiceProvider.GetService(typeof(T))`, so mocking `GetService` as above is sufficient and is the same technique `RunDqtHandlerTests.cs` uses.

### Data Flow

1. **FR-4 (baseline happy path):** `apiClientMock` returns a list with a mix of named and null-`Name` agents on the first (and only) call -> assert the returned dictionary contains only the named agents, keyed by `Id`.
2. **FR-1 (cold-cache failure -> empty dict):** `apiClientMock.Setup(...).ThrowsAsync(...)` on the very first call, cache never populated -> `GetAgentNamesAsync()` awaited without throwing -> result `.Should().NotBeNull()` and `.Should().BeEmpty()`.
3. **FR-2 (warm-cache failure -> stale dict):** `SetupSequence` returns a populated list on call 1, throws on call 2. First `GetAgentNamesAsync()` call populates `_cache`. A second call must trigger a second `GetAgentsAsync()` invocation for the failure branch to be exercised — since the TTL is 1 hour and cannot be faked without a clock seam (confirmed: no `TimeProvider`/injectable clock exists in `SmartsuppAgentCache`; out of scope to add one per `spec.r1.md`), see Decision below on how the second refresh attempt is triggered in-test.
4. **FR-3 (repeated failures after warm success -> stays stale):** extend the same sequence with `.ThrowsAsync(...)` a second/third time and call `GetAgentNamesAsync()` repeatedly, asserting the dictionary contents never change and never become empty.

**Architectural clarification / spec amendment needed for FR-2/FR-3 test mechanics:** `spec.r1.md`'s FR-3 acceptance criteria says to achieve "repeated successful-then-failing-refresh cycles" via "repeated calls to `GetAgentNamesAsync`" against a mock that "throws on all calls after the first" — but as written, `SmartsuppAgentCache`'s fast path (`if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl) return _cache;`, line 38) means that **after a successful first call, every subsequent call within the 1-hour TTL returns from the fast path and never calls `GetAgentsAsync` again at all** — the API mock's second setup would never be exercised, and FR-2/FR-3 would not actually be testing the catch-block stale-fallback path as intended. Two viable options, in order of preference:

- **Preferred:** Reflection-based test seam — use reflection (`typeof(SmartsuppAgentCache).GetField("_cachedAt", BindingFlags.NonPublic | BindingFlags.Instance)`) to force `_cachedAt` back to `DateTime.MinValue` (or any value older than `TimeSpan.FromHours(1)`) between the "warm" call and the "failing" call, without touching production code. This is a test-only technique, does not modify `SmartsuppAgentCache.cs`, and directly and deterministically exercises real TTL-expiry-then-failure, satisfying the issue's literal "TTL expires, API throws -> returns stale dict" scenario rather than approximating it.
- **Fallback (if reflection is judged too invasive/fragile by the developer):** Construct a *second* `SmartsuppAgentCache` instance whose mocked `GetAgentsAsync` throws on its very first call, and manually seed its private `_cache`/`_cachedAt` via the same reflection technique to simulate "already warm, now expired" — functionally equivalent, marginally more setup.

Both approaches require `System.Reflection`, which is already used elsewhere in this test project for exactly this kind of private-field seeding (grep the test project for `BindingFlags.NonPublic` to confirm existing precedent before implementing; if no precedent exists, reflection is still the correct minimal-footprint choice over modifying production code, and should be documented with a one-line comment explaining why).

**This supersedes spec.r1.md's FR-3 acceptance criteria**, which implied the fast-path short-circuit could be bypassed simply by calling `GetAgentNamesAsync()` twice — it cannot, given the actual TTL check at line 38. The planner must include this reflection-based `_cachedAt` reset step explicitly as a task step for FR-2 and FR-3.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Fast-path TTL short-circuit (line 38) silently prevents FR-2/FR-3 from ever re-invoking `GetAgentsAsync`, producing tests that pass without exercising the stale-fallback catch branch at all (false confidence) | High | Use the reflection-based `_cachedAt` reset technique (see Data Flow above) between the warm call and the failing call; add an explicit assertion that `GetAgentsAsync` was actually invoked twice via `apiClientMock.Verify(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2))` so the test fails loudly if the fast path is hit instead |
| Reflection-based private-field manipulation is brittle if `SmartsuppAgentCache`'s field names ever change | Low | Field names (`_cache`, `_cachedAt`) are internal implementation details of a `sealed` class with no subclassing concerns; a rename would fail the reflection lookup at test-run time with a clear `NullReferenceException`/`ArgumentNullException`, not a silent false-pass — acceptable tradeoff given no clock-seam alternative exists without touching production code (explicitly out of scope) |
| Mismatched assertion library (bare `Assert` instead of `FluentAssertions`) creating a one-off style inconsistency in the `Features/Smartsupp/` folder | Medium | Enforced explicitly in Decision 1 above; planner/developer must use FluentAssertions throughout |
| Over-testing concurrency/thread-safety of the `SemaphoreSlim` lock (issue mentions "concurrent API failure") beyond what's practical/valuable | Low | Explicitly scoped out in `spec.r1.md`'s Out of Scope section, consistent with this repo's existing cache test suites (none of which do multi-threaded stress testing); confirmed correct, no amendment needed |

## Specification Amendments

1. **Dependencies section:** Replace "xUnit `Assert`" guidance with a hard requirement to use **FluentAssertions** for all assertions in the new test file, matching every existing file in `Features/Smartsupp/`. Bare `Xunit.Assert` must not be used.
2. **Interface / Test Structure section:** Promote `NullLogger<SmartsuppAgentCache>.Instance` (`Microsoft.Extensions.Logging.Abstractions`) from "acceptable alternative" to the required approach for the logger dependency, since no test in this task asserts on log output.
3. **FR-3 acceptance criteria:** The spec's assumption that repeated `GetAgentNamesAsync()` calls alone will re-trigger `GetAgentsAsync` after a successful call is incorrect — the TTL fast-path (line 38 of `SmartsuppAgentCache.cs`) prevents this. FR-2 and FR-3 tests must reset the private `_cachedAt` field via reflection (documented technique above) between the warm-success call and the subsequent failing call(s) to force the code past the fast-path check and into the lock/catch block. Add an explicit `Times.Exactly(N)` verification on `GetAgentsAsync` invocation count to every warm-cache-failure test so a regression that silently short-circuits via the fast path fails the test instead of passing vacuously.
4. **FR-3's "fast-path is exercised at least once" sub-requirement:** confirmed correct and unchanged — at least one test (can be part of FR-4's happy-path test, extended with a second immediate call) should assert `GetAgentsAsync` is called exactly once even across two `GetAgentNamesAsync()` calls in quick succession, proving the cache-hit fast path itself is covered too (this directly helps close the remaining coverage gap on line 38).

## Prerequisites

None. No migrations, config, feature flags, or infrastructure changes are required. The task can start immediately against the current `main`/feature-branch state of `SmartsuppAgentCache.cs` and the existing test project (`Anela.Heblo.Tests`), which already references xUnit, Moq, and FluentAssertions as project dependencies (confirmed by their presence in every other `Features/Smartsupp/` test file, implying the `.csproj` already carries these package references — no `.csproj` edit is anticipated, but the developer should confirm the `Anela.Heblo.Tests.csproj` package references before writing the file, in case any are missing).
