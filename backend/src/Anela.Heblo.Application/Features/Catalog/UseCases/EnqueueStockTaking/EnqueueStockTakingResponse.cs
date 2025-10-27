using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.EnqueueStockTaking;

public class EnqueueStockTakingResponse : BaseResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public EnqueueStockTakingResponse() : base() { }

    public EnqueueStockTakingResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}