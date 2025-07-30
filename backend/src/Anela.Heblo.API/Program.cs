using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application;
using Anela.Heblo.Persistence;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
        builder.Services.AddSpaServices();

        // Controllers and API documentation
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Configure pipeline
        app.ConfigureApplicationPipeline();

        app.Run();
    }
}