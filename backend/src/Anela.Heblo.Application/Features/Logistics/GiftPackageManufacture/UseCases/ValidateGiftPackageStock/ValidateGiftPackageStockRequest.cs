using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.ValidateGiftPackageStock;

public class ValidateGiftPackageStockRequest : IRequest<ValidateGiftPackageStockResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
}