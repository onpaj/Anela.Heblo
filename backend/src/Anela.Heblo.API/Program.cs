using Microsoft.Extensions.FileProviders;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Anela.Heblo.API.Services;
using Anela.Heblo.API.Extensions;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging first
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(); // Always add console logging for container stdout
        
        // Add Application Insights
        var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"] 
                                        ?? builder.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"]
                                        ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        
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
            builder.Logging.AddApplicationInsights(
                configureTelemetryConfiguration: (config) => config.ConnectionString = appInsightsConnectionString,
                configureApplicationInsightsLoggerOptions: (options) => { }
            );
            
            Console.WriteLine($"📊 Application Insights configured for {builder.Environment.EnvironmentName}");
            Console.WriteLine($"📊 Connection String: {appInsightsConnectionString[..20]}...");
        }
        else
        {
            Console.WriteLine("⚠️ Application Insights not configured - missing ConnectionString");
            Console.WriteLine("⚠️ Checked: ApplicationInsights:ConnectionString, APPINSIGHTS_INSTRUMENTATIONKEY, APPLICATIONINSIGHTS_CONNECTION_STRING");
        }
        
        // Configure logging levels
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        
        // Add structured logging for better Application Insights experience  
        if (builder.Environment.IsProduction())
        {
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        }

        // Configure authentication using extension method
        builder.Services.ConfigureAuthentication(builder);

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