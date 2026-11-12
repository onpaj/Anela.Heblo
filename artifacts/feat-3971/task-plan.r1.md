# Feature-Flag Override Single-Flight Refresh Implementation Plan (#3971)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the recurring Npgsql `TaskCanceledException` bursts by making the feature-flag override cache refresh single-flight and independent of any HTTP request's cancellation token, and make the cancellation source visible in logs.

**Architecture:** `HebloFeatureProvider` (singleton OpenFeature provider) currently loads DB overrides through `IMemoryCache.GetOrCreateAsync`, which does not lock (every concurrent caller on a cold cache runs its own DB query) and whose factory captures the calling request's `CancellationToken`. We replace it with a lock-free `TryGetValue` fast path plus a `SemaphoreSlim`-guarded shared in-flight `Task`. The shared load runs on its own 5 s `CancellationTokenSource`; callers wait on it with `Task.WaitAsync(ct)`, so a caller cancelling only abandons its own wait. The catch block in `ResolveBooleanValueAsync` then logs caller-cancellation at Debug and everything else at Warning with a `cancelledBy` field.

**Tech Stack:** .NET 8, OpenFeature, Microsoft.Extensions.Caching.Memory, xUnit + Moq + FluentAssertions.

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` | Single-flight refresh, detached load token, cancellation-source logging |
| Modify | `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs` | New tests for FR-1..FR-4; existing tests stay unchanged |
| Modify | `docs/processes/module-feature-flags.md` | Describe the new cache refresh behaviour (process-doc rule) |
| Regenerate | `docs/processes/INDEX.md` | Via `python3 scripts/process-docs/check.py index` (expected: no diff) |

## Scope notes (read before starting)

- **No constructor change.** The spec says the refresh token is "linked to application shutdown if available". It is not available without adding a constructor dependency (`IHostApplicationLifetime`), and the design deliberately uses a plain `CancellationTokenSource(RefreshTimeout)`. Follow the design: do not add a dependency.
- **Do not touch** the Polly pipeline, `/health/ready`, Npgsql pool settings, or the upsert/clear handlers (they keep calling `_cache.Remove(HebloFeatureProvider.CacheKey)`, which still works).
- **Accepted behaviour:** a load already in flight when an admin write invalidates the cache may re-cache the pre-write data for up to 30 s. This is the same window as today; it is documented in Task 3, not fixed.
- The test project already sees internals of the Application project (`InternalsVisibleTo`), so `HebloFeatureProvider.CacheKey` and `HebloFeatureProvider.RefreshTimeout` are usable from tests.
- All commands are run from the repository root.

---

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
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
```

Expected: build succeeds with 0 errors; `dotnet format` leaves no further changes (`git status` shows only the two files above).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
git commit -m "fix(feature-flags): single-flight override refresh detached from caller token (#3971)"
```

---

### task: cancellation-source-diagnostics

Covers FR-4. Depends on `single-flight-refresh` (uses the `MakeProviderWithRepo` test helper and the detached load added there).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs` (the `catch` block of `ResolveBooleanValueAsync`)
- Test: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `HebloFeatureProviderTests.cs`, above the private `VerifyWarningLogged` helper:

```csharp
    [Fact]
    public async Task ResolveBooleanValueAsync_CallerCancelled_LogsDebugNotWarning()
    {
        var repo = new Mock<IFeatureFlagOverrideRepository>();
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProviderWithRepo(repo, logger: loggerMock.Object);
        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false, context: null, callerCts.Token);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(Reason.Error);
        repo.Verify(r => r.GetAllAsDictionaryAsync(It.IsAny<CancellationToken>()), Times.Never);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cancelledBy=caller")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_RefreshCancelledWithoutCallerCancel_LogsWarningCancelledByRefresh()
    {
        // The load's own timeout (or the DB) cancelled the query; the caller's token is untouched.
        var thrown = new OperationCanceledException("refresh timed out");
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "cancelledBy=refresh", "dbRetryExhausted=False", "attempts=1");
    }

    [Fact]
    public async Task ResolveBooleanValueAsync_NonCancellationFailure_LogsWarningCancelledByNone()
    {
        var thrown = new InvalidOperationException("DB unavailable");
        var loggerMock = new Mock<ILogger<HebloFeatureProvider>>();
        var provider = MakeProvider(thrownException: thrown, logger: loggerMock.Object);

        var result = await provider.ResolveBooleanValueAsync(FlagKey, defaultValue: false);

        result.Reason.Should().Be(Reason.Error);
        VerifyWarningLogged(loggerMock, thrown, "cancelledBy=none", "dbRetryExhausted=False", "attempts=1");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"
```

