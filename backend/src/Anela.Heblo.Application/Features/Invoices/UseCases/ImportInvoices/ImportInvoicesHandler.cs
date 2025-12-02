using System.Text.Json;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;

public class ImportInvoicesHandler : IRequestHandler<ImportInvoicesRequest, ImportResultDto>
{
    private readonly IIssuedInvoiceSource _issuedInvoiceSource;
    private readonly IIssuedInvoiceClient _issuedInvoiceClient;
    private readonly IIssuedInvoiceRepository _repository;
    private readonly IEnumerable<IIssuedInvoiceImportTransformation> _importTransformations;
    private readonly IMapper _mapper;
    private readonly ILogger<ImportInvoicesHandler> _logger;

    public ImportInvoicesHandler(
        IIssuedInvoiceSource issuedInvoiceSource,
        IIssuedInvoiceClient issuedInvoiceClient,
        IIssuedInvoiceRepository repository,
        IEnumerable<IIssuedInvoiceImportTransformation> importTransformations,
        IMapper mapper,
        ILogger<ImportInvoicesHandler> logger)
    {
        _issuedInvoiceSource = issuedInvoiceSource;
        _issuedInvoiceClient = issuedInvoiceClient;
        _repository = repository;
        _importTransformations = importTransformations;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ImportResultDto> Handle(ImportInvoicesRequest request, CancellationToken cancellationToken)
    {
        var batches = await _issuedInvoiceSource.GetAllAsync(request.Query);

        var resultDto = new ImportResultDto()
        {
            RequestId = request.Query.RequestId,
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
                }
            }

            if (error)
                await _issuedInvoiceSource.FailAsync(batch);
            else
                await _issuedInvoiceSource.CommitAsync(batch);
        }

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