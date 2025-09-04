using Anela.Heblo.Domain.Features.Configuration;

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
        var appInsightsConnectionString = configuration[ConfigurationConstants.APPLICATION_INSIGHTS_CONNECTION_STRING]
                                        ?? configuration[ConfigurationConstants.APPINSIGHTS_INSTRUMENTATION_KEY]
                                        ?? configuration[ConfigurationConstants.APPLICATIONINSIGHTS_CONNECTION_STRING];

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