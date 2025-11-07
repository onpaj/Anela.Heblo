namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ClassificationRule
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string RuleTypeIdentifier { get; private set; } = string.Empty;

    public string Pattern { get; private set; } = string.Empty;

    public string AccountingTemplateCode { get; private set; } = string.Empty;

    public string? Department { get; private set; }

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
        string accountingTemplateCode,
        string? department,
        string createdBy)
    {
        Id = Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        RuleTypeIdentifier = ruleTypeIdentifier ?? throw new ArgumentNullException(nameof(ruleTypeIdentifier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        AccountingTemplateCode = accountingTemplateCode ?? throw new ArgumentNullException(nameof(accountingTemplateCode));
        Department = department;
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
        string accountingTemplateCode,
        string? department,
        bool isActive,
        string updatedBy)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        RuleTypeIdentifier = ruleTypeIdentifier ?? throw new ArgumentNullException(nameof(ruleTypeIdentifier));
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        AccountingTemplateCode = accountingTemplateCode ?? throw new ArgumentNullException(nameof(accountingTemplateCode));
        Department = department;
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