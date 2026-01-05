using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;

public class GetStockUpOperationsResponse : BaseResponse
{
    public List<StockUpOperationDto> Operations { get; set; } = new();
    public int TotalCount { get; set; }
}

public class StockUpOperationDto
{
    public int Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int Amount { get; set; }
    public StockUpOperationState State { get; set; }
    public StockUpSourceType SourceType { get; set; }
    public int SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
