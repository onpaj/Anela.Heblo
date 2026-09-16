using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;

/// <summary>
/// Runs the break-insertion walk on demand. The nightly job covers a short rolling window ending
/// today; this takes an explicit window so a deliberate sweep can be walked backwards through
/// history in steps, without changing the nightly schedule.
/// </summary>
public class RunBreakInsertionRequest : IRequest<RunBreakInsertionResponse>
{
    /// <summary>Oldest day to scan, counted in whole days before today. Omit for the configured nightly lookback.</summary>
    public int? FromDaysAgo { get; set; }

    /// <summary>Newest day to scan, counted in whole days before today. Omit to scan up to today.</summary>
    public int? ToDaysAgo { get; set; }
}
