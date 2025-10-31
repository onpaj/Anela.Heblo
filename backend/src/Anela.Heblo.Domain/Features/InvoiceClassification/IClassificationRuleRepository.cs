namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationRuleRepository
{
    Task<List<ClassificationRule>> GetAllAsync();
    
    Task<List<ClassificationRule>> GetActiveRulesOrderedAsync();
    
    Task<ClassificationRule?> GetByIdAsync(Guid id);
    
    Task<ClassificationRule> AddAsync(ClassificationRule rule);
    
    Task<ClassificationRule> UpdateAsync(ClassificationRule rule);
    
    Task DeleteAsync(Guid id);
    
    Task ReorderRulesAsync(List<Guid> ruleIds);
}