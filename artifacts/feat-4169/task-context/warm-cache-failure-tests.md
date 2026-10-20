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
