using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web.Resource;
using Microsoft.Extensions.FileProviders;
using Anela.Heblo.API.Authentication;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Anela.Heblo.API.Services;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add Application Insights
        var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            builder.Services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
            {
                ConnectionString = appInsightsConnectionString,
                EnableAdaptiveSampling = true,
                EnableQuickPulseMetricStream = true,
                EnableDependencyTrackingTelemetryModule = true,
                EnablePerformanceCounterCollectionModule = true,
                EnableRequestTrackingTelemetryModule = true,
                EnableEventCounterCollectionModule = true,
                EnableAppServicesHeartbeatTelemetryModule = true,
                DeveloperMode = builder.Environment.IsDevelopment()
            });
            
            // Add logging to Application Insights
            builder.Logging.AddApplicationInsights();
            
            Console.WriteLine($"📊 Application Insights configured for {builder.Environment.EnvironmentName}");
        }
        else
        {
            Console.WriteLine("⚠️ Application Insights not configured - missing ConnectionString");
        }

        // Add services to the container.
        var useMockAuth = builder.Configuration.GetValue<bool>("UseMockAuth");
        
        // Only use mock auth if explicitly configured or in specific development environments
        if (builder.Environment.EnvironmentName == "Development" || 
            builder.Environment.EnvironmentName == "Automation" ||
            builder.Environment.EnvironmentName == "Test")
        {
            useMockAuth = true;
        }
        
        // In Production environment, NEVER use mock auth unless explicitly forced
        if (builder.Environment.EnvironmentName == "Production")
        {
            useMockAuth = false;
        }

        // Log authentication mode for debugging
        Console.WriteLine($"🔐 Environment: {builder.Environment.EnvironmentName}");
        Console.WriteLine($"🔐 Using Mock Authentication: {useMockAuth}");
                         
        if (useMockAuth)
        {
            // Mock authentication for development and automation
            Console.WriteLine("🔓 Configuring Mock Authentication for development/testing");
            builder.Services.AddAuthentication("Mock")
                .AddScheme<MockAuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", _ => { });
            builder.Services.AddAuthorization();
            
            // Register a null GraphServiceClient for mock authentication
            builder.Services.AddSingleton<Microsoft.Graph.GraphServiceClient>(provider => null!);
        }
        else
        {
            // Real Microsoft Identity authentication
            Console.WriteLine("🔒 Configuring Microsoft Identity Authentication for production");
            
            // Configure JWT Bearer to accept Microsoft Graph tokens
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var azureAdConfig = builder.Configuration.GetSection("AzureAd");
                    var tenantId = azureAdConfig["TenantId"];
                    
                    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidAudiences = new[]
                        {
                            "00000003-0000-0000-c000-000000000000", // Microsoft Graph
                            azureAdConfig["ClientId"] // Also accept tokens for our app
                        },
                        ValidIssuers = new[]
                        {
                            $"https://login.microsoftonline.com/{tenantId}/v2.0",
                            $"https://sts.windows.net/{tenantId}/"
                        },
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromMinutes(5)
                    };
                    
                    // Log token validation details for debugging
                    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            Console.WriteLine($"❌ JWT Authentication failed: {context.Exception.Message}");
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            Console.WriteLine("✅ JWT Token validated successfully");
                            var audience = context.Principal?.FindFirst("aud")?.Value;
                            var issuer = context.Principal?.FindFirst("iss")?.Value;
                            Console.WriteLine($"   Audience: {audience}");
                            Console.WriteLine($"   Issuer: {issuer}");
                            return Task.CompletedTask;
                        }
                    };
                });

            // Still add Microsoft Graph support for downstream API calls
            builder.Services.AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"));
        }

        // Add CORS
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        builder.Services.AddControllers();
        
        // Register telemetry service - conditionally based on Application Insights configuration
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
        }
        else
        {
            builder.Services.AddSingleton<ITelemetryService, NoOpTelemetryService>();
        }

        // Add health checks
        var healthChecksBuilder = builder.Services.AddHealthChecks();
        
        // Add database health check if connection string exists
        var dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            healthChecksBuilder.AddNpgSql(dbConnectionString, name: "database", tags: new[] { "db", "postgresql" });
        }
        
        // Application Insights telemetry is automatically integrated via AddApplicationInsightsTelemetry

        // Add SPA static files support
        builder.Services.AddSpaStaticFiles(configuration =>
        {
            configuration.RootPath = "wwwroot";
        });

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseOpenApi();
        }

        app.UseHttpsRedirection();

        // Use CORS
        app.UseCors("AllowFrontend");

        app.UseAuthentication();
        app.UseAuthorization();

        // Serve static files from wwwroot
        app.UseStaticFiles();

        // If not in development, also use SPA static files
        if (!app.Environment.IsDevelopment())
        {
            app.UseSpaStaticFiles();
        }

        app.MapControllers();

        // Map health check endpoints
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("db"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,  // Only app liveness, no dependencies
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        // SPA fallback - must be after MapControllers
        if (!app.Environment.IsDevelopment())
        {
            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "wwwroot";
                spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
                {
                    OnPrepareResponse = context =>
                    {
                        // Prevent caching of index.html
                        context.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                        context.Context.Response.Headers.Append("Pragma", "no-cache");
                        context.Context.Response.Headers.Append("Expires", "0");
                    }
                };
            });
        }

        app.Run();
    }
}