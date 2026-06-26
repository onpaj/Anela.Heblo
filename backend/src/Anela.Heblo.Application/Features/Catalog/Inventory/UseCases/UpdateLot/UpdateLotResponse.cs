using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;

public class UpdateLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;

    public UpdateLotResponse() : base() { }

    public UpdateLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
