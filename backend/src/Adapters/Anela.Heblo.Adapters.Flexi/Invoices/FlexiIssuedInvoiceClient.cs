using System.Text.Json;
using System.Xml.Linq;
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

    public Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        throw new NotImplementedException("GetAllAsync is blocked on FlexiBee SDK support.");
    }
}