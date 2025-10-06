namespace Anela.Heblo.Domain.Features.Manufacture;

public class DiscardResidualSemiProductResponse
{
    /// <summary>
    /// Indicates whether the discard operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The actual quantity that was found in stock
    /// </summary>
    public double QuantityFound { get; set; }

    /// <summary>
    /// The quantity that was automatically discarded
    /// </summary>
    public double QuantityDiscarded { get; set; }

    /// <summary>
    /// Indicates if manual approval is required due to quantity exceeding auto-discard limit
    /// </summary>
    public bool RequiresManualApproval { get; set; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Reference ID or code of the created stock movement (if any)
    /// </summary>
    public string? StockMovementReference { get; set; }

    /// <summary>
    /// Additional information about the operation
    /// </summary>
    public string? Details { get; set; }
}