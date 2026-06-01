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
    /// Microsoft Entra group ID used for manufacture responsible-person lookups.
    /// Null when the "ManufactureGroupId" configuration key is missing or empty.
    /// Note: ConfigurationController is intentionally anonymous (no [Authorize]) so
    /// the SPA can fetch version and useMockAuth before sign-in. ManufactureGroupId
    /// is a non-sensitive Entra group identifier and is acceptable to expose anonymously.
    /// </summary>
    public string? ManufactureGroupId { get; set; }
}
