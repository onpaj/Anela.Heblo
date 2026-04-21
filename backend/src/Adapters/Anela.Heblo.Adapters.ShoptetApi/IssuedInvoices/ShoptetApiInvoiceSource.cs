using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;

public class ShoptetApiInvoiceSource : IIssuedInvoiceSource
{
    private readonly IShoptetInvoiceClient _client;
    private readonly ShoptetInvoiceMapper _mapper;
    private readonly ILogger<ShoptetApiInvoiceSource> _logger;

    public ShoptetApiInvoiceSource(
        IShoptetInvoiceClient client,
        ShoptetInvoiceMapper mapper,
        ILogger<ShoptetApiInvoiceSource> logger)
    {
        _client = client;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.QueryByInvoice)
        {
            var single = await _client.GetInvoiceAsync(query.InvoiceId!, cancellationToken);
            var invoices = single != null ? new[] { single } : Array.Empty<ShoptetInvoiceDto>();
            var details = invoices.Select(i => _mapper.Map(i)).ToList();
            return new List<IssuedInvoiceDetailBatch>
            {
                new IssuedInvoiceDetailBatch { BatchId = query.RequestId, Invoices = details },
            };
        }

        // Shoptet /api/invoices list endpoint returns minimal data (no addresses, items, dates).
        // We fetch the list to get codes and filter by currency, then fetch each detail separately.
        var listItems = await _client.ListInvoicesAsync(query.DateFrom, query.DateTo, cancellationToken);
        var total = listItems.Count;

        // Shoptet /api/invoices does not support currency filtering as a query parameter,
        // so we filter in memory after fetching. For the typical volume (hundreds per month)
        // this is acceptable; revisit if Shoptet adds server-side currency filtering.
        var matchingCodes = listItems
            .Where(i => string.Equals(
                i.Price?.CurrencyCode,
                query.Currency,
                StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Code)
            .ToList();

        _logger.LogInformation(
            "ShoptetApiInvoiceSource fetched {Total} invoices, {Filtered} match currency {Currency}",
            total,
            matchingCodes.Count,
            query.Currency);

        var detailDtos = new List<ShoptetInvoiceDto>(matchingCodes.Count);
        foreach (var code in matchingCodes)
        {
            var detail = await _client.GetInvoiceAsync(code, cancellationToken);
            if (detail != null)
                detailDtos.Add(detail);
        }

        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = query.RequestId,
            Invoices = detailDtos.Select(i => _mapper.Map(i)).ToList(),
        };

        return new List<IssuedInvoiceDetailBatch> { batch };
    }

    public Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default)
        => Task.CompletedTask;

    public Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default)
        => Task.CompletedTask;
}
