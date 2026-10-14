using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;

public class SetCarrierLabelOffsetResponse : BaseResponse
{
    public SetCarrierLabelOffsetResponse()
    {
    }

    public SetCarrierLabelOffsetResponse(ErrorCodes errorCode)
        : base(errorCode)
    {
    }
}
