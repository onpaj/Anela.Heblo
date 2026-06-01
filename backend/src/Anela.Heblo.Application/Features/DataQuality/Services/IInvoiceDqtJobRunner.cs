namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Runs the invoice DQT comparison for a given DQT run ID.
/// </summary>
public interface IInvoiceDqtJobRunner
{
    Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default);
}
