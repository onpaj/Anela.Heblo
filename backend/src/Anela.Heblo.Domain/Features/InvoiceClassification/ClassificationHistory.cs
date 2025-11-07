namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ClassificationHistory
{
    public Guid Id { get; private set; }

    public string AbraInvoiceId { get; private set; } = string.Empty;

    public string InvoiceNumber { get; private set; } = string.Empty;

    public DateTime? InvoiceDate { get; private set; }

    public string CompanyName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Guid? ClassificationRuleId { get; private set; }

    public ClassificationRule? ClassificationRule { get; private set; }

    public ClassificationResult Result { get; private set; }

    public string? AccountingTemplateCode { get; private set; }

    public string? Department { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime Timestamp { get; private set; }

    public string ProcessedBy { get; private set; } = string.Empty;

    protected ClassificationHistory()
    {
    }

    public ClassificationHistory(
        string abraInvoiceId,
        string invoiceNumber,
        DateTime? invoiceDate,
        string companyName,
        string description,
        ClassificationResult result,
        string processedBy,
        Guid? classificationRuleId = null,
        string? accountingTemplateCode = null,
        string? department = null,
        string? errorMessage = null)
    {
        Id = Guid.NewGuid();
        AbraInvoiceId = abraInvoiceId ?? throw new ArgumentNullException(nameof(abraInvoiceId));
        InvoiceNumber = invoiceNumber ?? throw new ArgumentNullException(nameof(invoiceNumber));
        InvoiceDate = invoiceDate;
        CompanyName = companyName ?? throw new ArgumentNullException(nameof(companyName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        ClassificationRuleId = classificationRuleId;
        Result = result;
        AccountingTemplateCode = accountingTemplateCode;
        Department = department;
        ErrorMessage = errorMessage;
        ProcessedBy = processedBy ?? throw new ArgumentNullException(nameof(processedBy));
        Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}