using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using AutoMapper;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.Services;

public class InvoiceImportService : IInvoiceImportService
{
    private readonly IIssuedInvoiceSource _issuedInvoiceSource;
    private readonly IIssuedInvoiceClient _issuedInvoiceClient;
    private readonly IIssuedInvoiceRepository _repository;
    private readonly IEnumerable<IIssuedInvoiceImportTransformation> _importTransformations;
    private readonly IMapper _mapper;
    private readonly ILogger<InvoiceImportService> _logger;

    public InvoiceImportService(
        IIssuedInvoiceSource issuedInvoiceSource,
        IIssuedInvoiceClient issuedInvoiceClient,
        IIssuedInvoiceRepository repository,
        IEnumerable<IIssuedInvoiceImportTransformation> importTransformations,
        IMapper mapper,
        ILogger<InvoiceImportService> logger)
    {
        _issuedInvoiceSource = issuedInvoiceSource;
        _issuedInvoiceClient = issuedInvoiceClient;
        _repository = repository;
        _importTransformations = importTransformations;
        _mapper = mapper;
        _logger = logger;
    }

    [DisplayName("Import faktur: {0}")]
    public async Task<ImportResultDto> ImportInvoicesAsync(string description, IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting async invoice import with query: {Query}", JsonSerializer.Serialize(query));

        var batches = await _issuedInvoiceSource.GetAllAsync(query);

        var resultDto = new ImportResultDto()
        {
            RequestId = query.RequestId,
        };

        foreach (var batch in batches)
        {
            bool error = false;

            _logger.LogInformation("Importing batch: {BatchId}", batch.BatchId);
            foreach (var invoiceDetail in batch.Invoices)
            {
                try
                {
                    await ExecuteImportInvoice(invoiceDetail, cancellationToken);
                    resultDto.Succeeded.Add(invoiceDetail.Code);
                }
                catch (Exception ex)
                {
                    error = true;
                    resultDto.Failed.Add(invoiceDetail.Code);
                    _logger.LogError(ex, "Failed to import invoice: {InvoiceCode}", invoiceDetail.Code);
                }
            }

            if (error)
                await _issuedInvoiceSource.FailAsync(batch);
            else
                await _issuedInvoiceSource.CommitAsync(batch);
        }

        _logger.LogInformation("Completed async invoice import. Succeeded: {SucceededCount}, Failed: {FailedCount}",
            resultDto.Succeeded.Count, resultDto.Failed.Count);

        return resultDto;
    }

    private async Task<IssuedInvoice> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

            var invoice = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

            // Apply transformations to domain model
            var transformedInvoice = invoiceDetail;
            foreach (var transformation in _importTransformations)
            {
                transformedInvoice = await transformation.TransformAsync(transformedInvoice, cancellationToken);
            }

            try
            {
                // Send to external system via abstraction
                await _issuedInvoiceClient.SaveAsync(transformedInvoice, cancellationToken);
                invoice.SyncSucceeded(transformedInvoice);
                _logger.LogInformation(
                    "Successfully imported invoice: {InvoiceNumber}: {InvoiceValue} ({Currency})",
                    invoiceDetail.Code, invoiceDetail.Price.WithVat, invoiceDetail.Price.CurrencyCode);
            }
            catch (Exception ex)
            {
                invoice.SyncFailed(transformedInvoice, ex.Message);
            }

            await _repository.UpdateAsync(invoice, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while importing invoice: {InvoiceNumber}", invoiceDetail.Code);
            throw;
        }
    }

    private async Task<IssuedInvoice> GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
    {
        var found = await _repository.GetByIdAsync(key, cancellationToken);
        if (found == null)
        {
            found = factory();
            await _repository.AddAsync(found, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return found;
    }
}