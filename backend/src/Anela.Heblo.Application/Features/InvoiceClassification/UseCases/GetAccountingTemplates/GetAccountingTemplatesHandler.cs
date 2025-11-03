using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesHandler : IRequestHandler<GetAccountingTemplatesRequest, GetAccountingTemplatesResponse>
{
    private readonly IInvoiceClassificationsClient _invoiceClassificationsClient;

    public GetAccountingTemplatesHandler(IInvoiceClassificationsClient invoiceClassificationsClient)
    {
        _invoiceClassificationsClient = invoiceClassificationsClient;
    }

    public async Task<GetAccountingTemplatesResponse> Handle(GetAccountingTemplatesRequest request, CancellationToken cancellationToken)
    {
        var templates = await _invoiceClassificationsClient.GetValidAccountingTemplatesAsync();
        
        return new GetAccountingTemplatesResponse
        {
            Templates = templates
        };
    }
}