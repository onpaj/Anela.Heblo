using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;

namespace Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;

/// <summary>
/// Handler for retrieving the complete organizational structure
/// </summary>
public class GetOrganizationStructureHandler : IRequestHandler<GetOrganizationStructureRequest, OrgChartResponse>
{
    private readonly IOrgChartService _orgChartService;
    private readonly ILogger<GetOrganizationStructureHandler> _logger;

    public GetOrganizationStructureHandler(
        IOrgChartService orgChartService,
        ILogger<GetOrganizationStructureHandler> logger)
    {
        _orgChartService = orgChartService;
        _logger = logger;
    }

    public async Task<OrgChartResponse> Handle(GetOrganizationStructureRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling request to fetch organizational structure");

        try
        {
            var result = await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching organizational structure");
            throw;
        }
    }
}