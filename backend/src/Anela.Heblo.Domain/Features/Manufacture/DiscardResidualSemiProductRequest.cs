namespace Anela.Heblo.Domain.Features.Manufacture;

public class DiscardResidualSemiProductRequest
{
    /// <summary>
    /// The manufacture order code that has been completed
    /// </summary>
    public string ManufactureOrderCode { get; set; } = null!;

    /// <summary>
    /// Code of the semi-product to check for residual quantities
    /// </summary>
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; }


    /// <summary>
    /// Date when the manufacturing was completed
    /// </summary>
    public DateTime CompletionDate { get; set; }

    /// <summary>
    /// User who completed the manufacturing
    /// </summary>
    public string? CompletedBy { get; set; }

    /// <summary>
    /// Maximum quantity that can be automatically discarded for this product
    /// </summary>
    public double MaxAutoDiscardQuantity { get; set; }
}