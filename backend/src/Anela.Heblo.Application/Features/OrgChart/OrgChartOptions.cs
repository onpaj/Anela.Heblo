namespace Anela.Heblo.Application.Features.OrgChart;

/// <summary>
/// Configuration options for organizational chart data source
/// </summary>
public class OrgChartOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "OrgChart";

    /// <summary>
    /// URL to the organizational structure JSON data source
    /// </summary>
    public string DataSourceUrl { get; set; } = string.Empty;
}
