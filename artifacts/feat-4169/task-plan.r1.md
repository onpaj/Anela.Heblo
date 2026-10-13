# SmartsuppAgentCache API-Failure Fallback Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit test coverage for `SmartsuppAgentCache`'s untested exception-handling fallback branches (empty-fallback on cold cache, stale-data fallback on warm cache) without changing any production code.

**Architecture:** One new xUnit test file, `SmartsuppAgentCacheTests.cs`, constructs the concrete `SmartsuppAgentCache` directly (no DI container) against Moq test doubles for `IServiceScopeFactory` -> `IServiceScope` -> `IServiceProvider` -> `ISmartsuppApiClient` (the established idiom in this codebase, e.g. `RunDqtHandlerTests.cs`), using `NullLogger<SmartsuppAgentCache>.Instance` for the logger dependency and FluentAssertions for all assertions, matching every other file under `Features/Smartsupp/`. Because the class's 1-hour TTL fast-path (`SmartsuppAgentCache.cs:38`) cannot be bypassed by simply calling `GetAgentNamesAsync()` twice, warm-cache-failure scenarios use a reflection-based test helper to reset the private `_cachedAt` field between calls, forcing the code back into the refresh/catch path — a test-only technique with existing precedent in this test project (`CatalogRepositoryTests.cs` uses the same `BindingFlags.NonPublic | BindingFlags.Instance` pattern on a different class).

**Tech Stack:** .NET 8, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0, `Microsoft.Extensions.Logging.Abstractions` (`NullLogger<T>`), `System.Reflection`.

**Note on TDD adaptation for this task:** This is a coverage-gap remediation task against an *existing, unchanged* production class (`SmartsuppAgentCache.cs` — see spec.r1.md's Out of Scope: no production code changes). There is no "write minimal implementation to turn red to green" step because the implementation already exists and is not expected to change. Each task below therefore replaces the usual "run to verify it fails" / "implement" / "run to verify it passes" cycle with: write the test against the existing implementation, run it, and confirm it passes on the first run. If any test in this plan does NOT pass against the current `SmartsuppAgentCache.cs`, stop and treat that as a signal the spec/plan's understanding of the implementation is wrong — do not modify `SmartsuppAgentCache.cs` to make it pass (that would be an undocumented, out-of-scope production change); re-read `SmartsuppAgentCache.cs` and fix the test instead.

---

## File Reference

Full current contents of the system under test (unchanged by this plan) for reference while writing tests — `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppAgentCache.cs`:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp;

public interface ISmartsuppAgentCache
{
    Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default);
}

public sealed class SmartsuppAgentCache : ISmartsuppAgentCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private sealed record CacheData(IReadOnlyDictionary<string, string> NamesByAgentId);

    // ISmartsuppApiClient is scoped; use IServiceScopeFactory so the singleton
    // cache can create a short-lived scope for each API call.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmartsuppAgentCache> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private CacheData? _cache;
    private DateTime _cachedAt = DateTime.MinValue;

    public SmartsuppAgentCache(IServiceScopeFactory scopeFactory, ILogger<SmartsuppAgentCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default) =>
        (await GetCacheAsync(cancellationToken)).NamesByAgentId;

    private async Task<CacheData> GetCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl)
            return _cache;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<ISmartsuppApiClient>();
            var agents = await apiClient.GetAgentsAsync(cancellationToken);

            _cache = new CacheData(
                NamesByAgentId: agents
                    .Where(a => a.Name is not null)
                    .ToDictionary(a => a.Id, a => a.Name!));
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Smartsupp agent names; falling back to empty map");
            return _cache ?? new CacheData(new Dictionary<string, string>());
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

Relevant unchanged contracts referenced by the tests — `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`:

```csharp
public interface ISmartsuppApiClient
{
    // ...other members not used by these tests...
    Task<IReadOnlyList<SmartsuppAgentData>> GetAgentsAsync(CancellationToken cancellationToken);
    // ...other members not used by these tests...
}

public class SmartsuppAgentData
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? Email { get; set; }
}
```

