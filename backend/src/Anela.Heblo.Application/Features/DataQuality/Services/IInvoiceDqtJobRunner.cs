using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Runs the invoice DQT comparison for a given DQT run ID or date range.
/// </summary>
public interface IInvoiceDqtJobRunner
{
    /// <summary>
    /// Loads an existing run by ID and executes the comparison.
    /// </summary>
    Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new DQT run for the given date range, executes the comparison, and returns the run ID.
    /// </summary>
    Task<Guid> RunForDateRangeAsync(DateOnly from, DateOnly to, DqtTriggerType triggerType, CancellationToken cancellationToken = default);
}
