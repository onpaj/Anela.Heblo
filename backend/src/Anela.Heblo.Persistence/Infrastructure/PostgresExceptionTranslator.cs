using Anela.Heblo.Domain.Features.GridLayouts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// Translates Npgsql/PostgreSQL exceptions surfaced by EF Core into the domain-layer
/// <see cref="GridLayoutPersistenceException"/>, keeping driver types out of the Application layer.
/// Distinct from <see cref="PostgresExceptionLoggingInterceptor"/>: the interceptor enriches save-failure
/// logs at the EF Core SaveChanges boundary (no operation context); this translator logs the Postgres
/// SqlState with the repository operation name and covers read paths the interceptor does not see.
/// </summary>
public class PostgresExceptionTranslator
{
    private readonly ILogger<PostgresExceptionTranslator> _logger;

    public PostgresExceptionTranslator(ILogger<PostgresExceptionTranslator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a <see cref="GridLayoutPersistenceException"/> wrapping <paramref name="exception"/> when
    /// the chain contains a <see cref="NpgsqlException"/> (which includes <see cref="PostgresException"/>),
    /// or <c>null</c> for anything else so the caller can rethrow unchanged. When a translation occurs,
    /// emits a single <see cref="LogLevel.Warning"/> entry with <c>SqlState</c>, <c>Operation</c>, and the
    /// underlying message — the Persistence boundary is the right place to log Postgres-specific state.
    /// </summary>
    public GridLayoutPersistenceException? TryTranslateGridLayout(Exception exception, string operation)
    {
        var npgsqlEx = FindNpgsqlException(exception);
        if (npgsqlEx is null)
        {
            return null;
        }

        var sqlState = (npgsqlEx as PostgresException)?.SqlState;
        _logger.LogWarning(
            "GridLayout persistence error during {Operation}: SqlState={SqlState} Message={Message}",
            operation, sqlState, exception.Message);

        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            exception);
    }

    private static NpgsqlException? FindNpgsqlException(Exception? exception)
    {
        return exception switch
        {
            null => null,
            NpgsqlException npg => npg,
            _ => FindNpgsqlException(exception.InnerException)
        };
    }
}
