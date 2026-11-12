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

