using Anela.Heblo.Adapters.Anthropic;
using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.SendGrid;
using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.MetaAds;
using Anela.Heblo.Adapters.Flexi;
using Anela.Heblo.Adapters.OpenAI;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.MCP;
using Anela.Heblo.Application;
using Anela.Heblo.Domain.Features.Invoices;
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
        builder.Services.AddXccServices(builder.Configuration); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
        builder.Services.AddSpaServices();

        // Adapters
        builder.Services.AddFlexiAdapter(builder.Configuration);
        builder.Services.AddShoptetPlaywrightAdapter(builder.Configuration);
        builder.Services.AddShoptetApiAdapter(builder.Configuration);
        builder.Services.AddShoptetPayAdapter(builder.Configuration);
        builder.Services.AddComgateAdapter(builder.Configuration);
        builder.Services.AddMetaAdsAdapter(builder.Configuration);
        builder.Services.AddGoogleAdsAdapter(builder.Configuration);
        builder.Services.AddAnthropicAdapter(builder.Configuration);
        builder.Services.AddOpenAiAdapter(builder.Configuration);
        builder.Services.AddSendGridAdapter(builder.Configuration);

        // Bind IIssuedInvoiceSource to the implementation selected by Invoices:Source config flag.
        // Valid values: "RestApi" | "Playwright" (default)
        var invoicesSource = builder.Configuration["Invoices:Source"] ?? "Playwright";
        if (string.Equals(invoicesSource, "RestApi", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IIssuedInvoiceSource>(
                sp => sp.GetRequiredService<ShoptetApiInvoiceSource>());
        }
        else
        {
            builder.Services.AddSingleton<IIssuedInvoiceSource>(
                sp => sp.GetRequiredService<ShoptetPlaywrightInvoiceSource>());
        }

        // Print queue sink — valid values: "FileSystem" (default), "AzureBlob", "Cups", "Combined"
        builder.Services.AddPrintQueueSink(builder.Configuration);

        // MCP server
        builder.Services.AddMcpServices();

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