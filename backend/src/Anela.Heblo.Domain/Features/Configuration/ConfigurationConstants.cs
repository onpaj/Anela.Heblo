namespace Anela.Heblo.Domain.Features.Configuration;

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
}
