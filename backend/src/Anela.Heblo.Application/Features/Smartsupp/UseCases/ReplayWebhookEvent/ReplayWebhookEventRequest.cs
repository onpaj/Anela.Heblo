using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

public class ReplayWebhookEventRequest : IRequest<ReplayWebhookEventResponse>
{
    public Guid Id { get; set; }
    public string ReplayedBy { get; set; } = "";
}
