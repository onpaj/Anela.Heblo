using System.Text.Json;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventRequest : IRequest<ProcessWebhookEventResponse>
{
    public string EventName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string AppId { get; set; } = "";
    public JsonElement Data { get; set; }
}
