using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;

public class DiscardMaterialContainerResponse : BaseResponse
{
    public DiscardMaterialContainerResponse() : base() { }

    public DiscardMaterialContainerResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
