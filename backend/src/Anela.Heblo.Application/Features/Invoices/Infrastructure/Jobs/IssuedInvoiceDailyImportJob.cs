using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.BackgroundJobs;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;

public class IssuedInvoiceDailyImportJob : AsyncBackgroundJob<IssuedInvoiceDailyImportArgs>
{
    private readonly IMediator _mediator;
    private readonly IJobsService _jobsService;
    private readonly TimeProvider _timeProvider;

    public IssuedInvoiceDailyImportJob(
        IMediator mediator,
        IJobsService jobsService,
        TimeProvider timeProvider)
    {
        _mediator = mediator;
        _jobsService = jobsService;
        _timeProvider = timeProvider;
    }

    public async Task ImportYesterday(string jobName, CurrencyCode currency)
    {
        if(!await _jobsService.IsEnabled(jobName))
            return;
        
        await ExecuteAsync(new IssuedInvoiceDailyImportArgs(_timeProvider.GetUtcNow().AddDays(-1).Date, currency.ToString()));
    }
    
    public override Task ExecuteAsync(IssuedInvoiceDailyImportArgs args)
    {
        var query = new IssuedInvoiceSourceQuery()
        {
            RequestId = $"Daily schedule for {args.Day:yyyy-MM-dd}",
            DateFrom = args.Day.Date,
            DateTo = args.Day.Date,
            Currency = args.Currency.ToString()
        };
        
        var request = new ImportInvoicesRequest 
        {
            Query = query
        };

        return _mediator.Send(request);
    }
}