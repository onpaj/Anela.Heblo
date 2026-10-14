using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;

/// <summary>Turns a report with failures into the exception Hangfire persists; capped, every error is already logged.</summary>
internal static class AdsSyncJobFailure
{
    private const int MaxReportedErrors = 10;

    public static InvalidOperationException From(string jobName, AdsSyncReport report)
    {
        var shown = string.Join(" | ", report.Errors.Take(MaxReportedErrors));
        var more = report.Errors.Count > MaxReportedErrors ? $" | (+{report.Errors.Count - MaxReportedErrors} more)" : "";
        return new InvalidOperationException($"{jobName}: {report.Errors.Count} stream(s) failed: {shown}{more}");
    }
}
