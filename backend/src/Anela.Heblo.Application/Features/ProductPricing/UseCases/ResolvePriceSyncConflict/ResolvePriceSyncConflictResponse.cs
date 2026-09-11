using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictResponse : BaseResponse
{
    public ResolvePriceSyncConflictResponse() { }

    public ResolvePriceSyncConflictResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
