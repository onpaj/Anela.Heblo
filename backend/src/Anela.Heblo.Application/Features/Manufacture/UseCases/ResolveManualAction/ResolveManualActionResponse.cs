using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;

public class ResolveManualActionResponse : BaseResponse
{
    public ResolveManualActionResponse() : base() { }

    public ResolveManualActionResponse(ErrorCodes errorCode, Dictionary<string, string>? validationErrors = null)
        : base(errorCode, validationErrors) { }
}