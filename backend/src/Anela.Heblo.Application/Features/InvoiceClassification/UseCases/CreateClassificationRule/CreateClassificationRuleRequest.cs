using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.CreateClassificationRule;

public class CreateClassificationRuleRequest : IRequest<CreateClassificationRuleResponse>
{
    public string Name { get; set; } = string.Empty;
    
    public string RuleTypeIdentifier { get; set; } = string.Empty;
    
    public string Pattern { get; set; } = string.Empty;
    
    public string AccountingPrescription { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
}