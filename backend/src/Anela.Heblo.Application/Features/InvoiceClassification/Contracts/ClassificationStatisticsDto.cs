namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ClassificationStatisticsDto
{
    public int TotalInvoicesProcessed { get; set; }
    
    public int SuccessfulClassifications { get; set; }
    
    public int ManualReviewRequired { get; set; }
    
    public int Errors { get; set; }
    
    public decimal SuccessRate { get; set; }
    
    public List<RuleUsageStatisticDto> RuleUsage { get; set; } = new();
}