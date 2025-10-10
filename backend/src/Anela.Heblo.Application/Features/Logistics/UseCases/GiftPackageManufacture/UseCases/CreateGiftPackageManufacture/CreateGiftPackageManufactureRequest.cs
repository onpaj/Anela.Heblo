using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureRequest : IRequest<CreateGiftPackageManufactureResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
    public bool AllowStockOverride { get; set; }
    public Guid UserId { get; set; }
}