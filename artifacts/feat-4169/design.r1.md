# Design: SmartsuppAgentCache API-failure fallback test coverage

## Component Design

This is a test-only addition; no production components change. One new test component is introduced:

### `SmartsuppAgentCacheTests` (new)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAgentCacheTests.cs`
- **Responsibility:** Unit-test the fallback behavior of `SmartsuppAgentCache.GetAgentNamesAsync` when the underlying `ISmartsuppApiClient.GetAgentsAsync` call fails, covering both the cold-cache (empty-fallback) and warm-cache (stale-fallback) branches identified in `spec.r1.md`, plus the baseline happy path needed to establish a "warm" cache state.
- **Collaborators (test doubles only, via Moq):**
  - `Mock<IServiceScopeFactory>` — returns a `Mock<IServiceScope>` whose `.ServiceProvider` resolves `ISmartsuppApiClient` via `Mock<IServiceProvider>.Setup(p => p.GetService(typeof(ISmartsuppApiClient)))`.
  - `Mock<ISmartsuppApiClient>` — the only method under mock control is `GetAgentsAsync(CancellationToken)`, sequenced via Moq's `SetupSequence` to alternate between success and throw as each test scenario requires.
  - `NullLogger<SmartsuppAgentCache>.Instance` (from `Microsoft.Extensions.Logging.Abstractions`) — passed as the `ILogger<SmartsuppAgentCache>` dependency; log output/content is not asserted in this task.
- **System under test:** the concrete `SmartsuppAgentCache` class, constructed directly (no DI container), one fresh instance per `[Fact]`.
- **Test-only helper:** a private static `SetCachedAt(SmartsuppAgentCache instance, DateTime value)` reflection helper that writes the private `_cachedAt` field via `typeof(SmartsuppAgentCache).GetField("_cachedAt", BindingFlags.NonPublic | BindingFlags.Instance)`, used only in the warm-cache-failure scenarios (FR-2/FR-3) to force the TTL fast-path check (`_cache is not null && DateTime.UtcNow - _cachedAt < CacheTtl`) to evaluate false without needing a real one-hour wait or a production-code clock seam — consistent with the reflection-based private-field access already used elsewhere in this test project (e.g. `AddItemToBoxHandlerTests.cs`, `CatalogRepositoryTests.cs`).

### Test scenarios (mapped to spec.r1.md functional requirements)

| Scenario | FR | Setup | Assertion |
|---|---|---|---|
| Cold cache, API throws | FR-1 | `GetAgentsAsync` throws on its only invocation; `_cache` never populated | Result is non-null, empty dictionary; call does not throw |
| Warm cache, then API throws | FR-2 | `SetupSequence`: call 1 returns agents (some with null `Name`), call 2 throws; `_cachedAt` reset via reflection between calls | Second call's result equals the first call's dictionary contents (stale data returned, not empty, not thrown) |
| Warm cache, repeated failures | FR-3 | Extends FR-2's setup: call 1 succeeds, calls 2+ all throw; `_cachedAt` reset via reflection before each subsequent call | Every subsequent call returns the same stale dictionary; `GetAgentsAsync` invocation count matches the number of forced refresh attempts (verified via `Mock.Verify(..., Times.Exactly(n))`) so the test fails loudly if the TTL fast-path silently absorbs a call instead of exercising the catch branch |
| Successful fetch filters null-named agents | FR-4 | `GetAgentsAsync` returns a mixed list (some `Name is null`, some not) on its only call | Returned dictionary contains only the non-null-named agents, keyed by `Id` -> `Name` |
| Fast-path is exercised (no regression to always-refresh) | FR-3 (sub-requirement) | Two immediate calls to `GetAgentNamesAsync()` with no `_cachedAt` reset in between, against a mock returning agents on its first call | `GetAgentsAsync` is invoked exactly once across both calls (`Times.Once`), proving the TTL cache-hit fast path (line 38) is itself covered |

## Data Schemas

No database schema, API request/response shape, or event payload changes. The only "shape" relevant to this task is the existing, unchanged public contract exercised end-to-end by the new tests:

```csharp
// Domain contract (Anela.Heblo.Domain.Features.Smartsupp) — unchanged
public interface ISmartsuppApiClient
{
    Task<IReadOnlyList<SmartsuppAgentData>> GetAgentsAsync(CancellationToken cancellationToken);
    // (other members not relevant to this cache)
}

public class SmartsuppAgentData
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? Email { get; set; }
}

// Application contract (Anela.Heblo.Application.Features.Smartsupp) — unchanged
public interface ISmartsuppAgentCache
{
    Task<IReadOnlyDictionary<string, string>> GetAgentNamesAsync(CancellationToken cancellationToken = default);
}
```

Test fixture data shape used across scenarios (in-test only, not a production schema):

```csharp
new SmartsuppAgentData { Id = "agent-1", Name = "Jana Nováková" },
new SmartsuppAgentData { Id = "agent-2", Name = null },       // must be excluded from the resulting dictionary
new SmartsuppAgentData { Id = "agent-3", Name = "Petr Svoboda" },
```

Expected dictionary after a successful `GetAgentsAsync` call using the fixture above:

```csharp
new Dictionary<string, string>
{
    ["agent-1"] = "Jana Nováková",
    ["agent-3"] = "Petr Svoboda",
}
```
