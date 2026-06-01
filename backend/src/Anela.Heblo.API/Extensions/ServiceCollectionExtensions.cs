using System.Reflection;
using Npgsql;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Anela.Heblo.API.HealthChecks.DataQuality;
using Microsoft.ApplicationInsights.Extensibility;
using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Telemetry;
using Anela.Heblo.API.Infrastructure.Telemetry;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.OpenApi.Models;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Xcc.Services;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.Cups;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.API.Features.ExpeditionList;
using Anela.Heblo.API.PDFPrints;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

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
            // Use optimized Application Insights configuration
            services.AddOptimizedApplicationInsights(configuration, environment);

            // Configure Cloud Role for environment identification
            services.Configure<TelemetryConfiguration>(telemetryConfig =>
            {
                var cloudRole = configuration["ApplicationInsights:CloudRole"] ?? "Heblo-API";
                var cloudRoleInstance = configuration["ApplicationInsights:CloudRoleInstance"] ?? environment.EnvironmentName;

                telemetryConfig.TelemetryInitializers.Add(new CloudRoleInitializer(cloudRole, cloudRoleInstance));
            });

            services.AddSingleton<ITelemetryService, TelemetryService>();
        }
        else
        {
            services.AddSingleton<ITelemetryService, NoOpTelemetryService>();
        }

        return services;
    }

    public static IServiceCollection AddCorsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection(ConfigurationConstants.CORS_ALLOWED_ORIGINS).Get<string[]>() ?? Array.Empty<string>();

        // Conductor parallel instances serve the frontend on a dynamically chosen port,
        // so the exact origin is unknown ahead of time. Under Conductor overrides, allow
        // any loopback origin instead of a fixed allow-list.
        var allowAnyLoopbackOrigin = configuration.GetValue<bool>("UseConductorOverrides");

        services.AddCors(options =>
        {
            options.AddPolicy(ConfigurationConstants.CORS_POLICY_NAME, policy =>
            {
                if (allowAnyLoopbackOrigin)
                {
                    policy.SetIsOriginAllowed(IsLoopbackOrigin);
                }
                else
                {
                    policy.WithOrigins(allowedOrigins);
                }

                policy.AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    private static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;

    public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck<Anela.Heblo.Application.Common.BackgroundServicesReadyHealthCheck>("background-services-ready", tags: new[] { "ready" })
            .AddCheck<DataQualitySchemaHealthCheck>(
                name: "data-quality-schema",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "ready", "db", "schema" });

        // Add database health check via the shared NpgsqlDataSource so the probe
        // reuses the application connection pool instead of opening a fresh connection
        // on every health-check probe (which caused TaskCanceledException spikes).
        var dbConnectionString = configuration.GetConnectionString(ConfigurationConstants.DEFAULT_CONNECTION);
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            healthChecksBuilder.AddNpgSql(
                sp => sp.GetRequiredService<NpgsqlDataSource>(),
                name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
                tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG });
        }

        return services;
    }

    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Register HttpClient for E2E testing middleware
        services.AddHttpClient();

        // Built-in HTTP request logging
        services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
            logging.RequestHeaders.Add("User-Agent");
            logging.RequestHeaders.Add("Authorization");
            logging.ResponseHeaders.Add("Content-Type");
            logging.MediaTypeOptions.AddText("application/json");
            logging.RequestBodyLogLimit = 4096;
            logging.ResponseBodyLogLimit = 4096;
        });
        services.AddHttpLoggingInterceptor<SuppressHealthHttpLogging>();

        // PDF renderer — lives in API layer because QuestPDF is not a dependency of Application layer
        services.AddScoped<IManufactureProtocolRenderer, QuestPdfManufactureProtocolRenderer>();
        services.AddScoped<ISemiproductRecipeRenderer, QuestPdfSemiproductRecipeRenderer>();

        // Note: Application services are now registered in vertical slice modules
        // This method is kept for backward compatibility and other cross-cutting concerns

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

    public static IServiceCollection AddSwaggerServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Anela Heblo API",
                Version = "v1",
                Description = "API for Anela Heblo cosmetics company workspace application"
            });

            // Only add authentication to Swagger if mock auth is disabled
            var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);

            if (!useMockAuth)
            {
                // Add Microsoft Entra ID OAuth2 authentication
                var azureAdConfig = configuration.GetSection("AzureAd");
                var tenantId = azureAdConfig["TenantId"];
                var clientId = azureAdConfig["ClientId"];
                var configuredScopes = azureAdConfig["Scopes"];

                if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId))
                {
                    // Build scopes dictionary
                    var scopes = new Dictionary<string, string>();

                    if (!string.IsNullOrEmpty(configuredScopes))
                    {
                        // Use configured scopes from appsettings
                        foreach (var scope in configuredScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var description = scope switch
                            {
                                "openid" => "OpenID Connect sign-in",
                                "profile" => "Access user profile information",
                                "User.Read" => "Read user profile",
                                _ when scope.StartsWith("api://") => "Access the API as the signed-in user",
                                _ => $"Access {scope}"
                            };
                            scopes[scope] = description;
                        }
                    }
                    else
                    {
                        // Fallback to API scope
                        scopes[$"api://{clientId}/access_as_user"] = "Access the API as the signed-in user";
                    }

                    options.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Description = "Authenticate using Microsoft Entra ID (Azure AD)",
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"),
                                TokenUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"),
                                Scopes = scopes
                            }
                        }
                    });

                    // Security requirement - OAuth2 only
                    // Determine required scopes for OAuth2 security requirement
                    var requiredScopes = new List<string>();
                    if (!string.IsNullOrEmpty(configuredScopes))
                    {
                        requiredScopes.AddRange(configuredScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    }
                    else
                    {
                        requiredScopes.Add($"api://{clientId}/access_as_user");
                    }

                    options.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "OAuth2"
                                }
                            },
                            requiredScopes.ToArray()
                        }
                    });
                }
            }
        });

        return services;
    }

    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var hangfireOptions = configuration.GetSection(HangfireOptions.ConfigurationKey).Get<HangfireOptions>();
        if (hangfireOptions == null)
        {
            throw new InvalidOperationException("Hangfire options not found in configuration");
        }

        // Configure Hangfire storage based on environment
        if (hangfireOptions.UseInMemoryStorage)
        {
            // Use in-memory storage for Test environment
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMemoryStorage());
        }
        else
        {
            // Use PostgreSQL storage for other environments
            // Try environment-specific connection string first, then fall back to DefaultConnection
            var connectionString = configuration.GetConnectionString(environment.EnvironmentName)
                                 ?? configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Database connection string is required for Hangfire in {environment.EnvironmentName} environment. Please configure either '{environment.EnvironmentName}' or 'DefaultConnection' connection string.");
            }

            // Cap Hangfire's connection pool independently from EF Core's pool
            var hangfireConnectionString = connectionString;
            var hangfireConnectionLimit = hangfireOptions.ConnectionLimit;
            if (hangfireConnectionLimit > 0)
            {
                var csb = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    MaxPoolSize = hangfireConnectionLimit
                };
                hangfireConnectionString = csb.ToString();
            }

            // Initialize Hangfire schema before configuring Hangfire
            using (var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole()))
            {
                var logger = loggerFactory.CreateLogger<HangfireSchemaInitializer>();
                var schemaInitializer = new HangfireSchemaInitializer(connectionString, logger);
                schemaInitializer.EnsureSchemaExistsAsync().GetAwaiter().GetResult();
            }

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options =>
                {
                    options.UseNpgsqlConnection(hangfireConnectionString);
                }, new PostgreSqlStorageOptions
                {
                    // Use isolated schema to avoid conflicts with other applications
                    SchemaName = hangfireOptions.SchemaName,
                    PrepareSchemaIfNecessary = true, // We handle schema creation manually
                    InvisibilityTimeout = TimeSpan.FromMinutes(30),
                    DistributedLockTimeout = TimeSpan.FromSeconds(10),
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    JobExpirationCheckInterval = TimeSpan.FromHours(1),
                    CountersAggregateInterval = TimeSpan.FromMinutes(5),
                }));
        }

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = hangfireOptions.WorkerCount;
        });

        // Register Hangfire dashboard authorization filter
        services.AddTransient<HangfireDashboardTokenAuthorizationFilter>();

        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Register Hangfire adapter implementations (interfaces live in Application,
        // concrete types live in API/Infrastructure/Hangfire — relocated to keep the
        // Application project free of Hangfire imports for these specific adapters).
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
        services.AddSingleton<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();

        // Note: IRecurringJobStatusChecker is now registered in Application layer (BackgroundJobsModule)

        // Defensive: ensure IMemoryCache is available for handlers that cache Hangfire responses.
        // AddMemoryCache is idempotent — safe to call even if another module already registered it.
        services.AddMemoryCache();

        // Register configuration options
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.ConfigurationKey));
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));

        return services;
    }

    /// <summary>
    /// Registers all IRecurringJob implementations from the Application assembly and the discovery service
    /// </summary>
    public static IServiceCollection AddRecurringJobs(this IServiceCollection services)
    {
        // Auto-discover all IRecurringJob implementations in Application assembly
        var applicationAssembly = Assembly.Load("Anela.Heblo.Application");
        var jobTypes = applicationAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IRecurringJob).IsAssignableFrom(t));

        foreach (var jobType in jobTypes)
        {
            // Register each job type as both IRecurringJob (for discovery) and as itself (for Hangfire)
            services.AddScoped(typeof(IRecurringJob), jobType);
            services.AddScoped(jobType);
        }

        // Register the discovery service that will register jobs with Hangfire on startup
        services.AddHostedService<RecurringJobDiscoveryService>();

        return services;
    }

    /// <summary>
    /// Registers the print queue sink based on the "ExpeditionList:PrintSink" configuration value.
    /// Valid values: "FileSystem" (default), "AzureBlob", "Cups", "Combined".
    /// </summary>
    public static IServiceCollection AddPrintQueueSink(this IServiceCollection services, IConfiguration configuration)
    {
        // The CUPS label-printing infrastructure (ILabelPrintingService) is always available —
        // it is used by MaterialContainer label printing regardless of the expedition print sink.
        services.AddCupsPrinting(configuration);

        var printSink = configuration["ExpeditionList:PrintSink"];
        switch (printSink)
        {
            case "AzureBlob":
                services.AddAzurePrintQueueSink(configuration);
                break;
            case "Cups":
                services.AddScoped<IPrintQueueSink, CupsPrintQueueSink>();
                services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                break;
            case "Combined":
                // AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect;
                // it is unused here — the last non-keyed registration (CombinedPrintQueueSink) wins.
                services.AddAzurePrintQueueSink(configuration);
                services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
                services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>();
                break;
            default: // "FileSystem" or unset
                services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Seeds default recurring job configurations from discovered IRecurringJob implementations
    /// Must be called after app.Build() to ensure DI container is ready
    /// </summary>
    public static async Task SeedRecurringJobConfigurationsAsync(this WebApplication app)
    {
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var repository = scope.ServiceProvider.GetRequiredService<IRecurringJobConfigurationRepository>();

                // Get all discovered IRecurringJob implementations
                var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();

                await repository.SeedDefaultConfigurationsAsync(discoveredJobs);
                logger.LogInformation("Successfully seeded default recurring job configurations from {Count} discovered jobs",
                    discoveredJobs.Count());
            }
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to seed recurring job configurations during startup");
            throw; // Fail application startup if seeding fails to ensure database is properly configured
        }
    }



}