Expected: the three new tests FAIL — the first because a Warning is logged for a caller cancellation and no Debug entry exists, the other two because the Warning message has no `cancelledBy=` fragment. The other 13 tests PASS.

- [ ] **Step 3: Implement cancellation-source logging**

In `HebloFeatureProvider.cs`, inside `ResolveBooleanValueAsync`, replace the entire `catch (Exception ex) { ... }` block with:

```csharp
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                // The caller (typically an aborted HTTP request) gave up waiting. The shared
                // override refresh is not affected, so this is not a DB problem — keep it quiet.
                _logger.LogDebug(
                    "Feature flag resolution cancelled for key {FlagKey}; using default {DefaultValue}; cancelledBy=caller",
                    flagKey, defaultValue);
            }
            else
            {
                var attempts = ex.Data["Anela.DbRetryAttempts"] as int? ?? 1;
                var dbRetryExhausted = attempts > 1;
                var cancelledBy = ex is OperationCanceledException ? "refresh" : "none";
                _logger.LogWarning(
                    ex,
                    "Feature flag resolution failed for key {FlagKey}; using default {DefaultValue}; dbRetryExhausted={DbRetryExhausted} attempts={Attempts} cancelledBy={CancelledBy}",
                    flagKey, defaultValue, dbRetryExhausted, attempts, cancelledBy);
            }

            return new ResolutionDetails<bool>(
                flagKey,
                defaultValue,
                errorType: ErrorType.General,
                errorMessage: ex.Message,
                reason: Reason.Error);
        }
```

Notes for the implementer:
- The Warning template keeps `dbRetryExhausted={DbRetryExhausted} attempts={Attempts}` exactly as before (the two pre-existing log tests assert on `dbRetryExhausted=True`/`attempts=3` fragments); `cancelledBy` is appended at the end.
- The returned `ResolutionDetails` is unchanged for both branches: the method still never throws.

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HebloFeatureProviderTests"
```

Expected: all 16 tests PASS.

- [ ] **Step 5: Run the wider feature-flag tests, build and format**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
```

Expected: all tests PASS, build has 0 errors, `dotnet format` leaves no further changes.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs backend/test/Anela.Heblo.Tests/Features/FeatureFlags/HebloFeatureProviderTests.cs
git commit -m "fix(feature-flags): log caller-cancelled flag resolution at Debug with cancelledBy (#3971)"
```

---

### task: update-process-doc

Project rule (CLAUDE.md): a change to a process's behaviour updates its `docs/processes/` doc in the same PR; CI's "Process Docs" job fails otherwise. The owning doc is `docs/processes/module-feature-flags.md`. Depends on both code tasks (it documents their final behaviour).

**Files:**
- Modify: `docs/processes/module-feature-flags.md`
- Regenerate: `docs/processes/INDEX.md` (via script; expected unchanged)

- [ ] **Step 1: Update the cache description in "Data owned"**

In `docs/processes/module-feature-flags.md`, replace this bullet:

```markdown
- In-memory cache `feature_flag_overrides` (per app instance, `IMemoryCache`): the whole override
  table as a dictionary, absolute TTL 30 s, removed immediately by the upsert / clear handlers on
  the instance that handled the write.
