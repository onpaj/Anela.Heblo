namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ClassificationHistory
{
    public Guid Id { get; private set; }
    
    public string AbraInvoiceId { get; private set; } = string.Empty;
    
    public Guid? ClassificationRuleId { get; private set; }
    
    public ClassificationRule? ClassificationRule { get; private set; }
    
    public ClassificationResult Result { get; private set; }
    
    public string? AccountingPrescription { get; private set; }
    
    public string? ErrorMessage { get; private set; }
    
    public DateTime Timestamp { get; private set; }
    
    public string ProcessedBy { get; private set; } = string.Empty;

    protected ClassificationHistory()
    {
    }

    public ClassificationHistory(
        string abraInvoiceId,
        ClassificationResult result,
        string processedBy,
        Guid? classificationRuleId = null,
        string? accountingPrescription = null,
        string? errorMessage = null)
    {
        Id = Guid.NewGuid();
        AbraInvoiceId = abraInvoiceId ?? throw new ArgumentNullException(nameof(abraInvoiceId));
        ClassificationRuleId = classificationRuleId;
        Result = result;
        AccountingPrescription = accountingPrescription;
        ErrorMessage = errorMessage;
        ProcessedBy = processedBy ?? throw new ArgumentNullException(nameof(processedBy));
        Timestamp = DateTime.UtcNow;
    }
}