using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Anela.Heblo.API.Telemetry;

namespace Anela.Heblo.API.Extensions;

public static class ApplicationInsightsExtensions
{
    public static IServiceCollection AddOptimizedApplicationInsights(
        this IServiceCollection services, 
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];
        
        // Skip Application Insights if no connection string
        if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            return services;
        }

        // Configure Application Insights with cost optimizations
        var options = new ApplicationInsightsServiceOptions
        {
            ConnectionString = appInsightsConnectionString,
            
            // Sampling configuration - reduce data volume
            EnableAdaptiveSampling = true,
            
            // Disable expensive features in non-production
            EnableQuickPulseMetricStream = environment.IsProduction() && configuration.GetValue<bool>("ApplicationInsights:EnableLiveMetrics", false),
            
            // Selective module enabling based on environment
            EnableDependencyTrackingTelemetryModule = true,
            EnablePerformanceCounterCollectionModule = environment.IsProduction(),
            EnableRequestTrackingTelemetryModule = true,
            EnableEventCounterCollectionModule = environment.IsProduction(),
            EnableAppServicesHeartbeatTelemetryModule = environment.IsProduction(),
            
            // Developer mode only in development
            DeveloperMode = environment.IsDevelopment(),
            
            // Disable unnecessary modules
            EnableDiagnosticsTelemetryModule = false,
            EnableAzureInstanceMetadataTelemetryModule = false,
            
            // Request collection options
            RequestCollectionOptions = 
            {
                InjectResponseHeaders = false,
                TrackExceptions = true,
                EnableW3CDistributedTracing = true
            }
        };
        
        services.AddApplicationInsightsTelemetry(options);
        
        // Add telemetry initializer
        services.AddSingleton<ITelemetryInitializer, EnvironmentTelemetryInitializer>();
        
        // Add telemetry processor for filtering
        services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();
        
        // Configure sampling (more aggressive for cost savings)
        services.Configure<TelemetryConfiguration>((config) =>
        {
            var builder = config.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
            
            // Adaptive sampling with aggressive settings
            builder.UseAdaptiveSampling(
                maxTelemetryItemsPerSecond: environment.IsProduction() ? 5 : 1,
                excludedTypes: "Exception;Event"); // Always track exceptions and custom events
                
            // Fixed-rate sampling as fallback
            if (!environment.IsProduction())
            {
                builder.UseSampling(10); // Keep only 10% of telemetry in non-production
            }
            
            builder.Build();
        });
        
        return services;
    }
    
    public static IApplicationBuilder UseOptimizedApplicationInsights(this IApplicationBuilder app)
    {
        // Configure additional Application Insights middleware if needed
        return app;
    }
}