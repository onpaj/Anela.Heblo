using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.DisassembleGiftPackage;

public class DisassembleGiftPackageResponse : BaseResponse
{
    public GiftPackageDisassemblyDto Disassembly { get; set; } = null!;
}
