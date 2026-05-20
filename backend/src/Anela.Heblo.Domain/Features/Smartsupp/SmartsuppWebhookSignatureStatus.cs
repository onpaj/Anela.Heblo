namespace Anela.Heblo.Domain.Features.Smartsupp;

public enum SmartsuppWebhookSignatureStatus
{
    Valid = 0,
    Missing = 1,
    Mismatch = 2,
    AppIdMismatch = 3,
}
