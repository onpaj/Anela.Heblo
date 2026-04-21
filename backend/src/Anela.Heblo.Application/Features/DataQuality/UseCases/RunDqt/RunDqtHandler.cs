using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;

public class RunDqtHandler : IRequestHandler<RunDqtRequest, RunDqtResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IInvoiceDqtJobRunner _jobRunner;
    private readonly ILogger<RunDqtHandler> _logger;

    public RunDqtHandler(
        IDqtRunRepository repository,
        IInvoiceDqtJobRunner jobRunner,
        ILogger<RunDqtHandler> logger)
    {
        _repository = repository;
        _jobRunner = jobRunner;
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
            var run = DqtRun.Start(request.TestType, request.DateFrom, request.DateTo, DqtTriggerType.Manual);
            await _repository.AddAsync(run, cancellationToken);

            _ = Task.Run(() => _jobRunner.RunAsync(run.Id), CancellationToken.None);

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
