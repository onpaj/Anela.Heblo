namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Configuration options for Manufacture ERP integration (FlexiBee).
/// </summary>
public class ManufactureErpOptions
{
    /// <summary>
    /// Maximum number of seconds to wait for a single Flexi ERP call before timing out.
    /// Defaults to 60 seconds. Set to 0 to disable the application-level timeout.
    /// </summary>
    public int ErpTimeoutSeconds { get; set; } = 60;
}
