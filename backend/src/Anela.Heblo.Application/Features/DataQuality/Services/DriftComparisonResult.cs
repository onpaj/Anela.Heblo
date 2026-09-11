namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftComparisonResult
{
    public IReadOnlyList<DriftMismatch> Mismatches { get; init; } = Array.Empty<DriftMismatch>();

    /// <summary>
    /// Observations a comparer wants persisted and visible in the run detail, but which are
    /// NOT failures and must not be counted in <c>DqtRun.TotalMismatches</c> (so they cannot
    /// turn a dashboard tile amber on their own). Defaults to empty — comparers that have no
    /// such notion are unaffected.
    /// </summary>
    public IReadOnlyList<DriftMismatch> Informational { get; init; } = Array.Empty<DriftMismatch>();

    public int TotalChecked { get; init; }
}

public class DriftMismatch
{
    public string EntityKey { get; init; } = string.Empty;
    public int MismatchCode { get; init; }
    public string? HebloValue { get; init; }
    public string? ShoptetValue { get; init; }
    public string? Details { get; init; }
}
