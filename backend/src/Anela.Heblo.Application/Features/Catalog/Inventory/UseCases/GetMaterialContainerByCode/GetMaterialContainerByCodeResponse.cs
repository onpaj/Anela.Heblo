using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;

public class GetMaterialContainerByCodeResponse : BaseResponse
{
    public MaterialContainerDto Container { get; set; } = null!;
    public LotDto Lot { get; set; } = null!;

    public GetMaterialContainerByCodeResponse() : base() { }

    public GetMaterialContainerByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
