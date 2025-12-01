using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.BackgroundJobs;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueInvoiceImport;

public class EnqueueInvoiceImportHandler : IRequestHandler<EnqueueInvoiceImportRequest, List<string>>
{
    private readonly IBackgroundJobManager _jobManager;
    private readonly IMapper _mapper;

    public EnqueueInvoiceImportHandler(
        IBackgroundJobManager jobManager,
        IMapper mapper)
    {
        _jobManager = jobManager;
        _mapper = mapper;
    }

    public async Task<List<string>> Handle(EnqueueInvoiceImportRequest request, CancellationToken cancellationToken)
    {
        if (!request.ImportRequest.InvoiceIds.Any())
        {
            var query = _mapper.Map<ImportInvoiceRequestDto, IssuedInvoiceSourceQuery>(request.ImportRequest);
            return await EnqueueImportByDate(request.ImportRequest, query);
        }

        var jobIds = new List<string>();
        foreach(var invoiceId in request.ImportRequest.InvoiceIds)
        {
            jobIds.Add(await _jobManager.EnqueueAsync(new IssuedInvoiceSingleImportArgs(invoiceId, request.ImportRequest.Currency ?? "CZK")));
        }

        return jobIds;
    }

    private async Task<List<string>> EnqueueImportByDate(ImportInvoiceRequestDto request, IssuedInvoiceSourceQuery query)
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
            taskIds.Add(await _jobManager.EnqueueAsync(new IssuedInvoiceDailyImportArgs(day, query.Currency ?? "CZK")));
        }

        return taskIds;
    }
}