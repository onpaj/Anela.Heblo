using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemResponse : BaseResponse
{
    public ManufacturedProductInventoryItemDto? Item { get; set; }
}
