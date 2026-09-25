### task: remove-backend-timestamp-field

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`

- [ ] **Step 1: Confirm the current failing-safe baseline by running the existing test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS (all 5 tests green) — this confirms the starting state before any edits.

- [ ] **Step 2: Remove the `Timestamp`-asserting lines from the integration test**

In `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`, in `GetConfiguration_ShouldReturnValidConfigurationResponse`, change:

```csharp
    [Fact]
    public async Task GetConfiguration_ShouldReturnValidConfigurationResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Environment.Should().NotBeNull();
        (configResponse.Timestamp > DateTime.MinValue).Should().BeTrue();
        Assert.True(configResponse.Timestamp <= DateTime.UtcNow.AddMinutes(1)); // Allow 1 minute tolerance
    }
```

to:

```csharp
    [Fact]
    public async Task GetConfiguration_ShouldReturnValidConfigurationResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Environment.Should().NotBeNull();
    }
```

Leave the other three test methods (`GetConfiguration_ShouldReturnSuccessAndCorrectContentType`, `GetConfiguration_ShouldIncludeMockAuthFlag`, `GetConfiguration_ShouldReturnTestEnvironment`, `GetConfiguration_ShouldReturnValidVersion`) untouched.

- [ ] **Step 3: Run the test to verify it still passes (field still exists on the DTO at this point)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS — the assertions removed were the only ones referencing `Timestamp`; nothing else changed yet.

- [ ] **Step 4: Remove the `Timestamp` property from `GetConfigurationResponse`**

In `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`, change:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }

    /// <summary>
    /// Response timestamp in UTC
    /// </summary>
    public DateTime Timestamp { get; set; }
}
```

to:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }
}
```

- [ ] **Step 5: Remove the `Timestamp` assignment from `GetConfigurationHandler`**

In `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`, change:

```csharp
        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
            Timestamp = DateTime.UtcNow,
        };
```

to:

```csharp
        var response = new GetConfigurationResponse
        {
            Version = appConfig.Version,
            Environment = appConfig.Environment,
            UseMockAuth = appConfig.UseMockAuth,
        };
```

Do not touch any other line in this file — `BuildApplicationConfiguration()` and `GetVersionFromSources()` are unrelated and must stay exactly as they are.

- [ ] **Step 6: Build the backend and run the full Configuration test file to verify everything compiles and passes**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetConfigurationEndpointTests`
Expected: PASS (all 4 remaining tests green).

- [ ] **Step 7: Run `dotnet format` and commit**

Run: `dotnet format backend/Anela.Heblo.sln`

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs
git commit -m "fix: remove out-of-scope Timestamp field from GetConfigurationResponse"
```

---
