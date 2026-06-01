using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;

public class DeleteLotResponse : BaseResponse
{
    public DeleteLotResponse() : base() { }

    public DeleteLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
