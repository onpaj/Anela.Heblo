using Anela.Heblo.API.Infrastructure;

namespace Anela.Heblo.API.Extensions;

public static class LoggingExtensions
{
    public static ILoggingBuilder ConfigureApplicationLogging(this ILoggingBuilder logging, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // Clear providers and add console logging for container stdout
        logging.ClearProviders();
        logging.AddConsole();

        // Configure logging levels from configuration
        logging.AddConfiguration(configuration.GetSection("Logging"));

        // Add Application Insights logging if configured
        var appInsightsConnectionString = configuration[InfrastructureConstants.APPLICATION_INSIGHTS_CONNECTION_STRING]
                                        ?? configuration[InfrastructureConstants.APPINSIGHTS_INSTRUMENTATION_KEY]
                                        ?? configuration[InfrastructureConstants.APPLICATIONINSIGHTS_CONNECTION_STRING];

        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            logging.AddApplicationInsights(
                configureTelemetryConfiguration: (config) => config.ConnectionString = appInsightsConnectionString,
                configureApplicationInsightsLoggerOptions: (options) => { }
            );
        }

        return logging;
    }
}