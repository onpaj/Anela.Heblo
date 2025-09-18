using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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

        // Note: GraphService is now handled via MockGraphService in UserManagementModule
        // No need to register GraphServiceClient for mock authentication
    }

    private static void ConfigureRealAuthentication(IServiceCollection services, WebApplicationBuilder builder)
    {
        // Real Microsoft Identity authentication
        // Add both Web App (for browser redirects) and Web API (for Bearer tokens) authentication
        var authBuilder = services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd")
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        // Also add API authentication for Bearer tokens (for API clients)
        services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

        // Configure HTTPS forwarding headers for deployment behind load balancer/proxy
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Note: GraphService now uses HttpClient directly with ITokenAcquisition
        // No need for GraphServiceClient registration

        // Add cookie authentication for E2E test sessions (staging and development environments)
        if (E2ETestAuthenticationMiddleware.ShouldBeRegistered(builder))
        {
            services.AddAuthentication()
                .AddCookie("E2ETestCookies", options =>
                {
                    options.Cookie.Name = "E2ETestSession";
                    options.Cookie.HttpOnly = true;
                    // Use secure policy only in staging, allow http in development
                    options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Staging")
                        ? CookieSecurePolicy.Always
                        : CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan = TimeSpan.FromHours(1);
                    options.LoginPath = "/account/login"; // Fallback to login if not authenticated
                    options.LogoutPath = "/account/logout";
                });

            // Register E2E testing services only when they're needed (real auth + staging/development)
            services.AddScoped<IServicePrincipalTokenValidator, ServicePrincipalTokenValidator>();
            services.AddScoped<IE2ESessionService, E2ESessionService>();
        }
    }


}