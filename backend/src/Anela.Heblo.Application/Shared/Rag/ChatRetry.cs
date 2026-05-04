using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Rag;

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
            logger.LogWarning(ex, "Transient error during chat operation, retrying once");
            await Task.Delay(delay ?? TimeSpan.FromSeconds(1), ct);

            try
            {
                return await operation();
            }
            catch (Exception retryEx)
            {
                throw new InvalidOperationException("Operation failed after retry.", retryEx);
            }
        }
    }
}
