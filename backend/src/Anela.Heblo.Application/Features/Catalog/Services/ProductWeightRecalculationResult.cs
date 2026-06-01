namespace Anela.Heblo.Application.Features.Catalog.Services;

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