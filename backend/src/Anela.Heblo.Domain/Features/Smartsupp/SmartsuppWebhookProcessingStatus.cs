namespace Anela.Heblo.Domain.Features.Smartsupp;

public enum SmartsuppWebhookProcessingStatus
{
    NotProcessed = 0,
    MalformedJson = 1,
    Success = 2,
    HandlerException = 3,
}
