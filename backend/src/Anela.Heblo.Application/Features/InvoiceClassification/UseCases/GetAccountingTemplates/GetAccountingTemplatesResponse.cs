using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesResponse : BaseResponse
{
    public List<AccountingTemplateDto> Templates { get; set; } = new();

    public GetAccountingTemplatesResponse() : base() { }

    public GetAccountingTemplatesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}