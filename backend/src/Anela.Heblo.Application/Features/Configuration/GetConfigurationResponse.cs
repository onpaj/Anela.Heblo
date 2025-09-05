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
}