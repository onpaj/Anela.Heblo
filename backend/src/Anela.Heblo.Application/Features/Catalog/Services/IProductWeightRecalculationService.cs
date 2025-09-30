namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IProductWeightRecalculationService
{
    Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken cancellationToken = default);
    Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken cancellationToken = default);
}

public class ProductWeightRecalculationResult
{
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}