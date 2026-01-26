namespace Anela.Heblo.Domain.Features.Configuration;

/// <summary>
/// Domain model representing application configuration
/// </summary>
public class ApplicationConfiguration
{
    public string Version { get; private set; }
    public string Environment { get; private set; }
    public bool UseMockAuth { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string ApiUrl { get; private set; }
    public string AzureClientId { get; private set; }
    public string AzureAuthority { get; private set; }
    public string AzureTenantId { get; private set; }

    public ApplicationConfiguration(
        string version,
        string environment,
        bool useMockAuth,
        string apiUrl,
        string azureClientId,
        string azureAuthority,
        string azureTenantId)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        UseMockAuth = useMockAuth;
        ApiUrl = apiUrl ?? string.Empty;
        AzureClientId = azureClientId ?? string.Empty;
        AzureAuthority = azureAuthority ?? string.Empty;
        AzureTenantId = azureTenantId ?? string.Empty;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates configuration with fallback values
    /// </summary>
    public static ApplicationConfiguration CreateWithDefaults(
        string? version,
        string? environment,
        bool useMockAuth,
        string? apiUrl,
        string? azureClientId,
        string? azureAuthority,
        string? azureTenantId)
    {
        return new ApplicationConfiguration(
            version ?? "1.0.0",
            environment ?? "Production",
            useMockAuth,
            apiUrl ?? string.Empty,
            azureClientId ?? string.Empty,
            azureAuthority ?? string.Empty,
            azureTenantId ?? string.Empty
        );
    }
}