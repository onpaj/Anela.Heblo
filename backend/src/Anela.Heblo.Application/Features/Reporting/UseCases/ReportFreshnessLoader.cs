using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Data;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Reporting.UseCases;

internal static class ReportFreshnessLoader
{
    /// <summary>Freshness is advisory: any failure degrades to UNKNOWN instead of failing the response.</summary>
    public static async Task<ReportFreshness> LoadAsync(
        IReportingDataReader reader, ReportSource source, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.GetFreshnessAsync(source, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not load reporting freshness for source {Source}; reporting UNKNOWN", source);
            return new ReportFreshness();
        }
    }
}
