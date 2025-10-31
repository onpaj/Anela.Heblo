namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ClassificationRule
{
    public Guid Id { get; private set; }
    
    public string Name { get; private set; } = string.Empty;
    
    public string RuleTypeIdentifier { get; private set; } = string.Empty;
    
    public string Pattern { get; private set; } = string.Empty;
    
    public string AccountingPrescription { get; private set; } = string.Empty;
    
    public int Order { get; private set; }
    
    public bool IsActive { get; private set; } = true;
    
    public DateTime CreatedAt { get; private set; }
    
    public DateTime UpdatedAt { get; private set; }
    
    public string CreatedBy { get; private set; } = string.Empty;
    
    public string UpdatedBy { get; private set; } = string.Empty;

    protected ClassificationRule()
    {
    }

    public ClassificationRule(
        string name,
        string ruleTypeIdentifier,
        string pattern,
        string accountingPrescription,
        string createdBy)
    {
        Id = Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        RuleTypeIdentifier = ruleTypeIdentifier ?? throw new ArgumentNullException(nameof(ruleTypeIdentifier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        AccountingPrescription = accountingPrescription ?? throw new ArgumentNullException(nameof(accountingPrescription));
        Order = 0; // Will be set when adding to collection
        IsActive = true;
        CreatedBy = createdBy ?? throw new ArgumentNullException(nameof(createdBy));
        CreatedAt = DateTime.UtcNow;
        UpdatedBy = createdBy;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(
        string name,
        string ruleTypeIdentifier,
        string pattern,
        string accountingPrescription,
        bool isActive,
        string updatedBy)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        RuleTypeIdentifier = ruleTypeIdentifier ?? throw new ArgumentNullException(nameof(ruleTypeIdentifier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        AccountingPrescription = accountingPrescription ?? throw new ArgumentNullException(nameof(accountingPrescription));
        IsActive = isActive;
        UpdatedBy = updatedBy ?? throw new ArgumentNullException(nameof(updatedBy));
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetOrder(int order)
    {
        Order = order;
        UpdatedAt = DateTime.UtcNow;
    }
}