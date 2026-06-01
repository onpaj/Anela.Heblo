using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventResponse : BaseResponse
{
    public bool Handled { get; set; }
    public string? Reason { get; set; }

    public ProcessWebhookEventResponse() { }

    public ProcessWebhookEventResponse(ErrorCodes errorCode) : base(errorCode) { }
}
