using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Adapters.Flexi;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application;
using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.API;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add User Secrets for Development, Test, Staging, and Production environments
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") || builder.Environment.IsEnvironment("Staging") || builder.Environment.IsProduction())
        {
            builder.Configuration.AddUserSecrets<Program>();
        }

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
        builder.Services.AddPersistenceServices(builder.Configuration, builder.Environment);
        builder.Services.AddApplicationServices(builder.Configuration, builder.Environment); // Vertical slice modules from Application layer
        builder.Services.AddXccServices(); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
        builder.Services.AddSpaServices();

        // Adapters
        builder.Services.AddFlexiAdapter(builder.Configuration);
        builder.Services.AddShoptetAdapter(builder.Configuration);
        builder.Services.AddComgateAdapter(builder.Configuration);

        // Hangfire background jobs
        builder.Services.AddHangfireServices(builder.Configuration, builder.Environment);
        builder.Services.AddRecurringJobs(); // Register all IRecurringJob implementations and discovery service

        // Controllers and API documentation
        builder.Services.AddControllers(options =>
            {
                // Register URL mapping model binder provider
                options.ModelBinderProviders.Insert(0, new Anela.Heblo.API.Infrastructure.UrlMappingModelBinderProvider());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });
        builder.Services.AddSwaggerServices(builder.Configuration);
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Initialize tile registry with all registered tiles
        app.InitializeTileRegistry();

        // Seed default recurring job configurations from discovered IRecurringJob implementations
        // Note: Database creation and migrations are handled automatically by EF Core during first connection
        // This seeding runs after app.Build() to ensure the DI container is ready, but before pipeline
        // configuration and Hangfire startup. This guarantees job configurations exist before recurring jobs start.
        await app.SeedRecurringJobConfigurationsAsync();

        // Configure pipeline
        app.ConfigureApplicationPipeline();

        app.Run();
    }

}