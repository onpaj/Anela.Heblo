using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Http;

public static class ChatRetry
{
    public static async Task<T> RetryOnceAsync<T>(
        Func<Task<T>> operation,
        ILogger logger,
        CancellationToken ct,
        TimeSpan? delay = null)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
        {
            logger.LogWarning(ex, "Transient error, retrying once after {Delay}ms",
                (int)(delay ?? TimeSpan.FromSeconds(1)).TotalMilliseconds);
            await Task.Delay(delay ?? TimeSpan.FromSeconds(1), ct);
        }

        // Retry outside catch — OperationCanceledException propagates naturally here
        return await operation();
    }
}
