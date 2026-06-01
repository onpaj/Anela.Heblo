using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;

public class EnqueueGiftPackageManufactureRequest : IRequest<EnqueueGiftPackageManufactureResponse>
{
    public string GiftPackageCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool AllowStockOverride { get; set; } = false;
}