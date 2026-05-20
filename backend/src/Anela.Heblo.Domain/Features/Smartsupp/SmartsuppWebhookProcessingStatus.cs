namespace Anela.Heblo.Domain.Features.Smartsupp;

public enum SmartsuppWebhookProcessingStatus
{
    Processed,
    HandlerException,
    ParsingFailed,
    Skipped,
}
