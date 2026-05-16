namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Classification of an outbound HTTP call failure for observability and alerting.
/// </summary>
public enum OutboundCallReason
{
    Unknown = 0,
    ClientAborted = 1,
    Timeout = 2,
    Network = 3,
}
