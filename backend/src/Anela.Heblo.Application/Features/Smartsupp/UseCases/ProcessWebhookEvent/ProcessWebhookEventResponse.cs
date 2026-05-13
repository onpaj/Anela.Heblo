namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventResponse
{
    public bool Handled { get; set; }
    public string? Reason { get; set; }
}
