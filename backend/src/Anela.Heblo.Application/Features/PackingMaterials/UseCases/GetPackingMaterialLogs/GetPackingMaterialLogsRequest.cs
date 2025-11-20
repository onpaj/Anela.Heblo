using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsRequest : IRequest<GetPackingMaterialLogsResponse>
{
    public int PackingMaterialId { get; set; }
    public int Days { get; set; } = 60; // Default to 60 days
}