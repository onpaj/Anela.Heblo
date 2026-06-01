using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemResponse : BaseResponse
{
    public ManufacturedProductInventoryItemDto? Item { get; set; }
}
