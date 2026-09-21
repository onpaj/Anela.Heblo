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
