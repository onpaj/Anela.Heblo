using System.Net.Sockets;
using Polly.Retry;
using Polly.Timeout;

namespace Anela.Heblo.Adapters.HomeAssistant.Resilience;

/// <summary>
/// Transient error predicate for HomeAssistant HTTP resilience policies.
/// Determines which failures should be retried and which should fail fast.
/// Caller cancellation is always respected — never retried when caller requests cancellation.
/// </summary>
internal static class HomeAssistantTransientErrorPredicate
{
    /// <summary>
    /// Determines whether an exception or result is transient and should trigger a retry.
    /// Used in retry strategy ShouldHandle predicates.
    /// </summary>
    public static bool IsTransient(Exception? exception, object? result)
    {
        if (exception is null && result is HttpResponseMessage response)
        {
            return (int)response.StatusCode >= 500;
        }

        return exception switch
        {
            IOException => true,
            SocketException => true,
            HttpRequestException => true,
            TimeoutException => true,
            // Polly's TimeoutStrategy raises this on a per-attempt timeout.
            TimeoutRejectedException => true,
            // Per-attempt cancellation triggered by Polly's timeout strategy is internal — retry.
            OperationCanceledException oce when oce.CancellationToken == default => true,
            _ => false,
        };
    }
}
