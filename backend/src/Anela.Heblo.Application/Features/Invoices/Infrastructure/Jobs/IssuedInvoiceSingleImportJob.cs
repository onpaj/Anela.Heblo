using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.BackgroundJobs;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public class IssuedInvoiceSingleImportJob : AsyncBackgroundJob<IssuedInvoiceSingleImportArgs>
{
    private readonly IMediator _mediator;

    public IssuedInvoiceSingleImportJob(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task ExecuteAsync(IssuedInvoiceSingleImportArgs args)
    {
        var query = new IssuedInvoiceSourceQuery()
        {
            RequestId = $"Single invoice import: {args.InvoiceId}",
            InvoiceIds = new List<string> { args.InvoiceId },
            Currency = args.Currency
        };
        
        var request = new ImportInvoicesRequest 
        {
            Query = query
        };

        return _mediator.Send(request);
    }
}