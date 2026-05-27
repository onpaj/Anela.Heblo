namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public interface ISmartsuppWebhookMetrics
{
    void RecordReceived(string eventName, string outcome, double durationMs);
    void RecordSignatureFailure(string reason);
    void RecordPayloadBytes(int bytes);
}
