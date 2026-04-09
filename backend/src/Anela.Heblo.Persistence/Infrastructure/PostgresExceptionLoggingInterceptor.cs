using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// EF Core interceptor that enriches exception logs with the Postgres SqlState error code.
/// Without this, DbUpdateException bubbles up with no indication of which constraint was violated
/// (e.g. 23505 = unique_violation, 23503 = foreign_key_violation), making it impossible to
/// distinguish transient from application-logic errors in Application Insights.
/// </summary>
public class PostgresExceptionLoggingInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<PostgresExceptionLoggingInterceptor> _logger;

    public PostgresExceptionLoggingInterceptor(ILogger<PostgresExceptionLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        LogPostgresException(eventData.Exception);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogPostgresException(eventData.Exception);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void LogPostgresException(Exception exception)
    {
        var pgEx = UnwrapPostgresException(exception);
        if (pgEx is null)
            return;

        _logger.LogError(
            exception,
            "PostgresException SqlState={SqlState} Severity={Severity} MessageText={MessageText} Detail={Detail} TableName={TableName} ConstraintName={ConstraintName}",
            pgEx.SqlState,
            pgEx.Severity,
            pgEx.MessageText,
            pgEx.Detail,
            pgEx.TableName,
            pgEx.ConstraintName);
    }

    private static PostgresException? UnwrapPostgresException(Exception? exception)
    {
        return exception switch
        {
            PostgresException pg => pg,
            NpgsqlException { InnerException: PostgresException pg } => pg,
            { InnerException: not null } => UnwrapPostgresException(exception.InnerException),
            _ => null
        };
    }
}
