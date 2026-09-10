using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;

public class RunDqtHandler : IRequestHandler<RunDqtRequest, RunDqtResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RunDqtHandler> _logger;

    public RunDqtHandler(
        IDqtRunRepository repository,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RunDqtHandler> logger)
    {
        _repository = repository;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RunDqtResponse> Handle(RunDqtRequest request, CancellationToken cancellationToken)
    {
        if (request.DateFrom > request.DateTo)
        {
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.DqtInvalidDateRange
            };
        }

        try
        {
            using (var validationScope = _scopeFactory.CreateScope())
            {
                var hasRunner = validationScope.ServiceProvider
                    .GetServices<IDqtJobRunner>()
                    .Any(r => r.CanHandle(request.TestType));

                if (!hasRunner)
                {
                    return new RunDqtResponse
                    {
                        Success = false,
                        ErrorCode = ErrorCodes.DqtUnsupportedTestType
                    };
                }
            }

            var run = DqtRun.Start(request.TestType, request.DateFrom, request.DateTo, DqtTriggerType.Manual, _timeProvider.GetUtcNow().DateTime);
            await _repository.AddAsync(run, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Fire-and-forget in a dedicated scope — the HTTP request scope is disposed
            // before RunAsync completes, so capturing _jobRunner directly would cause
            // ObjectDisposedException on the DbContext. The try/catch below is a safety net:
            // the synchronous pre-check above should already guarantee a runner exists, but if
            // that check and this lookup ever diverge, this ensures the run is marked Failed
            // instead of being silently stuck in Running forever.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var runner = scope.ServiceProvider
                        .GetServices<IDqtJobRunner>()
                        .SingleOrDefault(r => r.CanHandle(request.TestType))
                        ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
                    await runner.RunAsync(run.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DQT run {DqtRunId} ({TestType}) failed before RunAsync was reached", run.Id, request.TestType);
                    var scopedRepository = scope.ServiceProvider.GetRequiredService<IDqtRunRepository>();
                    var scopedRun = await scopedRepository.GetByIdAsync(run.Id, CancellationToken.None);
                    scopedRun?.Fail(ex.Message, _timeProvider.GetUtcNow().DateTime);
                    await scopedRepository.SaveChangesAsync(CancellationToken.None);
                }
            }, CancellationToken.None);

            _logger.LogInformation("DQT run {DqtRunId} started for {TestType} from {DateFrom} to {DateTo}",
                run.Id, run.TestType, run.DateFrom, run.DateTo);

            return new RunDqtResponse
            {
                DqtRunId = run.Id,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting DQT run");
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
}
