using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public interface ICatalogResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}

public class CatalogResilienceService : ICatalogResilienceService
{
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<CatalogResilienceService> _logger;

    public CatalogResilienceService(ILogger<CatalogResilienceService> logger)
    {
        _logger = logger;
        _resiliencePipeline = CreateResiliencePipeline();
    }

    public async Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var context = ResilienceContextPool.Shared.Get(operationName);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async (ctx) =>
            {
                _logger.LogDebug("Executing {OperationName} with resilience patterns", operationName);
                return await operation(ctx.CancellationToken);
            }, context);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning("Circuit breaker is open for {OperationName}. External service may be down.", operationName);
            throw new InvalidOperationException($"External service for {operationName} is temporarily unavailable", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {OperationName} after all retry attempts", operationName);
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private ResiliencePipeline CreateResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            // Retry strategy: 3 attempts with exponential backoff
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<OperationCanceledException>(ex => ex.CancellationToken.IsCancellationRequested == false),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = Polly.DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning("Retrying operation. Attempt {AttemptNumber} of {MaxRetryAttempts}. Exception: {Exception}",
                        args.AttemptNumber + 1, 3, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            // Circuit breaker: Open after 3 consecutive failures, stay open for 30 seconds
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<OperationCanceledException>(ex => ex.CancellationToken.IsCancellationRequested == false),
                FailureRatio = 0.5, // Open circuit if 50% of requests fail
                MinimumThroughput = 3, // Need at least 3 requests before calculating failure ratio
                SamplingDuration = TimeSpan.FromMinutes(1), // Sample period for failure ratio
                BreakDuration = TimeSpan.FromSeconds(30), // How long to keep circuit open
                OnOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker opened due to {Exception}", args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker closed - service is healthy again");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation("Circuit breaker half-opened - testing service health");
                    return ValueTask.CompletedTask;
                }
            })
            // Timeout: 30 seconds per operation
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }
}