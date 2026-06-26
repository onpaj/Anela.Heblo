using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public static class TransientErrorClassifier
{
    private const string ConnectionExceptionPrefix = "08";
    private static readonly HashSet<string> TransientOperatorIntervention = new()
    {
        "57P01",
        "57P02",
        "57P03",
    };

    private static readonly HashSet<string> NonTransientLogicalCodes = new()
    {
        "23505",
        "23503",
        "23502",
    };

    public static bool IsTransient(Exception exception)
    {
        if (IsNonTransientLogical(exception))
        {
            return false;
        }

        return IsTransientCore(exception);
    }

    public static bool IsNonTransientLogical(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        var pg = UnwrapPostgresException(exception);
        return pg is not null && NonTransientLogicalCodes.Contains(pg.SqlState);
    }

    private static bool IsTransientCore(Exception exception)
    {
        return exception switch
        {
            PostgresException pg => IsTransientSqlState(pg.SqlState),
            SocketException => true,
            TimeoutException => true,
            IOException => true,
            { InnerException: { } inner } => IsTransientCore(inner),
            _ => false,
        };
    }

    private static bool IsTransientSqlState(string sqlState)
    {
        if (string.IsNullOrEmpty(sqlState))
        {
            return false;
        }

        return TransientOperatorIntervention.Contains(sqlState)
            || sqlState.StartsWith(ConnectionExceptionPrefix, StringComparison.Ordinal);
    }

    private static PostgresException? UnwrapPostgresException(Exception? exception)
    {
        return exception switch
        {
            null => null,
            PostgresException pg => pg,
            { InnerException: { } inner } => UnwrapPostgresException(inner),
            _ => null,
        };
    }
}
