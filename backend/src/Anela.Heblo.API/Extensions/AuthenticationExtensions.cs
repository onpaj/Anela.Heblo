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
        services.AddSingleton<Microsoft.Graph.GraphServiceClient>(provider => null!);
    }

    private static void ConfigureRealAuthentication(IServiceCollection services, WebApplicationBuilder builder, bool bypassJwtValidation)
    {
        // Real Microsoft Identity authentication
        Console.WriteLine("🔒 Configuring Microsoft Identity Authentication (UseMockAuth=false)");
        
        // Use Microsoft Identity Web API authentication (includes all required services)
        services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd")
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"))
            .AddInMemoryTokenCaches();
        
        // Override JWT Bearer options for our custom configuration
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            ConfigureJwtBearerOptions(options, builder.Configuration, bypassJwtValidation);
        });

        // Microsoft Graph and other services are already registered by AddMicrosoftIdentityWebApiAuthentication above
    }

    private static void ConfigureJwtBearerOptions(JwtBearerOptions options, IConfiguration configuration, bool bypassJwtValidation)
    {
        var azureAdConfig = configuration.GetSection("AzureAd");
        var tenantId = azureAdConfig["TenantId"];
        var clientId = azureAdConfig["ClientId"];
        
        // Debug configuration values
        Console.WriteLine($"🔑 Loading Azure AD configuration:");
        Console.WriteLine($"🔑 TenantId from config: '{tenantId}' (expected: '31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a')");
        Console.WriteLine($"🔑 ClientId from config: '{clientId}' (expected: '8b34be89-f86f-422f-af40-7dbcd30cb66a')");
        
        if (string.IsNullOrEmpty(tenantId) || tenantId.Contains("your-tenant-id"))
        {
            Console.WriteLine("❌ TenantId is not properly configured! Check Azure App Service environment variables.");
            Console.WriteLine("❌ Required: AzureAd__TenantId=31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a");
        }
        
        // Use common endpoint which should work for all tenants
        options.Authority = $"https://login.microsoftonline.com/common";
        options.MetadataAddress = $"https://login.microsoftonline.com/common/.well-known/openid_configuration";
        
        // Enable automatic retrieval of signing keys from metadata endpoint
        options.RequireHttpsMetadata = true;
        options.SaveToken = false;
        options.IncludeErrorDetails = true;
        
        // Add timeout and retry configuration for metadata retrieval
        options.BackchannelTimeout = TimeSpan.FromSeconds(30);
        
        // Configure HTTP handler with certificate bypass if needed
        var httpHandler = new HttpClientHandler();
        if (bypassJwtValidation)
        {
            Console.WriteLine("🔓 BypassJwtValidation=true - Also bypassing SSL certificate validation");
            httpHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }
        options.BackchannelHttpHandler = httpHandler;
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences = new[]
            {
                "00000003-0000-0000-c000-000000000000", // Microsoft Graph
                clientId ?? "" // Also accept tokens for our app
            },
            ValidIssuers = new[]
            {
                $"https://sts.windows.net/{tenantId}/", // v1.0 issuer (matches your token)
                $"https://login.microsoftonline.com/{tenantId}/v2.0", // v2.0 issuer (fallback)
                $"https://login.microsoftonline.com/common/v2.0", // common v2.0 issuer
                $"https://sts.windows.net/common/" // common v1.0 issuer
            },
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            NameClaimType = "name",
            RoleClaimType = "roles",
            // Explicitly enable automatic key retrieval
            RequireExpirationTime = true,
            ValidateTokenReplay = false
        };
        
        // Apply JWT signature bypass workaround if configured
        if (bypassJwtValidation)
        {
            Console.WriteLine("🔓 Applying JWT signature validation bypass workaround");
            options.TokenValidationParameters.ValidateIssuerSigningKey = false;
            options.TokenValidationParameters.SignatureValidator = delegate (string token, TokenValidationParameters parameters)
            {
                try
                {
                    // Workaround: Return JsonWebToken without signature validation
                    var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token);
                    return jwt;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ JsonWebToken parsing failed: {ex.Message}");
                    Console.WriteLine($"🔄 Falling back to JwtSecurityToken");
                    
                    // Fallback to old JWT handler if JsonWebToken fails
                    try
                    {
                        var legacyJwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token);
                        
                        // Convert to JsonWebToken manually
                        var jsonWebToken = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(
                            legacyJwt.Header.SerializeToJson(),
                            legacyJwt.Payload.SerializeToJson()
                        );
                        return jsonWebToken;
                    }
                    catch (Exception legacyEx)
                    {
                        Console.WriteLine($"❌ JwtSecurityToken parsing also failed: {legacyEx.Message}");
                        throw new SecurityException($"Unable to parse JWT token: {ex.Message}");
                    }
                }
            };
            Console.WriteLine("✅ JWT signature validation bypassed using SignatureValidator delegate");
        }
        
        // Enable PII logging temporarily for debugging
        Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = 
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" || 
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Test";
        
        Console.WriteLine($"🔑 JWT Authority: {options.Authority}");
        Console.WriteLine($"🔑 JWT Metadata: {options.MetadataAddress}");
        Console.WriteLine($"🔑 Valid Audiences: {string.Join(", ", options.TokenValidationParameters.ValidAudiences)}");
        Console.WriteLine($"🔑 Valid Issuers: {string.Join(", ", options.TokenValidationParameters.ValidIssuers)}");
        
        // Test multiple metadata endpoints during startup (v1.0 first!)
        TestMetadataEndpoints(tenantId, bypassJwtValidation, options);
        
        // Log token validation details for debugging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError("JWT Authentication failed: {Error} | Path: {Path} | UserAgent: {UserAgent}", 
                    context.Exception.Message, 
                    context.Request.Path,
                    context.Request.Headers.UserAgent.FirstOrDefault());
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var audience = context.Principal?.FindFirst("aud")?.Value ?? "unknown";
                var issuer = context.Principal?.FindFirst("iss")?.Value ?? "unknown";
                var userName = context.Principal?.FindFirst("name")?.Value ?? "unknown";
                
                logger.LogInformation("JWT Token validated successfully | User: {UserName} | Audience: {Audience} | Issuer: {Issuer} | Path: {Path}",
                    userName, audience, issuer, context.Request.Path);
                return Task.CompletedTask;
            }
        };
    }

    private static void TestMetadataEndpoints(string? tenantId, bool bypassJwtValidation, JwtBearerOptions options)
    {
        var metadataEndpoints = new[]
        {
            $"https://login.microsoftonline.com/common/.well-known/openid_configuration",
            $"https://login.microsoftonline.com/common/v2.0/.well-known/openid_configuration", 
            $"https://login.microsoftonline.com/{tenantId}/.well-known/openid_configuration",
            $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid_configuration"
        };
        
        string? workingEndpoint = null;
        foreach (var endpoint in metadataEndpoints)
        {
            try
            {
                Console.WriteLine($"🔑 Testing metadata endpoint: {endpoint}");
                
                var testHttpHandler = new HttpClientHandler();
                if (bypassJwtValidation)
                {
                    testHttpHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                }
                
                using var httpClient = new HttpClient(testHttpHandler);
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var metadataResponse = httpClient.GetAsync(endpoint).Result;
                Console.WriteLine($"🔑 Status: {metadataResponse.StatusCode}");
                
                if (metadataResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Successfully accessed: {endpoint}");
                    workingEndpoint = endpoint;
                    
                    // Update the options to use working endpoint
                    options.MetadataAddress = endpoint;
                    break;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ HTTP Request failed {endpoint}: {ex.Message}");
                if (ex.Message.Contains("SSL") || ex.Message.Contains("certificate") || ex.Message.Contains("TLS"))
                {
                    Console.WriteLine($"🔓 SSL/Certificate error detected. Set BypassJwtValidation=true to bypass certificate validation.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed {endpoint}: {ex.Message}");
            }
        }
        
        if (workingEndpoint == null && !bypassJwtValidation)
        {
            Console.WriteLine($"❌ CRITICAL: No metadata endpoint accessible!");
            Console.WriteLine($"❌ Network connectivity issue detected.");
            Console.WriteLine($"❌ Set BypassJwtValidation=true to bypass signature validation");
            Console.WriteLine($"❌ JWT authentication will fail without network access or bypass");
        }
        else if (workingEndpoint != null)
        {
            Console.WriteLine($"✅ Using metadata endpoint: {workingEndpoint}");
        }
    }
}