```

with:

```markdown
- In-memory cache `feature_flag_overrides` (per app instance, `IMemoryCache`): the whole override
  table as a dictionary, absolute TTL 30 s, removed immediately by the upsert / clear handlers on
  the instance that handled the write. When the entry is missing, exactly **one** database query
  reloads it however many requests ask at the same moment (single-flight); the others wait for
  that one result. The reload does not belong to any request: it runs on its own 5-second timeout
  (`HebloFeatureProvider.RefreshTimeout`), so a browser aborting its request cannot cancel it. A
  failed reload is not cached — the next evaluation tries again.
```

- [ ] **Step 2: Add the new quirks to "Known quirks"**

In the same file, add these two bullets at the end of the `## Known quirks` list (directly after the bullet that starts "Only boolean flags are supported"):

```markdown
- **A cancelled request gets the default, quietly.** If the caller's own request is cancelled
  while it waits for the override reload, that one evaluation returns the caller-supplied default
  (same landing spot as the DB-error quirk above) and is logged at Debug with `cancelledBy=caller`.
  Real reload failures are logged at Warning with `cancelledBy=refresh` (the reload's own timeout
  or a DB-side cancellation) or `cancelledBy=none` (any other error). Before #3971 every one of
  these was a Warning and the aborted request also cancelled the DB command, which showed up in
  Application Insights as bursts of Npgsql `TaskCanceledException`.
- **A reload already running when an admin flips a flag can re-cache the old value.** The write
  handler removes the cache entry, but a reload that started just before the write may finish
  afterwards and store the pre-write table for up to 30 s. Same worst-case delay as on other
  instances.
```

- [ ] **Step 3: Regenerate the index and run the process-docs check**

Run:

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
git status --short docs/processes
```

Expected: `check.py index` rewrites `docs/processes/INDEX.md` with no content change (the doc's front-matter `summary` was not touched), `check.py check` reports no errors for `module-feature-flags`, and `git status` lists only `docs/processes/module-feature-flags.md` as modified. If `INDEX.md` does show a diff, include it in the commit. Do not edit the `verified_at` front-matter value: staleness is measured from the later of `verified_at` and the doc's own last commit, so committing this edit is sufficient.

- [ ] **Step 4: Commit**

```bash
git add docs/processes/module-feature-flags.md docs/processes/INDEX.md
git commit -m "docs(processes): feature-flag override cache single-flight refresh (#3971)"
```

---

## Spec coverage

| Requirement | Task | Evidence |
|---|---|---|
| FR-1 single-flight (cold cache, after expiry) | single-flight-refresh | `..._ConcurrentCallersOnColdCache_QueryDbOnce`, `..._ConcurrentCallersAfterCacheExpiry_QueryDbOnceMore` |
| FR-2 refresh not bound to caller token | single-flight-refresh | `..._CallerCancellation_DoesNotCancelLoadOrOtherCallers`, `..._LoadStartedByCancelledCaller_StillPopulatesCache` |
| FR-3 failure behaviour preserved | single-flight-refresh | 7 pre-existing tests unchanged, `..._FailedLoadIsNotCached_NextCallReloads` |
| FR-4 cancellation-source diagnostics | cancellation-source-diagnostics | `..._CallerCancelled_LogsDebugNotWarning`, `..._LogsWarningCancelledByRefresh`, `..._LogsWarningCancelledByNone`, pre-existing `DbRetryAttempts` log tests |
| NFR-1 lock-free hit path | single-flight-refresh | `TryGetValue` before `_gate.WaitAsync`; `..._CacheHit_DoesNotQueryDbAgain` |
| NFR-3 one connection per refresh | single-flight-refresh | same tests as FR-1 |
| Process-doc rule | update-process-doc | `check.py check` passes |

Known deviation from the spec text: the refresh token is a plain 5 s timeout token and is not linked to application shutdown (see "Scope notes"); this follows `design.r1.md`.
