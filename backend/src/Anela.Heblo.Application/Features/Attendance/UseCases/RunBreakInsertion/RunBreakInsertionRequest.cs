using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

/// <summary>
/// Runs the break-insertion walk on demand. The nightly job covers a short rolling window; this
/// exists for the occasional deliberate sweep over more history, e.g. after a change to how the
/// job writes records.
/// </summary>
public class RunBreakInsertionRequest : IRequest<RunBreakInsertionResponse>
{
    /// <summary>Days of history to scan before today. Omit to use the configured nightly value.</summary>
    public int? LookbackDays { get; set; }
}
