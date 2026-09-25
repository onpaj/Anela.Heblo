### task: remove-async-state-machine

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:24-41`
- Test (existing, unmodified — used only to verify no regression): `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

- [ ] **Step 1: Run the existing tests to confirm the baseline passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"`
Expected: PASS — all 5 tests in `GetConfigurationHandlerTests` green (`Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet`, `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty`, `Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent`, `Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet`, `Handle_SetsTimestampAtResponseConstructionTime`).

This is the regression baseline: no new test is needed because this task changes no observable behavior — it only removes an unnecessary compiler-generated state machine. These existing tests are what verify that.

- [ ] **Step 2: Edit `GetConfigurationHandler.Handle` — drop `async`, return `Task.FromResult(response)`**

Current code (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:24-41`):

```csharp
    public async Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling GetConfiguration request");

        var appConfig = BuildApplicationConfiguration();

        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
            Timestamp = DateTime.UtcNow,
        };

        _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);

        return response;
    }
```

Replace with:

```csharp
    public Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling GetConfiguration request");

        var appConfig = BuildApplicationConfiguration();

        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
            Timestamp = DateTime.UtcNow,
        };

        _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);

        return Task.FromResult(response);
    }
```

Only two lines actually change: the method signature (`public async Task<...>` → `public Task<...>`) and the final `return response;` → `return Task.FromResult(response);`. Every other line — both `_logger.LogDebug` calls, the `BuildApplicationConfiguration()` call, and the response object initializer — stays byte-for-byte identical. Do not touch `BuildApplicationConfiguration()`, `GetVersionFromSources()`, the constructor, or the field declarations above line 24.

- [ ] **Step 3: Build and confirm the CS1998 warning is gone with no new warnings**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds with 0 errors, and no CS1998 warning is reported for `GetConfigurationHandler.cs` (search the build output for `CS1998` and `GetConfigurationHandler.cs` — neither should appear together).

- [ ] **Step 4: Run the existing tests again to confirm no regression**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"`
Expected: PASS — the same 5 tests from Step 1, unmodified, all still green. `Handle_SetsTimestampAtResponseConstructionTime` in particular confirms `Task.FromResult(response)` still captures `DateTime.UtcNow` at the same point in execution as the old `async` version did (the assignment happens before the `return` either way, so this is unaffected by removing `async`).

- [ ] **Step 5: Run `dotnet format` to confirm no formatting drift**

Run: `cd backend && dotnet format --verify-no-changes --include src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
Expected: exits 0 (no formatting changes required). If it reports a diff, run `dotnet format --include src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` to apply it, then re-run Step 4.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
git commit -m "fix(configuration): remove unnecessary async state machine from GetConfigurationHandler"
```
