using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialResponse : BaseResponse
{
    public string? LotCode { get; set; }

    public GetLastUsedLotForMaterialResponse() : base() { }

    public GetLastUsedLotForMaterialResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
