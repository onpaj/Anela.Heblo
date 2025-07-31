namespace Anela.Heblo.Application.Features.Configuration.Domain;

/// <summary>
/// Configuration constants and keys
/// </summary>
public static class ConfigurationConstants
{
    // Environment variable keys
    public const string APP_VERSION = "APP_VERSION";
    public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";

    // Configuration keys
    public const string USE_MOCK_AUTH = "UseMockAuth";
    public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";

    // Default values
    public const string DEFAULT_VERSION = "1.0.0";
    public const string DEFAULT_ENVIRONMENT = "Production";

    // Application Insights configuration keys
    public const string APPLICATION_INSIGHTS_CONNECTION_STRING = "ApplicationInsights:ConnectionString";
    public const string APPINSIGHTS_INSTRUMENTATION_KEY = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public const string APPLICATIONINSIGHTS_CONNECTION_STRING = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    // Database configuration keys
    public const string DEFAULT_CONNECTION = "DefaultConnection";

    // CORS configuration keys
    public const string CORS_ALLOWED_ORIGINS = "Cors:AllowedOrigins";

    // Policy names
    public const string CORS_POLICY_NAME = "AllowFrontend";
    public const string MOCK_AUTH_SCHEME = "Mock";

    // Health check tags
    public const string DB_TAG = "db";
    public const string POSTGRESQL_TAG = "postgresql";

    // Health check names
    public const string DATABASE_HEALTH_CHECK = "database";
}