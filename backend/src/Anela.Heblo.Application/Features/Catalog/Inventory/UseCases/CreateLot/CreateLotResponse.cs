using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;

    public CreateLotResponse() : base() { }

    public CreateLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
