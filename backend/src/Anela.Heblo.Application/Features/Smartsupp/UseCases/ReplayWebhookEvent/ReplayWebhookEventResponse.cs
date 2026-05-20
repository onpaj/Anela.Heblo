using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

public class ReplayWebhookEventResponse : BaseResponse
{
    public int ReplayCount { get; set; }
    public DateTime? LastReplayedAt { get; set; }

    public ReplayWebhookEventResponse() { }
    public ReplayWebhookEventResponse(ErrorCodes errorCode) : base(errorCode) { }
}
