using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;

public class EnqueueGiftPackageManufactureRequest : IRequest<EnqueueGiftPackageManufactureResponse>
{
    public string GiftPackageCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool AllowStockOverride { get; set; } = false;
    public string RequestedByUserName { get; set; } = string.Empty;
}