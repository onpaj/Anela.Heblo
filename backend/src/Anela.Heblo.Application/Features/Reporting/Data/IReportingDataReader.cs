using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;

namespace Anela.Heblo.Application.Features.Reporting.Data;

public interface IReportingDataReader
{
    /// <summary>Runs a built query; throws <see cref="ReportQueryTimeoutException"/> when the statement timeout fires.</summary>
    Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct);

    Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct);
}
