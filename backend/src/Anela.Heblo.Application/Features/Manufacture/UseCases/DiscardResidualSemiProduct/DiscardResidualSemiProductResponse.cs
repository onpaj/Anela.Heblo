using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct;

public class DiscardResidualSemiProductResponse : BaseResponse
{
    public double QuantityFound { get; set; }
    
    public double QuantityDiscarded { get; set; }
    
    public bool RequiresManualApproval { get; set; }
    
    public string? StockMovementReference { get; set; }
    
    public string? Details { get; set; }

    public DiscardResidualSemiProductResponse() : base() { }

    public DiscardResidualSemiProductResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }

    public DiscardResidualSemiProductResponse(Exception exception) : base(exception)
    {
    }
}