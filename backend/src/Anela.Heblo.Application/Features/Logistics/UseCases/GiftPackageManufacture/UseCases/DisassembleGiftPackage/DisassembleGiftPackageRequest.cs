using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;

public class DisassembleGiftPackageRequest : IRequest<DisassembleGiftPackageResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public int Quantity { get; set; }
    public Guid UserId { get; set; }
}
