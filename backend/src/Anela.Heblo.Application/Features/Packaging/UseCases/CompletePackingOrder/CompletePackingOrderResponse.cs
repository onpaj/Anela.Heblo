using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;

public class CompletePackingOrderResponse : BaseResponse
{
    public bool Completed { get; set; }

    public CompletePackingOrderResponse(bool completed)
    {
        Completed = completed;
    }

    public CompletePackingOrderResponse(ErrorCodes errorCode) : base(errorCode) { }
}
