using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

public interface IManufactureErpResilienceService
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Circuit breaker around synchronous FlexiBee ERP calls (currently: manufacture submit).
/// Bounds blast radius when FlexiBee is degraded by failing fast instead of letting every
/// caller queue for the full per-call <see cref="ManufactureErpOptions.ErpTimeoutSeconds"/>
/// window. No retry and no inner timeout are added here: the existing app-level
/// CancelAfter timeout remains the only per-call time budget; this pipeline only decides
/// when to stop attempting calls at all.
/// </summary>
public class ManufactureErpResilienceService : IManufactureErpResilienceService
{
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<ManufactureErpResilienceService> _logger;

    public ManufactureErpResilienceService(
        IOptions<ManufactureErpOptions> options,
        TimeProvider timeProvider,
        ILogger<ManufactureErpResilienceService> logger)
    {
        _logger = logger;
        _resiliencePipeline = CreateResiliencePipeline(options.Value, timeProvider);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var context = ResilienceContextPool.Shared.Get(operationName, cancellationToken);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(
                async ctx => await operation(ctx.CancellationToken),
                context);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(
                "Circuit breaker is open for '{OperationName}'. FlexiBee ERP is repeatedly failing/timing out; failing fast.",
                operationName);
            throw new ManufactureErpUnavailableException(operationName, ex);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private ResiliencePipeline CreateResiliencePipeline(ManufactureErpOptions options, TimeProvider timeProvider)
    {
        return new ResiliencePipelineBuilder
        {
            TimeProvider = timeProvider
        }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // The caller (SubmitManufactureHandler) applies a per-call timeout via a
                // linked CancellationTokenSource.CancelAfter and passes the ORIGINAL,
                // un-linked cancellationToken here (not the linked/timeout token). That
                // means args.Context.CancellationToken (bound from that original token)
                // stays uncancelled when our own CancelAfter fires, and only becomes
                // cancelled when the actual caller cancels. This is what lets us tell
                // "our own timeout elapsed" (a failure to count) apart from "the caller
                // gave up" (not a dependency failure, don't count).
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception switch
                {
                    HttpRequestException => true,
                    OperationCanceledException => !args.Context.CancellationToken.IsCancellationRequested,
                    _ => false
                }),
                FailureRatio = options.ErpCircuitBreakerFailureRatio,
                MinimumThroughput = options.ErpCircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(options.ErpCircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(options.ErpCircuitBreakerBreakDurationSeconds),
                OnOpened = args =>
                {
                    _logger.LogWarning(
                        "Circuit breaker opened for '{OperationName}' due to {Exception}. FlexiBee ERP calls will fail fast for {BreakDurationSeconds}s.",
                        args.Context.OperationKey, args.Outcome.Exception?.Message, options.ErpCircuitBreakerBreakDurationSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker closed for '{OperationName}' - FlexiBee ERP is healthy again",
                        args.Context.OperationKey);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogInformation(
                        "Circuit breaker half-opened for '{OperationName}' - testing FlexiBee ERP health",
                        args.Context.OperationKey);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
