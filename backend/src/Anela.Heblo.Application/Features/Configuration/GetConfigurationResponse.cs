using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Configuration;

/// <summary>
/// Response containing application configuration information
/// </summary>
public class GetConfigurationResponse : BaseResponse
{
    /// <summary>
    /// Application version from CI/CD pipeline or assembly
    /// </summary>
    public string Version { get; set; } = default!;

    /// <summary>
    /// Current environment (Development, Test, Production)
    /// </summary>
    public string Environment { get; set; } = default!;

    /// <summary>
    /// Whether mock authentication is enabled
    /// </summary>
    public bool UseMockAuth { get; set; }

    /// <summary>
    /// Response timestamp in UTC
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// API base URL for the frontend
    /// </summary>
    public string ApiUrl { get; set; } = default!;

    /// <summary>
    /// Azure AD Client ID for authentication
    /// </summary>
    public string AzureClientId { get; set; } = default!;

    /// <summary>
    /// Azure AD Authority URL
    /// </summary>
    public string AzureAuthority { get; set; } = default!;

    /// <summary>
    /// Azure AD Tenant ID
    /// </summary>
    public string AzureTenantId { get; set; } = default!;
}