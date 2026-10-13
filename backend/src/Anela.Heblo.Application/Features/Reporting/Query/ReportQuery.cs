namespace Anela.Heblo.Application.Features.Reporting.Query;

public sealed class ReportFilter
{
    public required string Column { get; init; }
    public required string Op { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string>? Values { get; init; }
}

public sealed class ReportSort
{
    public required string Column { get; init; }
    public string Direction { get; init; } = "asc";
}

public sealed class ReportQuery
{
    public IReadOnlyList<string>? Columns { get; init; }
    public IReadOnlyList<ReportFilter> Filters { get; init; } = [];
    public IReadOnlyList<ReportSort> Sort { get; init; } = [];
    public int? Limit { get; init; }
    public int? Offset { get; init; }
}
