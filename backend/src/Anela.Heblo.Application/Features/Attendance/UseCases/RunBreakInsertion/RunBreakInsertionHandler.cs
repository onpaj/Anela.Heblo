using Anela.Heblo.Application.Features.Attendance.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

public class RunBreakInsertionHandler : IRequestHandler<RunBreakInsertionRequest, RunBreakInsertionResponse>
{
    private readonly BreakInsertionService _service;
    private readonly ILogger<RunBreakInsertionHandler> _logger;

    public RunBreakInsertionHandler(
        BreakInsertionService service,
        ILogger<RunBreakInsertionHandler> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RunBreakInsertionResponse> Handle(
        RunBreakInsertionRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Break insertion requested on demand with lookback {LookbackDays}",
            request.LookbackDays?.ToString() ?? "(configured default)");

        var summary = await _service.RunAsync(request.LookbackDays, cancellationToken);

        return new RunBreakInsertionResponse
        {
            DaysScanned = summary.DaysScanned,
            BreaksInserted = summary.BreaksInserted,
            RecordsTouched = summary.RecordsTouched,
            SkippedExistingBreak = summary.SkippedExistingBreak,
            SkippedInProgress = summary.SkippedInProgress,
            SkippedBelowThreshold = summary.SkippedBelowThreshold,
            SkippedHoursOnly = summary.SkippedHoursOnly,
            SkippedNoSlot = summary.SkippedNoSlot,
            Failed = summary.Failed
        };
    }
}
