using MediatR;
using Anela.Heblo.Application.Features.OrgChart.Contracts;

namespace Anela.Heblo.Application.Features.OrgChart.UseCases.GetOrganizationStructure;

/// <summary>
/// Request for retrieving the complete organizational structure
/// </summary>
public class GetOrganizationStructureRequest : IRequest<OrgChartResponse>
{
}