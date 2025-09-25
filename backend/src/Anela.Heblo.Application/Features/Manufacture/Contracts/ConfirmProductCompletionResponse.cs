using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmProductCompletionResponse : BaseResponse
{
    public string? Message { get; set; }

    public ConfirmProductCompletionResponse() : base() { }

    public ConfirmProductCompletionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}