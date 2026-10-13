using Anela.Heblo.Application.Features.Reporting.Catalog;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting.Query;

public sealed class ReportSqlCommand
{
    public required string Sql { get; init; }
    public required IReadOnlyList<NpgsqlParameter> Parameters { get; init; }
    public required IReadOnlyList<ReportColumn> OutputColumns { get; init; }

    /// <summary>Rows the caller asked for; the SQL fetches one more to detect a further page.</summary>
    public required int Limit { get; init; }
}
