using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;

public class DeleteEanResponse : BaseResponse
{
    public DeleteEanResponse() : base() { }

    public DeleteEanResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
