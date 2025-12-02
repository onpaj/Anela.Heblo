using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Services;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueInvoiceImport;

public class EnqueueInvoiceImportHandler : IRequestHandler<EnqueueInvoiceImportRequest, List<string>>
{
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IMapper _mapper;

    public EnqueueInvoiceImportHandler(
        IBackgroundWorker backgroundWorker,
        IMapper mapper)
    {
        _backgroundWorker = backgroundWorker;
        _mapper = mapper;
    }

    public Task<List<string>> Handle(EnqueueInvoiceImportRequest request, CancellationToken cancellationToken)
    {
        if (!request.ImportRequest.InvoiceIds.Any())
        {
            var query = _mapper.Map<ImportInvoiceRequestDto, IssuedInvoiceSourceQuery>(request.ImportRequest);
            return Task.FromResult(EnqueueImportByDate(request.ImportRequest, query));
        }

        var jobIds = new List<string>();
        foreach(var invoiceId in request.ImportRequest.InvoiceIds)
        {
            var currency = request.ImportRequest.Currency ?? "CZK";
            
            //TODO Enqueue job
            //jobIds.Add(_backgroundWorker.Enqueue<Infrastructure.Jobs.IssuedInvoiceSingleImportJob>(job => job.ExecuteAsync(invoiceId, currency)));
        }

        return Task.FromResult(jobIds);
    }

    private List<string> EnqueueImportByDate(ImportInvoiceRequestDto request, IssuedInvoiceSourceQuery query)
    {
        var taskIds = new List<string>();
        if (!request.DateFrom.HasValue)
            throw new ArgumentNullException($"{nameof(request.DateFrom)} must have value");
        if (!request.DateTo.HasValue)
            throw new ArgumentNullException($"{nameof(request.DateTo)} must have value");
        if (request.DateTo < request.DateFrom)
            throw new ArgumentException($"{nameof(request.DateTo)} must be later than {nameof(request.DateFrom)}");
        if ((request.DateTo.Value - request.DateFrom.Value).TotalDays > 14)
            throw new ArgumentException($"Interval is too large, add 14 days at most");

        for (var day = request.DateFrom.Value.Date; day < request.DateTo.Value.Date; day = day.AddDays(1))
        {
            var currency = query.Currency ?? "CZK";
            // TODO Enqueue job
            //taskIds.Add(_backgroundWorker.Enqueue<Infrastructure.Jobs.IssuedInvoiceDailyImportJob>(job => job.ImportForDate(day, currency)));
        }

        return taskIds;
    }
}