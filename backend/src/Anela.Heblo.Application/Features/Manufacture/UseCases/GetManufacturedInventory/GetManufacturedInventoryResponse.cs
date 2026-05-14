using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryResponse : BaseResponse
{
    public List<ManufacturedProductInventoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
}
