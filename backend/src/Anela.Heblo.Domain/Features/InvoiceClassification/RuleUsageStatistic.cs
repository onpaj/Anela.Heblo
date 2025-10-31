namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class RuleUsageStatistic
{
    public Guid RuleId { get; set; }
    
    public string RuleName { get; set; } = string.Empty;
    
    public int UsageCount { get; set; }
    
    public decimal UsagePercentage { get; set; }
}