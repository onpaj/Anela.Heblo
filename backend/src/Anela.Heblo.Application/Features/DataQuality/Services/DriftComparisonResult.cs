namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftComparisonResult
{
    public IReadOnlyList<DriftMismatch> Mismatches { get; init; } = Array.Empty<DriftMismatch>();
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
