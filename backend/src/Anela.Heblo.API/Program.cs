using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Adapters.Flexi;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application;
using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc;

namespace Anela.Heblo.API;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure application timezone based on configuration
        builder.Configuration.ConfigureApplicationTimeZone();

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
        builder.Services.AddApplicationServices(builder.Configuration, builder.Environment); // Vertical slice modules from Application layer
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

}