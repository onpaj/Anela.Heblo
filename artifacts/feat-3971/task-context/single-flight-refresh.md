### task: single-flight-refresh

Covers FR-1 (single-flight), FR-2 (refresh not bound to caller token), FR-3 (failures not cached, never throws), NFR-1 (lock-free hit path).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs`

- [ ] **Step 1: Add a test helper that accepts a caller-supplied repository mock and cache**

In `HebloFeatureProviderTests.cs`, add this method directly below the existing `MakeProvider` method (do not change `MakeProvider`):

```csharp
    private static HebloFeatureProvider MakeProviderWithRepo(
        Mock<IFeatureFlagOverrideRepository> repo,
        IMemoryCache? cache = null,
        ILogger<HebloFeatureProvider>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new HebloFeatureProvider(
            scopeFactory,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
            logger ?? NullLogger<HebloFeatureProvider>.Instance);
    }
```

- [ ] **Step 2: Write the failing tests**

Add these tests to `HebloFeatureProviderTests.cs`, above the private `VerifyWarningLogged` helper:

```csharp
    [Fact]
    public async Task ResolveBooleanValueAsync_ConcurrentCallersOnColdCache_QueryDbOnce()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var provider = MakeProviderWithRepo(repo);

        var calls = Enumerable.Range(0, 20)
            .Select(_ => provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false))
            .ToArray();
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(r => r.Value && r.Reason == Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = MakeProviderWithRepo(repo, cache);

        var first = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        await first;

        // Simulates TTL expiry (the entry disappears from the cache).
        cache.Remove(HebloFeatureProvider.CacheKey);
        pending = new TaskCompletionSource<Dictionary<string, bool>>();

        var calls = Enumerable.Range(0, 20)
            .Select(_ => provider.ResolveBooleanValueAsync(FlagKey, defaultValue: true))
            .ToArray();
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = false });
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(r => !r.Value && r.Reason == Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_CallerCancellation_DoesNotCancelLoadOrOtherCallers()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repoTokens = new List<CancellationToken>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                repoTokens.Add(token);
                return pending.Task;
            });
        var provider = MakeProviderWithRepo(repo);
        using var callerCts = new CancellationTokenSource();

        var cancelledCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);
        var otherCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        callerCts.Cancel();

        var cancelledResult = await cancelledCall;
        cancelledResult.Value.Should().BeFalse();
        cancelledResult.Reason.Should().Be(Reason.Error);

        repoTokens.Should().ContainSingle();
        repoTokens[0].Should().NotBe(callerCts.Token);
        repoTokens[0].CanBeCanceled.Should().BeTrue("the load runs on its own timeout token");
        repoTokens[0].IsCancellationRequested.Should().BeFalse();
        otherCall.IsCompleted.Should().BeFalse();

        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });
        var otherResult = await otherCall;
        otherResult.Value.Should().BeTrue();
        otherResult.Reason.Should().Be(Reason.TargetingMatch);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_LoadStartedByCancelledCaller_StillPopulatesCache()
    {
        var pending = new TaskCompletionSource<Dictionary<string, bool>>();
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => pending.Task);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = MakeProviderWithRepo(repo, cache);
        using var callerCts = new CancellationTokenSource();

        var cancelledCall = provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);
        callerCts.Cancel();
        (await cancelledCall).Reason.Should().Be(Reason.Error);

        // No RunContinuationsAsynchronously on the TCS: the load's continuation (cache.Set)
        // runs inline inside SetResult, so the cache is populated when SetResult returns.
        pending.SetResult(new Dictionary<string, bool> { [FlagKey] = true });

        cache.TryGetValue(HebloFeatureProvider.CacheKey, out Dictionary<string, bool>? cached).Should().BeTrue();
        cached.Should().ContainKey(FlagKey).WhoseValue.Should().BeTrue();

        var next = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        next.Value.Should().BeTrue();
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_FailedLoadIsNotCached_NextCallReloads()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.SetupSequence(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"))
            .ReturnsAsync(new Dictionary<string, bool> { [FlagKey] = true });
        var provider = MakeProviderWithRepo(repo);

        var failed = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        var recovered = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        failed.Value.Should().BeFalse();
        failed.Reason.Should().Be(Reason.Error);
        recovered.Value.Should().BeTrue();
        recovered.Reason.Should().Be(Reason.TargetingMatch);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_CacheHit_DoesNotQueryDbAgain()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        repo.Setup(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, bool> { [FlagKey] = true });
        var provider = MakeProviderWithRepo(repo);

        await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);
        var second = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        second.Value.Should().BeTrue();
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 3: Run the tests to verify the new behaviour is missing**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"
```

Expected: these four FAIL against the current implementation —
- `..._ConcurrentCallersOnColdCache_QueryDbOnce` (repository called 20 times, expected once)
- `..._ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore` (called 21 times, expected 2)
- `..._CallerCancellation_DoesNotCancelLoadOrOtherCallers` (two repository tokens captured, one of them the caller's)
- `..._LoadStartedByCancelledCaller_StillPopulatesCache` (the load token is the caller's token)

`..._FailedLoadIsNotCached_NextCallReloads` and `..._CacheHit_DoesNotQueryDbAgain` PASS already — they are characterization tests that pin behaviour the rewrite must keep. The 7 pre-existing tests PASS.

- [ ] **Step 4: Implement the single-flight, caller-independent refresh**

In `HebloFeatureProvider.cs`:

(a) Replace the two existing field lines

```csharp
    internal const string CacheKey = "feature_flag_overrides";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
