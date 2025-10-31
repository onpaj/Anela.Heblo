namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ClassificationStatistics
{
    public int TotalInvoicesProcessed { get; set; }
    
    public int SuccessfulClassifications { get; set; }
    
    public int ManualReviewRequired { get; set; }
    
    public int Errors { get; set; }
    
    public decimal SuccessRate => TotalInvoicesProcessed > 0 
        ? (decimal)SuccessfulClassifications / TotalInvoicesProcessed * 100 
        : 0;
    
    public List<RuleUsageStatistic> RuleUsage { get; set; } = new();
}