---

### task: scaffold-cache-tests

Creates the new test file with its shared test infrastructure (scope-factory builder, SUT factory, reflection helper) and the two tests that establish the cache's happy-path behavior: the null-`Name` filtering (FR-4) and the TTL fast-path / single-fetch-per-warm-window behavior (FR-3 sub-requirement). These two tests do not require the reflection seam and give the fastest possible feedback that the test-double wiring (`IServiceScopeFactory` -> `IServiceScope` -> `IServiceProvider` -> `ISmartsuppApiClient`) is correct before the more complex failure-path tests are added in later tasks.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs`

- [ ] **Step 1: Write the test file with shared helpers and the happy-path test**

Create `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` with the following content:

```csharp
using System.Reflection;
using Anela.Heblo.Application.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppAgentCacheTests
{
    /// <summary>
    /// Builds a mocked IServiceScopeFactory -> IServiceScope -> IServiceProvider chain that
    /// resolves ISmartsuppApiClient to a controllable mock, mirroring the scope-per-refresh
    /// pattern SmartsuppAgentCache uses in production (see RunDqtHandlerTests.cs for the same
    /// technique against a different class).
    /// </summary>
    private static (Mock<IServiceScopeFactory> Factory, Mock<ISmartsuppApiClient> ApiClient) BuildScopeFactory()
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

    private static SmartsuppAgentCache CreateSut(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, NullLogger<SmartsuppAgentCache>.Instance);

    /// <summary>
    /// SmartsuppAgentCache's 1-hour TTL fast-path (line 38: "_cache is not null &&
    /// DateTime.UtcNow - _cachedAt &lt; CacheTtl") means a successful call is never re-fetched
    /// by simply calling GetAgentNamesAsync() again. To deterministically force the code back
    /// into the refresh/catch path for the warm-cache-failure tests, this reflection helper
    /// resets the private _cachedAt field to a value older than the TTL. This is a test-only
    /// technique (no production code change) with existing precedent in this test project, e.g.
    /// CatalogRepositoryTests.cs's use of BindingFlags.NonPublic | BindingFlags.Instance.
    /// </summary>
    private static void ExpireCache(SmartsuppAgentCache instance)
    {
        var field = typeof(SmartsuppAgentCache).GetField("_cachedAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(instance, DateTime.MinValue);
    }

    [Fact]
    public async Task GetAgentNamesAsync_SuccessfulFetch_ExcludesAgentsWithNullName()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
            new() { Id = "agent-2", Name = null },
            new() { Id = "agent-3", Name = "Petr Svoboda" },
        };
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(agents);
        var sut = CreateSut(factory.Object);

        // Act
        var result = await sut.GetAgentNamesAsync();

        // Assert
        result.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["agent-1"] = "Jana Novakova",
            ["agent-3"] = "Petr Svoboda",
        });
    }

    [Fact]
    public async Task GetAgentNamesAsync_CalledTwiceWithinTtl_OnlyFetchesFromApiOnce()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
        };
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(agents);
        var sut = CreateSut(factory.Object);

        // Act
        var first = await sut.GetAgentNamesAsync();
        var second = await sut.GetAgentNamesAsync();

        // Assert
        first.Should().BeEquivalentTo(second);
        apiClient.Verify(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the two tests to verify they pass against the existing (unchanged) implementation**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests"
```
Expected: both tests pass (`Passed! - Failed: 0, Passed: 2, Skipped: 0`). Per the TDD adaptation note above, these are expected to pass on the first run since `SmartsuppAgentCache.cs` is unchanged — a failure here means the test itself is wrong, not that production code needs to change.

- [ ] **Step 3: Commit**

```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs
git commit -m "test: add SmartsuppAgentCache happy-path and TTL fast-path coverage"
```

---

### task: cold-cache-failure-test

