namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdsSyncReport(bool Skipped, string? SkipReason, int SourcesAttempted, int RowsWritten, IReadOnlyList<string> Errors)
{
    public bool HasFailures => Errors.Count > 0;

    public static AdsSyncReport Skip(string reason) => new(true, reason, 0, 0, Array.Empty<string>());
}
