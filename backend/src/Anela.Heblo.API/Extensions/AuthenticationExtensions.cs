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

        ConfigureAuthorizationPolicies(services);

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

        ConfigureAuthorizationPolicies(services);
    }

    private static void ConfigureAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Basic product margins access - requires authentication
            options.AddPolicy("ViewProductMargins", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    // For mock authentication, allow all authenticated users
                    if (context.User.HasClaim("auth_scheme", "MockAuthentication"))
                    {
                        return true;
                    }

                    // For real authentication, require specific roles or claims
                    return context.User.IsInRole("FinancialManager") ||
                           context.User.IsInRole("ProductManager") ||
                           context.User.IsInRole("Admin") ||
                           context.User.HasClaim("department", "finance") ||
                           context.User.HasClaim("department", "product") ||
                           context.User.HasClaim("department", "management");
                });
            });

            // Detailed margins access - for sensitive cost data
            options.AddPolicy("ViewDetailedMargins", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    // For mock authentication, allow all authenticated users (simplified for development)
                    if (context.User.HasClaim("auth_scheme", "MockAuthentication"))
                    {
                        return true;
                    }

                    // For real authentication, require higher-level roles
                    return context.User.IsInRole("FinancialManager") ||
                           context.User.IsInRole("Admin") ||
                           context.User.HasClaim("clearance", "confidential");
                });
            });

            // Margin management - for future administrative functions
            options.AddPolicy("ManageMargins", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    // For mock authentication, restrict to admin-like mock users
                    if (context.User.HasClaim("auth_scheme", "MockAuthentication"))
                    {
                        return context.User.HasClaim("role", "Admin") ||
                               context.User.HasClaim("role", "FinancialManager");
                    }

                    // For real authentication, require administrative roles only
                    return context.User.IsInRole("Admin") ||
                           (context.User.IsInRole("FinancialManager") &&
                            context.User.HasClaim("permission", "manage_margins"));
                });
            });
        });
    }

}