using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesResponse
{
    public List<AccountingTemplateDto> Templates { get; set; } = new();
}