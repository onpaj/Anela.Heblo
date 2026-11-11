using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Handler for retrieving application configuration
/// </summary>
public class GetConfigurationHandler : IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>
{
    private const string DefaultVersion = "1.0.0";
    private const string DefaultEnvironment = "Production";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GetConfigurationHandler> _logger;

    public GetConfigurationHandler(IConfiguration configuration, ILogger<GetConfigurationHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling GetConfiguration request");

        var response = new GetConfigurationResponse
        {
            // Version priority order:
            // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
            // 2. Assembly informational version
            // 3. Assembly version
            // 4. Fallback to default
            Version = GetVersionFromSources() ?? DefaultVersion,
            Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? DefaultEnvironment,
            UseMockAuth = _configuration.GetValue<bool>(InfrastructureConfigurationKeys.USE_MOCK_AUTH, false),
        };

        _logger.LogDebug("Configuration retrieved successfully: {@Config}", response);

        return Task.FromResult(response);
    }

    private string? GetVersionFromSources()
    {
        // 1. Try environment variable first (CI/CD pipeline)
        var version = _configuration[InfrastructureConfigurationKeys.APP_VERSION];
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