Adds the FR-1 test: when the cache has never been successfully populated and `GetAgentsAsync` throws, `GetAgentNamesAsync` must return a non-null, empty dictionary without throwing.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` (append a new `[Fact]` method)

- [ ] **Step 1: Write the failing-API cold-cache test**

Add the following method to the `SmartsuppAgentCacheTests` class (e.g. directly below `GetAgentNamesAsync_CalledTwiceWithinTtl_OnlyFetchesFromApiOnce`):

```csharp
    [Fact]
    public async Task GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp API unavailable"));
        var sut = CreateSut(factory.Object);

        // Act
        // If GetAgentNamesAsync rethrew instead of falling back, this await would throw and
        // fail the test — so a passing test also proves FR-1's "must not throw" requirement.
        var result = await sut.GetAgentNamesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
```

Add `using System.Net.Http;` is not required — `HttpRequestException` lives in `System.Net.Http`, which is covered by .NET 8's implicit usings for `net8.0`-targeted projects with `ImplicitUsings` enabled (confirmed in `Anela.Heblo.Tests.csproj`); if the build fails with `CS0246: The type or namespace name 'HttpRequestException' could not be found`, add `using System.Net.Http;` to the top of the file.

- [ ] **Step 2: Run the test to verify it passes**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Run the full test file to confirm no regressions among the tests added so far**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

- [ ] **Step 4: Commit**

```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs
git commit -m "test: add SmartsuppAgentCache cold-cache API-failure coverage (FR-1)"
```

---

### task: warm-cache-failure-tests

Adds the FR-2 (single warm-then-fail) and FR-3 (repeated warm-then-fail) tests using the `ExpireCache` reflection helper from `scaffold-cache-tests` to force the TTL fast-path to be bypassed between calls, then runs the full backend test suite and checks coverage as the final validation of this plan.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs` (append two new `[Fact]` methods)

- [ ] **Step 1: Write the warm-cache-then-single-failure test (FR-2)**

Add the following method to the `SmartsuppAgentCacheTests` class:

```csharp
    [Fact]
    public async Task GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
            new() { Id = "agent-2", Name = null },
        };
        apiClient.SetupSequence(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents)
            .ThrowsAsync(new HttpRequestException("Smartsupp API unavailable"));
        var sut = CreateSut(factory.Object);

        // Act
        var first = await sut.GetAgentNamesAsync();
        ExpireCache(sut); // bypass the TTL fast-path so the next call re-enters the refresh/catch path
        var second = await sut.GetAgentNamesAsync();

        // Assert
        second.Should().BeEquivalentTo(first);
        second.Should().NotBeEmpty();
        apiClient.Verify(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
```

- [ ] **Step 2: Run the new test to verify it passes**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Write the repeated-warm-failure test (FR-3)**

Add the following method to the `SmartsuppAgentCacheTests` class:

```csharp
    [Fact]
    public async Task GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
        };
        apiClient.SetupSequence(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents)
            .ThrowsAsync(new HttpRequestException("Smartsupp API unavailable"))
            .ThrowsAsync(new HttpRequestException("Smartsupp API unavailable"));
        var sut = CreateSut(factory.Object);

        // Act
        var first = await sut.GetAgentNamesAsync();

        ExpireCache(sut);
        var second = await sut.GetAgentNamesAsync();

        ExpireCache(sut);
        var third = await sut.GetAgentNamesAsync();

        // Assert: every failed refresh keeps returning the one-and-only successful payload —
        // a failed refresh never clears _cache or advances _cachedAt.
        second.Should().BeEquivalentTo(first);
        third.Should().BeEquivalentTo(first);
        apiClient.Verify(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
```

- [ ] **Step 4: Run the new test to verify it passes**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests.GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary"
```
Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 5: Run the full `SmartsuppAgentCacheTests` class**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SmartsuppAgentCacheTests"
```
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (the 2 tests from `scaffold-cache-tests`, the 1 from `cold-cache-failure-test`, and the 2 added in this task).

