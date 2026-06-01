using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesResponse : BaseResponse
{
    public string Platform { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
