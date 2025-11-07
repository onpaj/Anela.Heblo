using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ClassificationHistoryDto
{
    public Guid Id { get; set; }

    public string InvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? ClassificationRuleId { get; set; }

    public string? RuleName { get; set; }

    public ClassificationResult Result { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? Department { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime Timestamp { get; set; }

    public string ProcessedBy { get; set; } = string.Empty;
}