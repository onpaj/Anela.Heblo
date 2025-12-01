using System.Text.Json;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Xcc.Uow;
using Anela.Heblo.Xcc.Application.Dtos;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Model;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;

public class ImportInvoicesHandler : IRequestHandler<ImportInvoicesRequest, ImportResultDto>
{
    private readonly IIssuedInvoiceSource _issuedInvoiceSource;
    private readonly IIssuedInvoiceClient _issuedInvoiceClient;
    private readonly IIssuedInvoiceRepository _repository;
    private readonly IEnumerable<IIssuedInvoiceImportTransformation> _importTransformations;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IMapper _mapper;
    private readonly ILogger<ImportInvoicesHandler> _logger;

    public ImportInvoicesHandler(
        IIssuedInvoiceSource issuedInvoiceSource,
        IIssuedInvoiceClient issuedInvoiceClient,
        IIssuedInvoiceRepository repository,
        IEnumerable<IIssuedInvoiceImportTransformation> importTransformations,
        IUnitOfWorkManager uowManager,
        IMapper mapper,
        ILogger<ImportInvoicesHandler> logger)
    {
        _issuedInvoiceSource = issuedInvoiceSource;
        _issuedInvoiceClient = issuedInvoiceClient;
        _repository = repository;
        _importTransformations = importTransformations;
        _uowManager = uowManager;
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
                var result = await ExecuteImportInvoice(invoiceDetail, cancellationToken);

                if (!result.IsSuccess)
                {
                    error = true;
                    resultDto.Failed.Add(invoiceDetail.Code);
                }
                else
                {
                    resultDto.Succeeded.Add(invoiceDetail.Code);
                }
            }

            if (error)
                await _issuedInvoiceSource.FailAsync(batch);
            else
                await _issuedInvoiceSource.CommitAsync(batch);
        }

        return resultDto;
    }

    private async Task<OperationResult<OperationResultDetail>> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
        using (var uow = _uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            try
            {
                _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

                var invoice = await GetOrCreateAsync(invoiceDetail.Code, () => _mapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

                var flexiInvoice = _mapper.Map<IssuedInvoiceDetail, Rem.FlexiBeeSDK.Model.Invoices.IssuedInvoiceDetailFlexiDto>(invoiceDetail);
                foreach (var transformation in _importTransformations)
                {
                    flexiInvoice = await transformation.TransformAsync(flexiInvoice, cancellationToken);
                }
                var result = await _issuedInvoiceClient.SaveAsync(flexiInvoice, cancellationToken);

                if (result.IsSuccess)
                {
                    invoice.SyncSucceeded(flexiInvoice);
                    _logger.LogInformation(
                        "Successfully imported invoice: {InvoiceNumber}: {InvoiceValue} ({Currency})",
                        invoiceDetail.Code, invoiceDetail.Price.WithVat, invoiceDetail.Price.CurrencyCode);
                }
                else
                {
                    var flexiError = result.Result?.Results?.FirstOrDefault()?.Errors?.FirstOrDefault();
                    if (flexiError != null)
                    {
                        var invoiceError = _mapper.Map<Rem.FlexiBeeSDK.Model.Error, IssuedInvoiceError>(flexiError);
                        invoice.SyncFailed(flexiInvoice, invoiceError);
                    }
                    else
                    {
                        invoice.SyncFailed(flexiInvoice, "Error not specified");
                    }
                    _logger.LogError("Failed to import invoice: {InvoiceNumber}: {Error}", invoiceDetail.Code, result.ErrorMessage);
                }

                await _repository.UpdateAsync(invoice, true, cancellationToken);
                return result;
            }
            finally
            {
                await uow.CompleteAsync(cancellationToken);
            }
        }
    }

    private async Task<IssuedInvoice> GetOrCreateAsync(string key, Func<IssuedInvoice> factory, CancellationToken cancellationToken = default)
    {
        var found = await _repository.FindAsync(key, true, cancellationToken);
        if (found == null)
        {
            found = factory();
            await _repository.InsertAsync(found, true, cancellationToken);
        }

        return found;
    }
}