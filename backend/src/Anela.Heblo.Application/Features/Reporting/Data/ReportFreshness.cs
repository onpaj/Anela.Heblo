namespace Anela.Heblo.Application.Features.Reporting.Data;

public sealed class ReportFreshness
{
    public const string Ok = "OK";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";

    public DateTimeOffset? LastSyncAt { get; init; }
    public string Status { get; init; } = Unknown;
}

public sealed class ReportRows
{
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public required bool HasMore { get; init; }
}
