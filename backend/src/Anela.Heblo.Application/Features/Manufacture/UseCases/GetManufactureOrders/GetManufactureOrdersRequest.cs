using MediatR;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;

public class GetManufactureOrdersRequest : IRequest<GetManufactureOrdersResponse>
{
    public ManufactureOrderState? State { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? ResponsiblePerson { get; set; }
    public string? OrderNumber { get; set; }
    public string? ProductCode { get; set; }
    public string? ErpDocumentNumber { get; set; }
    public bool? ManualActionRequired { get; set; }
    public string? LotNumber { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}