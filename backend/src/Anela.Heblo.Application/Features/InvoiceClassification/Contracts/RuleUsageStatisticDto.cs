namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class RuleUsageStatisticDto
{
    public Guid RuleId { get; set; }
    
    public string RuleName { get; set; } = string.Empty;
    
    public int UsageCount { get; set; }
    
    public decimal UsagePercentage { get; set; }
}