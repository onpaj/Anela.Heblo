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

    public async Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query)
    {
        var ct = CancellationToken.None;

        IReadOnlyList<ShoptetInvoiceDto> invoices;

        if (query.QueryByInvoice)
        {
            var single = await _client.GetInvoiceAsync(query.InvoiceId!, ct);
            invoices = single != null
                ? new[] { single }
                : Array.Empty<ShoptetInvoiceDto>();
        }
        else
        {
            invoices = await _client.ListInvoicesAsync(query.DateFrom, query.DateTo, ct);
        }

        var total = invoices.Count;

        var filtered = invoices
            .Where(i => string.Equals(
                i.Price?.CurrencyCode,
                query.Currency,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation(
            "ShoptetApiInvoiceSource fetched {Total} invoices, {Filtered} match currency {Currency}",
            total,
            filtered.Count,
            query.Currency);

        var details = filtered
            .Select(i => _mapper.Map(i))
            .ToList();

        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = query.RequestId,
            Invoices = details,
        };

        return new List<IssuedInvoiceDetailBatch> { batch };
    }

    public Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default)
        => Task.CompletedTask;

    public Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default)
        => Task.CompletedTask;
}
