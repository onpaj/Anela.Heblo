using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Anela.Heblo.Adapters.Anthropic;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.HomeAssistant;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.OpenMeteo;
using Anela.Heblo.Adapters.Plaud;
using Anela.Heblo.Adapters.SendGrid;
using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.MetaAds;
using Anela.Heblo.Adapters.Flexi;
using Anela.Heblo.Adapters.OpenAI;
using Anela.Heblo.Adapters.WebSearch;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Features.Users;
using Anela.Heblo.API.MCP;
using Anela.Heblo.API.Webhooks.Smartsupp;
using Anela.Heblo.Application;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.API;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        // Server APIs must not depend on the OS locale for number/date formatting.
        // The FlexiBee SDK (and other third-party XML/JSON serializers) call decimal.ToString()
        // without explicit culture, which produces "1,000" instead of "1.000" on Czech OS.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        var builder = WebApplication.CreateBuilder(args);

        // Azure Key Vault: activated when KeyVault:Uri is set (in Azure App Settings).
        // Added BEFORE user-secrets and command-line so those can always override KV values.
        // Local dev leaves KeyVault:Uri unset and continues using user-secrets / appsettings.json.
        var keyVaultUri = builder.Configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential(),
                new AzureKeyVaultConfigurationOptions
                {
                    ReloadInterval = TimeSpan.FromMinutes(30)
                });
        }

        // User secrets and command-line args are layered on top of KV so they always win.
        // Re-adding command-line here ensures it outranks KV (CreateBuilder adds it earlier).
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test") || builder.Environment.IsEnvironment("Staging") || builder.Environment.IsProduction())
        {
            builder.Configuration.AddUserSecrets<Program>();
        }
        builder.Configuration.AddCommandLine(args);

        // Conductor parallel-instance overrides (see appsettings.Conductor.json).
        // Layered on top of the active environment so ephemeral local instances never
        // hydrate from live external systems. Enabled by scripts/conductor-run.sh.
        if (builder.Configuration.GetValue<bool>("UseConductorOverrides"))
        {
            builder.Configuration.AddJsonFile("appsettings.Conductor.json", optional: false, reloadOnChange: true);
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
        builder.Services.AddScoped<ISmartsuppWebhookMetrics, SmartsuppWebhookMetrics>();
        builder.Services.AddXccServices(builder.Configuration); // Cross-cutting concerns (audit, telemetry, etc.)
        builder.Services.AddUsersModule(); // Users feature composition root (API-layer adapter for ICurrentUserService)
        builder.Services.AddCrossCuttingServices(); // Cross-cutting services from API layer
        builder.Services.AddSpaServices();

        // Adapters
        builder.Services.AddFlexiAdapter(builder.Configuration);
        builder.Services.AddShoptetCsvAdapter(builder.Configuration);
        builder.Services.AddShoptetApiAdapter(builder.Configuration);
        builder.Services.AddShoptetPayAdapter(builder.Configuration);
        builder.Services.AddComgateAdapter(builder.Configuration);
        builder.Services.AddMetaAdsAdapter(builder.Configuration);
        builder.Services.AddGoogleAdsAdapter(builder.Configuration);
        builder.Services.AddAnthropicAdapter(builder.Configuration);
        builder.Services.AddSmartsuppAdapter(builder.Configuration);
        builder.Services.AddOpenAiAdapter(builder.Configuration);
        builder.Services.AddWebSearchAdapter(builder.Configuration);
        builder.Services.AddSendGridAdapter(builder.Configuration);
        builder.Services.AddHomeAssistantAdapter(builder.Configuration);
        builder.Services.AddOpenMeteoAdapter(builder.Configuration);
        builder.Services.AddPlaudAdapter(builder.Configuration);
        builder.Services.AddMicrosoft365Adapter(builder.Configuration);

        builder.Services.AddSingleton<IIssuedInvoiceSource>(sp => sp.GetRequiredService<ShoptetApiInvoiceSource>());

        // Print queue sink — valid values: "FileSystem" (default), "AzureBlob", "Cups", "Combined"
        builder.Services.AddPrintQueueSink(builder.Configuration);

        // MCP server
        builder.Services.AddMcpServices();

        // Request timeouts — required for .WithRequestTimeout() on MCP and other endpoints
        builder.Services.AddRequestTimeouts();

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

        await app.InitializeFeatureFlagsAsync();

        // Initialize tile registry with all registered tiles
        app.InitializeTileRegistry();

        await app.MigrateDatabaseAsync();

        // Seed default recurring job configurations from discovered IRecurringJob implementations.
        // Runs before pipeline configuration and Hangfire startup to guarantee job configurations exist before recurring jobs start.
        await app.SeedRecurringJobConfigurationsAsync();

        // Configure pipeline
        app.ConfigureApplicationPipeline();

        app.Run();
    }

}