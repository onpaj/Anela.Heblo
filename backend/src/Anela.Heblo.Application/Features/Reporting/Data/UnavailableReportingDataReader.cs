using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;

namespace Anela.Heblo.Application.Features.Reporting.Data;

/// <summary>Used when no PostgreSQL data source is registered (in-memory database mode).</summary>
public sealed class UnavailableReportingDataReader : IReportingDataReader
{
    private const string Message = "Reporting data is unavailable: no PostgreSQL data source is configured (in-memory database mode).";

    public Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct) =>
        throw new InvalidOperationException(Message);

    public Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct) =>
        Task.FromResult(new ReportFreshness());
}
