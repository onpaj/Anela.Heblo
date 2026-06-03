using Anela.Heblo.Domain.Features.GridLayouts;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure;

/// <summary>
/// Translates Npgsql/PostgreSQL exceptions surfaced by EF Core into the domain-layer
/// <see cref="GridLayoutPersistenceException"/>, keeping driver types out of the Application layer.
/// Distinct from <see cref="PostgresExceptionLoggingInterceptor"/>: the interceptor enriches save-failure
/// logs at the boundary; this helper produces a domain exception so handlers can catch a domain type.
/// </summary>
public static class PostgresExceptionTranslator
{
    /// <summary>
    /// Returns a <see cref="GridLayoutPersistenceException"/> wrapping <paramref name="exception"/> when
    /// the chain contains a <see cref="NpgsqlException"/> (which includes <see cref="PostgresException"/>),
    /// or <c>null</c> for anything else so the caller can rethrow unchanged.
    /// </summary>
    public static GridLayoutPersistenceException? TryTranslateGridLayout(Exception exception, string operation)
    {
        var npgsqlEx = FindNpgsqlException(exception);
        if (npgsqlEx is null)
        {
            return null;
        }

        var sqlState = (npgsqlEx as PostgresException)?.SqlState;
        return new GridLayoutPersistenceException(
            $"GridLayout persistence error during {operation}: {exception.Message}",
            sqlState,
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
