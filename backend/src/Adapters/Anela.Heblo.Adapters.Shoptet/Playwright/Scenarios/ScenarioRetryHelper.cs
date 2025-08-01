using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;

namespace Anela.Heblo.Adapters.Shoptet.Playwright.Scenarios;

public static class ScenarioRetryHelper
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        string scenarioName,
        int maxRetries = 3,
        int delayMilliseconds = 1000)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Executing {ScenarioName}, attempt {Attempt} of {MaxRetries}",
                    scenarioName, attempt, maxRetries);

                return await action();
            }
            catch (PlaywrightException ex) when (attempt < maxRetries && ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(ex, "TargetClosedException in {ScenarioName}, attempt {Attempt} of {MaxRetries}. Retrying...",
                    scenarioName, attempt, maxRetries);

                await Task.Delay(delayMilliseconds * attempt); // Exponential backoff
            }
            catch (TimeoutException ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "TimeoutException in {ScenarioName}, attempt {Attempt} of {MaxRetries}. Retrying...",
                    scenarioName, attempt, maxRetries);

                await Task.Delay(delayMilliseconds * attempt);
            }
            catch (PlaywrightException ex) when (attempt < maxRetries && IsRetriableError(ex))
            {
                logger.LogWarning(ex, "Retriable PlaywrightException in {ScenarioName}, attempt {Attempt} of {MaxRetries}. Retrying...",
                    scenarioName, attempt, maxRetries);

                await Task.Delay(delayMilliseconds * attempt);
            }
        }

        // This shouldn't be reached, but just in case
        throw new InvalidOperationException($"Failed to execute {scenarioName} after {maxRetries} attempts");
    }

    private static bool IsRetriableError(PlaywrightException ex)
    {
        var retriableMessages = new[]
        {
            "Target closed",
            "Target page, context or browser has been closed",
            "Protocol error",
            "Connection closed",
            "WebSocket is not open"
        };

        return retriableMessages.Any(msg => ex.Message.Contains(msg, StringComparison.OrdinalIgnoreCase));
    }
}