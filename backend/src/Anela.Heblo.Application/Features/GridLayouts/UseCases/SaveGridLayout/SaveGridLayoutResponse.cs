using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutResponse : BaseResponse
{
    public SaveGridLayoutResponse() : base() { }

    public SaveGridLayoutResponse(ErrorCodes errorCode) : base(errorCode) { }
}
