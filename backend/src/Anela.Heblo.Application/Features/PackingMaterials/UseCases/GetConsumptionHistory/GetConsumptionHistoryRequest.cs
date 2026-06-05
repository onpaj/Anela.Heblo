using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryRequest : IRequest<GetConsumptionHistoryResponse>
{
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int? PackingMaterialId { get; set; }
    public ConsumptionType? ConsumptionType { get; set; }
    public string? ProductCode { get; set; }
    public string? InvoiceId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool SortDescending { get; set; } = true;
}
