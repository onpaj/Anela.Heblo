using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesRequest : IRequest<ImportMarketingInvoicesResponse>
{
    public string Platform { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
