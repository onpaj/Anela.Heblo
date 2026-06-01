namespace Anela.Heblo.Domain.Features.Logistics.Transport;

/// <summary>
/// Service for completing transport boxes after stock-up operations finish
/// </summary>
public interface ITransportBoxCompletionService
{
    /// <summary>
    /// Check all boxes in Received state and transition them to Stocked/Error
    /// based on their StockUpOperations completion status
    /// </summary>
    Task CompleteReceivedBoxesAsync(CancellationToken cancellationToken = default);
}
