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

    public async Task SaveAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Converting domain invoice to FlexiBee format: {InvoiceCode}", invoiceDetail.Code);

            // Map domain model to FlexiBee SDK model
            var flexiInvoice = _mapper.Map<IssuedInvoiceDetail, IssuedInvoiceDetailFlexiDto>(invoiceDetail);

            // Call FlexiBee SDK
            var flexiResult = await _flexiClient.SaveAsync(flexiInvoice, cancellationToken);

            _logger.LogDebug("FlexiBee save result: {Success} for invoice {InvoiceCode}",
                flexiResult.IsSuccess, invoiceDetail.Code);

            if (!flexiResult.IsSuccess)
            {
                throw new ApplicationException(flexiResult.GetErrorMessage());
            }
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
}


public class XmlIssuedInvoiceClient : Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices.IIssuedInvoiceClient
{
    private readonly ILogger<XmlIssuedInvoiceClient> _logger;
    private const string InvoicesFolder = "invoices";

    public XmlIssuedInvoiceClient(ILogger<XmlIssuedInvoiceClient> logger)
    {
        _logger = logger;

        if (!Directory.Exists(InvoicesFolder))
        {
            Directory.CreateDirectory(InvoicesFolder);
            _logger.LogInformation("Created invoices directory: {Directory}", InvoicesFolder);
        }
    }

    public Task<IssuedInvoiceDetailFlexiDto> GetAsync(string invoiceCode, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(InvoicesFolder, $"{invoiceCode}.xml");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Invoice file not found: {FilePath}", filePath);
            return Task.FromResult(new IssuedInvoiceDetailFlexiDto { Code = invoiceCode });
        }

        try
        {
            var xmlContent = File.ReadAllText(filePath);
            var doc = XDocument.Parse(xmlContent);

            var invoice = new IssuedInvoiceDetailFlexiDto
            {
                Code = invoiceCode,
                Id = doc.Root?.Element("Id")?.Value,
            };

            _logger.LogInformation("Retrieved invoice from XML: {InvoiceCode}", invoiceCode);
            return Task.FromResult(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read invoice XML: {FilePath}", filePath);
            return Task.FromResult(new IssuedInvoiceDetailFlexiDto { Code = invoiceCode });
        }
    }

    public Task<OperationResult<OperationResultDetail>> SaveAsync(IssuedInvoiceDetailFlexiDto invoice, CancellationToken cancellationToken = default)
    {
        try
        {
            var invoiceId = invoice.Code ?? invoice.Id ?? Guid.NewGuid().ToString();
            var filePath = Path.Combine(InvoicesFolder, $"{invoiceId}.xml");

            var xmlDoc = new XDocument(
                new XElement("IssuedInvoice",
                    new XElement("Id", invoice.Id),
                    new XElement("Code", invoice.Code),
                    new XElement("DateCreated", DateTime.UtcNow.ToString("O")),
                    new XElement("InvoiceData",
                        System.Text.Json.JsonSerializer.Serialize(invoice, new JsonSerializerOptions { WriteIndented = true })
                    )
                )
            );

            xmlDoc.Save(filePath);

            _logger.LogInformation("Saved invoice to XML: {InvoiceCode} -> {FilePath}", invoiceId, filePath);

            return Task.FromResult(new OperationResult<OperationResultDetail>(System.Net.HttpStatusCode.OK));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save invoice to XML: {InvoiceCode}", invoice.Code);

            return Task.FromResult(
                new OperationResult<OperationResultDetail>(System.Net.HttpStatusCode.InternalServerError, ex.Message));
        }
    }
}