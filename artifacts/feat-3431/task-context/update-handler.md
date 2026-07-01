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

