using Anela.Heblo.Application.Features.Invoices.Contracts;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;

public class ImportInvoicesResponse
{
    public ImportResultDto Result { get; set; } = new();
}