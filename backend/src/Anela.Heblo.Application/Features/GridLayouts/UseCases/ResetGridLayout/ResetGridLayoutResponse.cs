using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutResponse : BaseResponse
{
    public ResetGridLayoutResponse() : base() { }

    public ResetGridLayoutResponse(ErrorCodes errorCode) : base(errorCode) { }
}
