# Design: Remove ApplicationConfiguration entity and ConfigurationConstants

## Component Design
`GetConfigurationHandler` (Application layer) is the sole component. Target shape:

```csharp
public class GetConfigurationHandler : IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>
{
    private const string DefaultVersion = "1.0.0";
    private const string DefaultEnvironment = "Production";
    // ctor unchanged

    public Task<GetConfigurationResponse> Handle(...)
    {
        _logger.LogDebug("Handling GetConfiguration request");
        var response = new GetConfigurationResponse
        {
            Version = GetVersionFromSources() ?? DefaultVersion,
            Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? DefaultEnvironment,
            UseMockAuth = _configuration.GetValue<bool>(InfrastructureConfigurationKeys.USE_MOCK_AUTH, false),
        };
        _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);
        return Task.FromResult(response);
    }
    // GetVersionFromSources unchanged
}
```
Keep the explanatory comments on version priority order. Remove `using Anela.Heblo.Domain.Features.Configuration;`.

Tests (`GetConfigurationHandlerTests`): replace `ConfigurationConstants.DEFAULT_VERSION` with `"1.0.0"`; add tests `Handle_ReturnsProductionEnvironment_WhenAspNetCoreEnvironmentAbsent`, `Handle_ReturnsConfiguredEnvironment_WhenSet`, `Handle_UseMockAuthDefaultsToFalse_WhenAbsent`.

## Data Schemas
No change. `GetConfigurationResponse { string Version; string Environment; bool UseMockAuth }`.
