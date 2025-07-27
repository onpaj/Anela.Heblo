using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Anela.Heblo.Application.Features.Configuration.Domain;
using Anela.Heblo.Application.Features.Configuration.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Configuration.Application;

/// <summary>
/// Handler for retrieving application configuration
/// </summary>
public class GetConfigurationHandler : IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GetConfigurationHandler> _logger;

    public GetConfigurationHandler(IConfiguration configuration, IHostEnvironment environment, ILogger<GetConfigurationHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Handling GetConfiguration request");

            var appConfig = await BuildApplicationConfigurationAsync();

            var response = new GetConfigurationResponse
            {
                Version = appConfig.Version,
                Environment = appConfig.Environment,
                UseMockAuth = appConfig.UseMockAuth,
                Timestamp = appConfig.Timestamp
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

    private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()
    {
        // Get version with priority order:
        // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
        // 2. Assembly informational version
        // 3. Assembly version
        // 4. Fallback to default
        var version = GetVersionFromSources();
        
        // Get environment
        var environment = _environment.EnvironmentName;
        
        // Get mock auth setting
        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);

        var config = ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth);
        
        await Task.CompletedTask; // Placeholder for potential async operations
        
        return config;
    }

    private string? GetVersionFromSources()
    {
        // 1. Try environment variable first (CI/CD pipeline)
        var version = Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION);
        if (!string.IsNullOrEmpty(version))
        {
            _logger.LogDebug("Version found from APP_VERSION environment variable: {Version}", version);
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