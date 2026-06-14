using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

/// <summary>
/// EF Core execution strategy delegating to the singleton Polly ResiliencePipeline.
/// EF Core resets its change tracker before each retry so transient mid-call failures replay safely.
/// EnableRetryOnFailure must not be used alongside this strategy — there is exactly one retry layer.
/// </summary>
public sealed class PollyExecutionStrategy : IExecutionStrategy
{
    private readonly ExecutionStrategyDependencies _dependencies;
    private readonly IDbResiliencePipelineProvider _pipelineProvider;
    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<PollyExecutionStrategy> _logger;

    public PollyExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        IDbResiliencePipelineProvider pipelineProvider,
        DbResilienceMetrics metrics,
        ILogger<PollyExecutionStrategy> logger)
    {
        _dependencies = dependencies;
        _pipelineProvider = pipelineProvider;
        _metrics = metrics;
        _logger = logger;
    }

    public bool RetriesOnFailure => true;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        var attempt = 0;
        try
        {
            var result = _pipelineProvider.Pipeline.Execute(_ =>
            {
                attempt++;
                return operation(_dependencies.CurrentContext.Context, state);
            });

            if (attempt > 1)
            {
                _metrics.RecordRetrySuccess(attempt);
            }

            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordRetryFailure(ex.GetType().FullName ?? "unknown", attempt);
            _logger.LogError(
                ex,
                "DbTransientRetryExhausted attempts={Attempts} exception.type={ExceptionType}",
                attempt,
                ex.GetType().FullName);
            throw;
        }
    }

    public async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            var result = await _pipelineProvider.Pipeline.ExecuteAsync(async ct =>
            {
                attempt++;
                return await operation(_dependencies.CurrentContext.Context, state, ct);
            }, cancellationToken);

            if (attempt > 1)
            {
                _metrics.RecordRetrySuccess(attempt);
            }

            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordRetryFailure(ex.GetType().FullName ?? "unknown", attempt);
            _logger.LogError(
                ex,
                "DbTransientRetryExhausted attempts={Attempts} exception.type={ExceptionType}",
                attempt,
                ex.GetType().FullName);
            throw;
        }
    }
}
