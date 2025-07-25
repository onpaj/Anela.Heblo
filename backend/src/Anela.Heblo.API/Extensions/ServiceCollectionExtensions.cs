using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Anela.Heblo.API.Constants;
using Anela.Heblo.Application.Interfaces;
using Anela.Heblo.Application.Services;
using Anela.Heblo.Infrastructure.Services;

namespace Anela.Heblo.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationInsightsServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var appInsightsConnectionString = configuration[ConfigurationConstants.APPLICATION_INSIGHTS_CONNECTION_STRING]
                                        ?? configuration[ConfigurationConstants.APPINSIGHTS_INSTRUMENTATION_KEY]
                                        ?? configuration[ConfigurationConstants.APPLICATIONINSIGHTS_CONNECTION_STRING];

        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
            {
                ConnectionString = appInsightsConnectionString,
                EnableAdaptiveSampling = true,
                EnableQuickPulseMetricStream = true,
                EnableDependencyTrackingTelemetryModule = true,
                EnablePerformanceCounterCollectionModule = true,
                EnableRequestTrackingTelemetryModule = true,
                EnableEventCounterCollectionModule = true,
                EnableAppServicesHeartbeatTelemetryModule = true,
                DeveloperMode = environment.IsDevelopment()
            });

            services.AddSingleton<ITelemetryService, Anela.Heblo.Infrastructure.Services.TelemetryService>();
        }
        else
        {
            services.AddSingleton<ITelemetryService, Anela.Heblo.Infrastructure.Services.NoOpTelemetryService>();
        }

        return services;
    }

    public static IServiceCollection AddCorsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection(ConfigurationConstants.CORS_ALLOWED_ORIGINS).Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(ConfigurationConstants.CORS_POLICY_NAME, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks();

        // Add database health check if connection string exists
        var dbConnectionString = configuration.GetConnectionString(ConfigurationConstants.DEFAULT_CONNECTION);
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            healthChecksBuilder.AddNpgSql(dbConnectionString,
                name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
                tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG });
        }

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register HttpContextAccessor for user service
        services.AddHttpContextAccessor();

        // Register Application Services
        services.AddScoped<IWeatherService, WeatherService>();
        services.AddScoped<IUserService, UserService>();

        return services;
    }

    public static IServiceCollection AddSpaServices(this IServiceCollection services)
    {
        services.AddSpaStaticFiles(configuration =>
        {
            configuration.RootPath = "wwwroot";
        });

        return services;
    }
}