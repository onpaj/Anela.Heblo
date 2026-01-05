using System.Configuration;
using Microsoft.ApplicationInsights.Extensibility;
using Anela.Heblo.Xcc.Telemetry;
using Anela.Heblo.API.Infrastructure.Telemetry;
using Anela.Heblo.Application.Features.Users;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.OpenApi.Models;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;
using Anela.Heblo.API.Services;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Xcc.Services;

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

        services.AddCors(options =>
        {
            options.AddPolicy(ConfigurationConstants.CORS_POLICY_NAME, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddHealthCheckServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks()
            .AddCheck<Anela.Heblo.Application.Common.BackgroundServicesReadyHealthCheck>("background-services-ready", tags: new[] { "ready" });

        // Add database health check if connection string exists
        var dbConnectionString = configuration.GetConnectionString(ConfigurationConstants.DEFAULT_CONNECTION);
        if (!string.IsNullOrEmpty(dbConnectionString))
        {
            healthChecksBuilder.AddNpgSql(dbConnectionString,
                name: ConfigurationConstants.DATABASE_HEALTH_CHECK,
                tags: new[] { ConfigurationConstants.DB_TAG, ConfigurationConstants.POSTGRESQL_TAG });
        }

        return services;
    }

    public static IServiceCollection AddCrossCuttingServices(this IServiceCollection services)
    {
        // Register HttpContextAccessor for user service
        services.AddHttpContextAccessor();

        // Register TimeProvider
        services.AddSingleton(TimeProvider.System);

        // Register Current User Service
        services.AddSingleton<ICurrentUserService, CurrentUserService>();

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
            throw new ConfigurationErrorsException("Hangfire options not found");
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
                    options.UseNpgsqlConnection(connectionString);
                }, new PostgreSqlStorageOptions
                {
                    // Use isolated schema to avoid conflicts with other applications
                    SchemaName = hangfireOptions.SchemaName,
                    PrepareSchemaIfNecessary = true // We handle schema creation manually
                }));
        }

        // Always add Hangfire server with Heblo queue - NEVER allow fallback to Default queue
        services.AddHangfireServer(options =>
        {
            // Configure server options - ALWAYS only process Heblo queue
            options.WorkerCount = hangfireOptions.WorkerCount;
            options.Queues = new[] { hangfireOptions.QueueName };
        });

        // Only register job scheduler service in Production and Staging environments
        if (hangfireOptions.SchedulerEnabled)
        {
            services.AddHostedService<HangfireJobSchedulerService>();
        }

        // Register background job service (always available for manual execution via dashboard)
        services.AddTransient<HangfireBackgroundJobService>();

        // Register Hangfire dashboard authorization filter
        services.AddTransient<HangfireDashboardTokenAuthorizationFilter>();

        // Register IBackgroundWorker implementation
        services.AddTransient<IBackgroundWorker, HangfireBackgroundWorker>();

        // Register job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        // Register configuration options
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.ConfigurationKey));
        services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));

        return services;
    }
}