using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Anela.Heblo.API.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.Security;

namespace Anela.Heblo.API.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection ConfigureAuthentication(this IServiceCollection services, WebApplicationBuilder builder)
    {
        // Mock authentication is ONLY controlled by UseMockAuth configuration variable
        // Environment name does NOT influence authentication mode (per updated specification)
        var useMockAuth = builder.Configuration.GetValue<bool>("UseMockAuth", defaultValue: false);
        var bypassJwtValidation = builder.Configuration.GetValue<bool>("BypassJwtValidation", defaultValue: false);

        // Log authentication mode for debugging
        Console.WriteLine($"🔐 Environment: {builder.Environment.EnvironmentName}");
        Console.WriteLine($"🔐 UseMockAuth configuration: {useMockAuth}");
        Console.WriteLine($"🔐 BypassJwtValidation configuration: {bypassJwtValidation}");
        
        // If bypass is enabled, just use mock auth - no JWT validation at all
        if (bypassJwtValidation)
        {
            Console.WriteLine("🔓 BypassJwtValidation=true - Using Mock Authentication instead of broken JWT validation");
            ConfigureMockAuthentication(services);
        }
        else if (useMockAuth)
        {
            Console.WriteLine("🔐 Final authentication mode: Mock");
            ConfigureMockAuthentication(services);
        }
        else
        {
            Console.WriteLine("🔐 Final authentication mode: Real JWT");
            ConfigureRealAuthentication(services, builder, bypassJwtValidation);
        }

        return services;
    }

    private static void ConfigureMockAuthentication(IServiceCollection services)
    {
        // Mock authentication - can be used in any environment when UseMockAuth=true
        Console.WriteLine("🔓 Configuring Mock Authentication (UseMockAuth=true)");
        services.AddAuthentication("Mock")
            .AddScheme<MockAuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", _ => { });
        services.AddAuthorization();
        
        // Register a null GraphServiceClient for mock authentication
        services.AddSingleton<Microsoft.Graph.GraphServiceClient?>(provider => null);
    }

    private static void ConfigureRealAuthentication(IServiceCollection services, WebApplicationBuilder builder, bool bypassJwtValidation)
    {
        // Real Microsoft Identity authentication
        Console.WriteLine("🔒 Configuring Microsoft Identity Authentication (UseMockAuth=false)");
        
        if (bypassJwtValidation)
        {
            Console.WriteLine("🔓 BypassJwtValidation=true - Using Mock Authentication instead of broken JWT validation");
            ConfigureMockAuthentication(services);
        }
        else
        {
            Console.WriteLine("🔒 Using standard Microsoft Identity Web API configuration");
            // Use standard Microsoft Identity Web API authentication (recommended)
            // This includes proper JWT validation, signing keys, issuer and audience validation
            services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd")
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches();
        }
    }

}