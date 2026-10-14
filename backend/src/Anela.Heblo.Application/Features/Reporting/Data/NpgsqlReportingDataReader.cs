using System.Globalization;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting.Data;

/// <summary>
/// Runs reporting SQL on the shared data source with raw ADO.NET — deliberately not EF, whose
/// Polly execution strategy would retry a slow statement on the shared B1ms core. Every query runs
/// in a READ ONLY transaction with a local statement_timeout.
/// </summary>
public sealed class NpgsqlReportingDataReader : IReportingDataReader
{
    private static readonly TimeSpan DefaultStatementTimeout = TimeSpan.FromSeconds(10);
    private const string QueryCanceled = "57014";
    private const string UndefinedTable = "42P01";
    private const string InvalidSchema = "3F000";

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _timeoutMs;

    public NpgsqlReportingDataReader(NpgsqlDataSource dataSource, TimeSpan? statementTimeout = null)
    {
        _dataSource = dataSource;
        _timeoutMs = ((int)(statementTimeout ?? DefaultStatementTimeout).TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }

    public async Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await BeginReadOnlyAsync(connection, ct);

        await using var cmd = new NpgsqlCommand(command.Sql, connection, transaction);
        foreach (var p in command.Parameters)
            cmd.Parameters.Add(p.Clone());

        var rows = new List<IReadOnlyList<object?>>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(ReadRow(reader, command.OutputColumns));
        }
        catch (PostgresException ex) when (ex.SqlState == QueryCanceled)
        {
            throw new ReportQueryTimeoutException(ex);
        }

        await transaction.CommitAsync(ct);

        var hasMore = rows.Count > command.Limit;
        return new ReportRows { Rows = hasMore ? rows.Take(command.Limit).ToList() : rows, HasMore = hasMore };
    }

    public async Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await BeginReadOnlyAsync(connection, ct);
        await using var cmd = new NpgsqlCommand(ReportSources.FreshnessSqlFor(source), connection, transaction);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(1))
                return new ReportFreshness();

            return new ReportFreshness
            {
                LastSyncAt = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
                Status = reader.GetBoolean(1) ? ReportFreshness.Ok : ReportFreshness.Failed,
            };
        }
        catch (PostgresException ex) when (ex.SqlState is UndefinedTable or InvalidSchema)
        {
            return new ReportFreshness();
        }
    }

    private async Task<NpgsqlTransaction> BeginReadOnlyAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var transaction = await connection.BeginTransactionAsync(ct);
        await using var setup = new NpgsqlCommand(
            $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = {_timeoutMs}", connection, transaction);
        await setup.ExecuteNonQueryAsync(ct);
        return transaction;
    }

    private static IReadOnlyList<object?> ReadRow(NpgsqlDataReader reader, IReadOnlyList<ReportColumn> columns)
    {
        var row = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            row[i] = reader.IsDBNull(i) ? null : columns[i].Type switch
            {
                ReportColumnType.Date => reader.GetFieldValue<DateOnly>(i),
                ReportColumnType.Integer => Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture),
                ReportColumnType.Decimal => Convert.ToDecimal(reader.GetValue(i), CultureInfo.InvariantCulture),
                ReportColumnType.Boolean => reader.GetBoolean(i),
                _ => reader.GetValue(i).ToString(),
            };
        }
        return row;
    }
}
