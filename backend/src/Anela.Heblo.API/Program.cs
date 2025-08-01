using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Adapters.Flexi;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application;
using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure application timezone based on configuration
        ConfigureApplicationTimeZone(builder.Configuration);

        // Configure logging
        builder.Logging.ConfigureApplicationLogging(builder.Configuration, builder.Environment);

        // Create a temporary logger for startup configuration
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var logger = loggerFactory.CreateLogger<Program>();

        // Configure services
        builder.Services.ConfigureAuthentication(builder, logger);
        builder.Services.AddApplicationInsightsServices(builder.Configuration, builder.Environment);
        builder.Services.AddCorsServices(builder.Configuration);
        builder.Services.AddHealthCheckServices(builder.Configuration);

        // Add new architecture services
        builder.Services.AddPersistenceServices(builder.Configuration);
        builder.Services.AddApplicationServices(); // Vertical slice modules from Application layer
        builder.Services.AddXccServices(); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
        builder.Services.AddSpaServices();

        // Adapters
        builder.Services.AddFlexiAdapter(builder.Configuration);
        builder.Services.AddShoptetAdapter(builder.Configuration);
        builder.Services.AddComgateAdapter(builder.Configuration);

        // Controllers and API documentation
        builder.Services.AddControllers();
        builder.Services.AddSwaggerServices(builder.Configuration);
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Configure pipeline
        app.ConfigureApplicationPipeline();

        app.Run();
    }

    private static void ConfigureApplicationTimeZone(IConfiguration configuration)
    {
        // Get timezone ID from configuration or environment variable
        var timeZoneId = Environment.GetEnvironmentVariable("TIMEZONE")
                        ?? configuration["Application:TimeZone"]
                        ?? "Central Europe Standard Time";

        // Set system timezone environment variable for consistent behavior
        var systemTimeZoneId = GetSystemTimeZoneId(timeZoneId);
        Environment.SetEnvironmentVariable("TZ", systemTimeZoneId);
        TimeZoneInfo.ClearCachedData(); // Clear any cached timezone data to force reload
    }

    private static string GetSystemTimeZoneId(string windowsTimeZoneId)
    {
        // Map Windows timezone IDs to IANA timezone IDs for cross-platform compatibility
        return windowsTimeZoneId switch
        {
            "Central Europe Standard Time" => "Europe/Prague",
            "Central European Standard Time" => "Europe/Prague",
            _ => "Europe/Prague" // Default fallback
        };
    }
}