using Microsoft.Identity.Web;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Configuration;

namespace Anela.Heblo.API.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection ConfigureAuthentication(this IServiceCollection services, WebApplicationBuilder builder, ILogger logger)
    {
        // Mock authentication is ONLY controlled by UseMockAuth configuration variable
        // Environment name does NOT influence authentication mode (per updated specification)
        var useMockAuth = builder.Configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = builder.Configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        // Log authentication mode for debugging
        logger.LogInformation("Authentication Configuration - Environment: {Environment}, UseMockAuth: {UseMockAuth}, BypassJwtValidation: {BypassJwtValidation}",
            builder.Environment.EnvironmentName, useMockAuth, bypassJwtValidation);

        // Determine authentication strategy
        if (bypassJwtValidation || useMockAuth)
        {
            logger.LogInformation("Configuring Mock Authentication");
            ConfigureMockAuthentication(services);
        }
        else
        {
            logger.LogInformation("Configuring Microsoft Identity Authentication");
            ConfigureRealAuthentication(services, builder);
        }

        return services;
    }

    private static void ConfigureMockAuthentication(IServiceCollection services)
    {
        // Mock authentication - can be used in any environment when UseMockAuth=true
        services.AddAuthentication(ConfigurationConstants.MOCK_AUTH_SCHEME)
            .AddScheme<MockAuthenticationSchemeOptions, MockAuthenticationHandler>(ConfigurationConstants.MOCK_AUTH_SCHEME, _ => { });
        services.AddAuthorization();

        // Register a null GraphServiceClient for mock authentication
        // services.AddSingleton<Microsoft.Graph.GraphServiceClient?>(provider => null);
    }

    private static void ConfigureRealAuthentication(IServiceCollection services, WebApplicationBuilder builder)
    {
        // Real Microsoft Identity authentication
        // Use standard Microsoft Identity Web API authentication (recommended)
        // This includes proper JWT validation, signing keys, issuer and audience validation
        services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd")
            .EnableTokenAcquisitionToCallDownstreamApi()
            // .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"))
            .AddInMemoryTokenCaches();
    }

}