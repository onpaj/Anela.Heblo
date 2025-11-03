using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public class InvoiceClassificationResult
{
    public ClassificationResult Result { get; set; }

    public Guid? RuleId { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? ErrorMessage { get; set; }
}