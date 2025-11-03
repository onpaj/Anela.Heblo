namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ClassificationRuleDto
{
    public Guid Id { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    public string RuleTypeIdentifier { get; set; } = string.Empty;
    
    public string Pattern { get; set; } = string.Empty;
    
    public string AccountingTemplateCode { get; set; } = string.Empty;
    
    public int Order { get; set; }
    
    public bool IsActive { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime UpdatedAt { get; set; }
    
    public string CreatedBy { get; set; } = string.Empty;
    
    public string UpdatedBy { get; set; } = string.Empty;
}