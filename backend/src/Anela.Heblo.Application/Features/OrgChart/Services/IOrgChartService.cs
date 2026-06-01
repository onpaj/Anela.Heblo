using Anela.Heblo.Application.Features.OrgChart.Contracts;

namespace Anela.Heblo.Application.Features.OrgChart.Services;

/// <summary>
/// Service for retrieving organizational chart data
/// </summary>
public interface IOrgChartService
{
    /// <summary>
    /// Retrieves the complete organizational structure from the configured data source
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Organization chart response containing all positions and employees</returns>
    Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default);
}
