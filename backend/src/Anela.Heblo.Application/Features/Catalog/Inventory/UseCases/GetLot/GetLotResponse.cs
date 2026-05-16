using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;

public class GetLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;
    public List<EanDto> Eans { get; set; } = new();

    public GetLotResponse() : base() { }

    public GetLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
