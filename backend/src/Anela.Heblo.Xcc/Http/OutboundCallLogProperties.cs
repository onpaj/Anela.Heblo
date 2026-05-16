namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Property names emitted on structured logs and Application Insights exception telemetry
/// for outbound HTTP call failures. PascalCase to match existing TelemetryService usage.
/// </summary>
public static class OutboundCallLogProperties
{
    public const string TargetHost = "TargetHost";
    public const string TargetPath = "TargetPath";
    public const string HttpMethod = "HttpMethod";
    public const string ElapsedMs = "ElapsedMs";
    public const string CancellationRequested = "CancellationRequested";
    public const string Reason = "Reason";
    public const string OperationId = "OperationId";
}
