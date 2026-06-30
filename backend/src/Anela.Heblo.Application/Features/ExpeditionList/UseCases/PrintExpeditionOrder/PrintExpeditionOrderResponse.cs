using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;

public class PrintExpeditionOrderResponse : BaseResponse
{
    public PrintExpeditionOrderResponse() { }

    public PrintExpeditionOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params) { }
}
