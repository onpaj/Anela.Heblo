using Anela.Heblo.Application.Common;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Model.Invoices;
using AutoMapper;

namespace Anela.Heblo.Adapters.Flexi.InvoiceClassification;

public class FlexiReceivedInvoicesClient : IReceivedInvoicesClient
{
    private readonly IReceivedInvoiceClient _client;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlexiReceivedInvoicesClient> _logger;
    private readonly IMapper _mapper;

    public FlexiReceivedInvoicesClient(
        IReceivedInvoiceClient  client,
        IOptions<DataSourceOptions> dataSourceOptions,
        TimeProvider timeProvider,
        ILogger<FlexiReceivedInvoicesClient> logger,
        IMapper mapper)
    {
        _client = client;
        _dataSourceOptions = dataSourceOptions;
        _timeProvider = timeProvider;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync()
    {
        var dateTo = _timeProvider.GetLocalNow().DateTime;
        var dateFrom = dateTo.AddDays(-1 * _dataSourceOptions.Value.InvoiceClassificationDaysBack);
        var invoices = await _client.SearchAsync(new ReceivedInvoiceRequest(dateFrom, dateTo,
            label: _dataSourceOptions.Value.InvoiceClassificationTriggerLabel));

        return _mapper.Map<List<ReceivedInvoiceDto>>(invoices);
    }

    public async Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId)
    {
        var found = await _client.GetAsync(invoiceId);
        return _mapper.Map<ReceivedInvoiceDto>(found);;
    }
}