- [ ] **Step 6: Run the full backend test suite to confirm no regressions elsewhere**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai/backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```
Expected: build succeeds, `dotnet format --verify-no-changes` reports no formatting violations (if it reports violations in `SmartsuppAgentCacheTests.cs`, run `dotnet format` without `--verify-no-changes` to auto-fix, then re-run `--verify-no-changes` to confirm), and the full suite passes with 0 failures (this is a test-only addition to an unchanged production class, so no other test should be affected).

- [ ] **Step 7: Check line coverage on the changed file (informational, matches the spec's motivating context)**

Run:
```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai/backend
dotnet test /p:CollectCoverage=true /p:CoverageReporter=html test/Anela.Heblo.Tests/
```
Expected: the coverage report (path printed by the `dotnet test` output, typically under `test/Anela.Heblo.Tests/coveragereport/` or `TestResults/`) shows `SmartsuppAgentCache.cs` at a materially higher line-coverage percentage than the 20.6% baseline cited in `spec.r1.md`. Per `spec.r1.md`'s Out of Scope section, hitting exactly 60% is not a hard requirement of this task — the two catch-branch paths (empty-fallback, stale-fallback) and the fast-path/happy-path lines must all show as covered; if any of those specific lines still show uncovered, that indicates a test in this plan did not actually exercise the branch it was intended to (re-check the corresponding `Times.Exactly(...)` / `Times.Once` verification for that test), not that additional tests are needed beyond this plan's scope.

- [ ] **Step 8: Commit**

```bash
cd /home/user/worktrees/feature-4169-Coverage-Gap-Smartsupp-Smartsuppagentcache-Api-Fai
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs
git commit -m "test: add SmartsuppAgentCache warm-cache and repeated API-failure coverage (FR-2, FR-3)"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (cold-cache failure -> empty dict, no throw) -> `cold-cache-failure-test` task, `GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing`.
- FR-2 (warm-cache failure -> stale dict) -> `warm-cache-failure-tests` task, `GetAgentNamesAsync_WarmCacheThenApiThrows_ReturnsStaleDictionary`.
- FR-3 (repeated failure after warm success stays stale, plus fast-path coverage) -> `warm-cache-failure-tests` task's `GetAgentNamesAsync_RepeatedFailuresAfterWarmSuccess_KeepsReturningOriginalStaleDictionary`, and `scaffold-cache-tests`' `GetAgentNamesAsync_CalledTwiceWithinTtl_OnlyFetchesFromApiOnce` for the "fast-path is exercised at least once" sub-requirement. Both use `Times.Exactly`/`Times.Once` verification per the arch-review's risk mitigation, so a regression that silently short-circuits into the fast path fails loudly instead of passing vacuously.
- FR-4 (null-`Name` filtering on happy path) -> `scaffold-cache-tests` task, `GetAgentNamesAsync_SuccessfulFetch_ExcludesAgentsWithNullName`.
- NFR-1 (no real delays/HTTP, no flakiness) -> all tests use Moq test doubles and the reflection-based `ExpireCache` seam instead of `Task.Delay`/real time; no wall-clock races.
- NFR-2 (no real credentials/HTTP) -> confirmed, only mocked `ISmartsuppApiClient`.
- Architecture review's three spec amendments (FluentAssertions required, `NullLogger<T>.Instance` required, reflection-based `_cachedAt` reset required with `Times.Exactly` verification) are all incorporated into every test above.

**2. Placeholder scan:** No `TBD`/`TODO`/"implement later" markers. Every step shows complete, runnable code. No step says "similar to Task N" without repeating the code in full.

**3. Type consistency:** `BuildScopeFactory()`, `CreateSut(...)`, and `ExpireCache(...)` are defined once in `scaffold-cache-tests` and reused with identical signatures in every later task's code blocks. The `SmartsuppAgentData`, `ISmartsuppApiClient.GetAgentsAsync(CancellationToken)`, and `ISmartsuppAgentCache.GetAgentNamesAsync(CancellationToken = default)` signatures used throughout match the actual current source (`ISmartsuppApiClient.cs`, `SmartsuppAgentCache.cs`) verified while writing this plan.
