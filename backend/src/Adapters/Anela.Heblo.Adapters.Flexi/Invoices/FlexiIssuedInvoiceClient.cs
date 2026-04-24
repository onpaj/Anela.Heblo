using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Model.Invoices;
using Rem.FlexiBeeSDK.Model.Response;
using IIssuedInvoiceClient = Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices.IIssuedInvoiceClient;

namespace Anela.Heblo.Adapters.Flexi.Invoices;

public class FlexiIssuedInvoiceClient : Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceClient
{
    private readonly Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices.IIssuedInvoiceClient _flexiClient;
    private readonly IMapper _mapper;
    private readonly ILogger<FlexiIssuedInvoiceClient> _logger;

    public FlexiIssuedInvoiceClient(
        Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices.IIssuedInvoiceClient flexiClient,
        IMapper mapper,
        ILogger<FlexiIssuedInvoiceClient> logger)
    {
        _flexiClient = flexiClient;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Converting domain invoice to FlexiBee format: {InvoiceCode}", invoiceDetail.Code);

            // Map domain model to FlexiBee SDK model
            var flexiInvoice = _mapper.Map<IssuedInvoiceDetail, IssuedInvoiceDetailFlexiDto>(invoiceDetail);

            // Call FlexiBee SDK
            var flexiResult = await _flexiClient.SaveAsync(flexiInvoice, true, cancellationToken);
            var rawResponse = JsonSerializer.Serialize(flexiResult);

            _logger.LogDebug("FlexiBee save result: {Success} for invoice {InvoiceCode}",
                flexiResult.IsSuccess, invoiceDetail.Code);

            if (!flexiResult.IsSuccess)
            {
                _logger.LogDebug("FlexiBee HTTP 400 response payload for invoice {InvoiceCode}: {Payload}",
                    invoiceDetail.Code, rawResponse);
                throw new IssuedInvoiceClientException(flexiResult.GetErrorMessage(), rawResponse);
            }

            return rawResponse;
        }
        catch (IssuedInvoiceClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving invoice to FlexiBee: {InvoiceCode}", invoiceDetail.Code);
            throw;
        }
    }

    public async Task<IssuedInvoiceDetail> GetAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting invoice from FlexiBee: {InvoiceId}", invoiceId);

            var flexiResult = await _flexiClient.GetAsync(invoiceId, cancellationToken);
            _logger.LogDebug("FlexiBee get result: {Success} for invoice {InvoiceId}", flexiResult.Id, invoiceId);

            // Map FlexiBee result back to domain result
            return _mapper.Map<IssuedInvoiceDetailFlexiDto, IssuedInvoiceDetail>(flexiResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice from FlexiBee: {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Getting all invoices from FlexiBee for period {From} to {To}", from, to);

            var dateFrom = from.ToDateTime(TimeOnly.MinValue);
            var dateTo = to.ToDateTime(TimeOnly.MaxValue);

            var flexiInvoices = await _flexiClient.GetAllAsync(dateFrom, dateTo, ct);

            _logger.LogDebug("FlexiBee returned {Count} invoices for period {From}–{To}", flexiInvoices.Count, from, to);

            return flexiInvoices.Select(MapToDomain).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all invoices from FlexiBee: {From} to {To}", from, to);
            throw;
        }
    }

    private static IssuedInvoiceDetail MapToDomain(IssuedInvoiceDetailFlexiDto dto)
    {
        var items = dto.Items.Select(MapItemToDomain).ToList();

        return new IssuedInvoiceDetail
        {
            Code = dto.Code ?? string.Empty,
            Price = new InvoicePrice
            {
                TotalWithVat = ParseDecimal(dto.SumTotal),
                TotalWithoutVat = items.Sum(i => i.ItemPrice.TotalWithoutVat),
            },
            Items = items,
        };
    }

    private static IssuedInvoiceDetailItem MapItemToDomain(IssuedInvoiceItemFlexiDto item)
    {
        var amount = ParseDecimal(item.Amount);
        var totalWithVat = item.SumTotal ?? item.SumTotalC ?? 0m;
        var totalWithoutVat = item.SumBase ?? item.SumBaseC ?? 0m;

        return new IssuedInvoiceDetailItem
        {
            Code = ResolveItemCode(item.Code, item.PriceList),
            Name = item.Name ?? string.Empty,
            Amount = amount,
            ItemPrice = new InvoicePrice
            {
                TotalWithVat = totalWithVat,
                TotalWithoutVat = totalWithoutVat,
                WithoutVat = item.PricePerUnit,
                WithVat = amount > 0 ? Math.Round(totalWithVat / amount, 4) : 0m,
            },
        };
    }

    // PriceList ("code:PRODUCT-CODE") is the reliable product identifier on read-back from FlexiBee.
    // Code is often null or an internal FlexiBee item ID.
    private static string ResolveItemCode(string? code, string? priceList)
    {
        if (!string.IsNullOrEmpty(code))
            return code;
        if (!string.IsNullOrEmpty(priceList))
            return priceList.StartsWith("code:") ? priceList[5..] : priceList;
        return string.Empty;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }
}