```

with

```csharp
    internal const string CacheKey = "feature_flag_overrides";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);

    // Guards _inflight so that concurrent cache misses share one DB load (single-flight).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<Dictionary<string, bool>>? _inflight;
```

(b) Replace the whole existing `GetOverridesAsync` method (the last method in the class) with these two methods:

```csharp
    private async Task<Dictionary<string, bool>> GetOverridesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Hit path: no lock.
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, bool>? hit) && hit is not null)
            return hit;

        Task<Dictionary<string, bool>> load;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out hit) && hit is not null)
                return hit;

            // A completed task here is either a failed load (failures are never cached, so retry)
            // or a successful one whose cache entry has since expired or been invalidated.
            if (_inflight is null || _inflight.IsCompleted)
                _inflight = LoadOverridesAsync();

            load = _inflight;
        }
        finally
        {
            _gate.Release();
        }

        // A cancelled caller abandons only its own wait; the shared load keeps running.
        return await load.WaitAsync(ct);
    }

    /// <summary>
    /// Loads all overrides and caches them. Shared by every caller that missed the cache, so it
    /// must not observe any caller's CancellationToken — it is bounded by RefreshTimeout instead.
    /// </summary>
    private async Task<Dictionary<string, bool>> LoadOverridesAsync()
    {
        using var cts = new CancellationTokenSource(RefreshTimeout);
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IFeatureFlagOverrideRepository>();
        var overrides = await repo.GetAllAsDictionaryAsync(cts.Token) ?? [];
        _cache.Set(CacheKey, overrides, CacheTtl);
        return overrides;
    }
```

Notes for the implementer:
- `_cache.Set(key, value, TimeSpan)` is the `Microsoft.Extensions.Caching.Memory` extension that sets an absolute expiration relative to now — the same TTL semantics as the old `entry.AbsoluteExpirationRelativeToNow = CacheTtl`.
- `_cache.Set` is reached only when the repository call succeeded, so failures are never cached.
- `_inflight` is only read and written while holding `_gate`. There is no `finally` that clears it; the `IsCompleted` check does that job.
- `Task.WaitAsync(CancellationToken)` is a .NET 6+ BCL method; no new using is needed.
- Do not change `ResolveBooleanValueAsync` in this task.

- [ ] **Step 5: Run the tests to verify they pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"
```

Expected: all 13 tests PASS (7 pre-existing + 6 new).

- [ ] **Step 6: Build and format**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
```

Expected: build succeeds with 0 errors; `dotnet format` leaves no further changes (`git status` shows only the two files above). If the solution file is not at `backend/Anela.Heblo.sln`, locate it with `git ls-files '*.sln'` and use that path.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
git commit -m "fix(feature-flags): single-flight override refresh detached from caller token (#3971)"
```

---

