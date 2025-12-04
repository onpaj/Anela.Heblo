using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.Invoices.Services;

public interface IInvoiceImportService
{
    [DisplayName("Import faktur: {0}")]
    Task<ImportResultDto> ImportInvoicesAsync(string description, IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default);
}