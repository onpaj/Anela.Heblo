using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryRequest : IRequest<GetManufacturedInventoryResponse>
{
    public string? Search { get; set; }
    public bool OnlyWithStock { get; set; }
    public int? ManufactureOrderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
