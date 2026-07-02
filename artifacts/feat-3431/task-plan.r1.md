# Remove IHostEnvironment from GetConfigurationHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `IHostEnvironment` injection in `GetConfigurationHandler` with a direct `IConfiguration` key lookup to eliminate an upward architectural dependency.

**Architecture:** `GetConfigurationHandler` lives in the Application layer and must not depend on `Microsoft.Extensions.Hosting` (an infrastructure concern). Reading `ASPNETCORE_ENVIRONMENT` directly from `IConfiguration` achieves the same result using a dependency already present in the handler. MediatR's `RegisterServicesFromAssembly` wires the handler automatically — no DI registration change is needed.

**Tech Stack:** .NET 8, MediatR, NSubstitute, xUnit, FluentAssertions

---

### task: update-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

- [ ] Open `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`.

- [ ] Remove the `using Microsoft.Extensions.Hosting;` using directive (line 3).

- [ ] Remove the `private readonly IHostEnvironment _environment;` field declaration (line 16).

- [ ] Remove the `IHostEnvironment environment` constructor parameter and its null guard. The constructor signature changes from 3 parameters to 2.

- [ ] In `BuildApplicationConfiguration()`, replace the comment and the `_environment.EnvironmentName` assignment with the inline-documented `IConfiguration` lookup.

The complete file after changes:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Anela.Heblo.Domain.Features.Configuration;
using MediatR;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Handler for retrieving application configuration
/// </summary>
public class GetConfigurationHandler : IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetConfigurationHandler> _logger;

    public GetConfigurationHandler(IConfiguration configuration, ILogger<GetConfigurationHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Handling GetConfiguration request");

            var appConfig = BuildApplicationConfiguration();

            var response = new GetConfigurationResponse
            {
                Version = appConfig.Version,
                Environment = appConfig.Environment,
                UseMockAuth = appConfig.UseMockAuth,
                Timestamp = appConfig.Timestamp,
            };

            _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving application configuration");
            throw;
        }
    }

    private ApplicationConfiguration BuildApplicationConfiguration()
    {
        // Get version with priority order:
        // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
        // 2. Assembly informational version
        // 3. Assembly version
        // 4. Fallback to default
        var version = GetVersionFromSources();

        // Falls back to ConfigurationConstants.DEFAULT_ENVIRONMENT ("Production") if not set
        var environment = _configuration["ASPNETCORE_ENVIRONMENT"]
            ?? ConfigurationConstants.DEFAULT_ENVIRONMENT;

        // Get mock auth setting
        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);

        var config = ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth);

        return config;
    }

    private string? GetVersionFromSources()
    {
        // 1. Try environment variable first (CI/CD pipeline)
        var version = _configuration[ConfigurationConstants.APP_VERSION];
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version resolved from configuration: {Version}", version);
            return version;
        }

        // 2. Try assembly informational version
        var assembly = Assembly.GetExecutingAssembly();
        version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version found from assembly informational version: {Version}", version);
            return version;
        }

        // 3. Try assembly version
        version = assembly.GetName().Version?.ToString();
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version found from assembly version: {Version}", version);
            return version;
        }

        _logger.LogDebug("No version found, will use default");
        return null;
    }
}
```

- [ ] Verify the build compiles with zero errors:
  ```
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```
  Expected: `Build succeeded. 0 Error(s)`

- [ ] Run dotnet format to confirm no style regressions:
  ```
  dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
  ```
  Expected: exit code 0 (no unformatted files)

- [ ] Commit:
  ```
  git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
  git commit -m "refactor: remove IHostEnvironment from GetConfigurationHandler

  Read ASPNETCORE_ENVIRONMENT from IConfiguration instead of IHostEnvironment
  to eliminate upward Application→Hosting architectural dependency."
  ```

---

### task: fix-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

- [ ] Open `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`.

- [ ] Remove the `using Microsoft.Extensions.Hosting;` using directive (line 5).

- [ ] In `CreateHandler`: remove the two lines that create and configure the `IHostEnvironment` substitute:
  ```csharp
  var environment = Substitute.For<IHostEnvironment>();
  environment.EnvironmentName.Returns("Test");
  ```

- [ ] In `CreateHandler`: add `["ASPNETCORE_ENVIRONMENT"] = "Test"` to the `configData` dictionary before building the `IConfiguration` so all four tests receive an environment value. Merge it into the incoming dictionary rather than replacing it, so per-test keys are preserved.

- [ ] Update the `GetConfigurationHandler` constructor call in `CreateHandler` to pass only 2 arguments (drop `environment`).

- [ ] Check whether the `NSubstitute` using is still needed anywhere in the file. If not, remove `using NSubstitute;` as well. (It was only used for `IHostEnvironment`.)

The complete file after changes:

```csharp
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Domain.Features.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Features.Configuration;

public class GetConfigurationHandlerTests
{
    private static GetConfigurationHandler CreateHandler(Dictionary<string, string?> configData)
    {
        // Ensure every test has an environment value; callers may override by supplying their own key.
        configData.TryAdd("ASPNETCORE_ENVIRONMENT", "Test");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new GetConfigurationHandler(configuration, NullLogger<GetConfigurationHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsVersionFromConfiguration_WhenAppVersionIsSet()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = "2.5.1-ci.42"
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().Be("2.5.1-ci.42");
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsEmpty()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = ""
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        // When APP_VERSION is empty, should fall back to assembly version (non-null, not the hardcoded "1.0.0" default)
        response.Version.Should().NotBeNullOrEmpty().And.NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_FallsBackToAssemblyVersion_WhenAppVersionIsAbsent()
    {
        // Arrange
        var handler = CreateHandler(new Dictionary<string, string?>());

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        // When APP_VERSION is absent, should fall back to assembly informational version or assembly version
        response.Version.Should().NotBeNullOrEmpty().And.NotBe(ConfigurationConstants.DEFAULT_VERSION);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectUseMockAuth_WhenAppVersionIsSet()
    {
        // Arrange — regression guard: surgical change must not break UseMockAuth wiring
        var handler = CreateHandler(new Dictionary<string, string?>
        {
            [ConfigurationConstants.APP_VERSION] = "1.2.3",
            [ConfigurationConstants.USE_MOCK_AUTH] = "true"
        });

        // Act
        var response = await handler.Handle(new GetConfigurationRequest(), CancellationToken.None);

        // Assert
        response.Version.Should().Be("1.2.3");
        response.UseMockAuth.Should().BeTrue();
    }
}
```

- [ ] Run only the handler tests to confirm all four pass:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConfigurationHandlerTests"
  ```
  Expected output: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] Run dotnet format to confirm no style regressions:
  ```
  dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes
  ```
  Expected: exit code 0

- [ ] Commit:
  ```
  git add backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs
  git commit -m "test: remove IHostEnvironment mock from GetConfigurationHandlerTests

  Supply ASPNETCORE_ENVIRONMENT via in-memory configuration instead.
  All four tests pass without NSubstitute."
  ```
