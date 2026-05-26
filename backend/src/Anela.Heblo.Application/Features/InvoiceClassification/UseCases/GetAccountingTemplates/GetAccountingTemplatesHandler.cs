using AutoMapper;
using MediatR;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesHandler : IRequestHandler<GetAccountingTemplatesRequest, GetAccountingTemplatesResponse>
{
    private readonly IInvoiceClassificationsClient _invoiceClassificationsClient;
    private readonly IMapper _mapper;

    public GetAccountingTemplatesHandler(
        IInvoiceClassificationsClient invoiceClassificationsClient,
        IMapper mapper)
    {
        _invoiceClassificationsClient = invoiceClassificationsClient;
        _mapper = mapper;
    }

    public async Task<GetAccountingTemplatesResponse> Handle(GetAccountingTemplatesRequest request, CancellationToken cancellationToken)
    {
        var templates = await _invoiceClassificationsClient.GetValidAccountingTemplatesAsync(cancellationToken);

        return new GetAccountingTemplatesResponse
        {
            Templates = _mapper.Map<List<Contracts.AccountingTemplateDto>>(templates)
        };
    }
}