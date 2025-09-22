using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Invoices.Transformations;
using Anela.Heblo.IssuedInvoices;
using Anela.Heblo.IssuedInvoices.Model;
using Anela.Heblo.Xcc.Persistance;
using AutoMapper.Internal.Mappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices;
using Rem.FlexiBeeSDK.Model.Response;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using IssuedInvoice = Anela.Heblo.IssuedInvoices.IssuedInvoice;
using IBankClient = Rem.FlexiBeeSDK.Client.Clients.Banks.IBankClient;
using IIssuedInvoiceSource = Anela.Heblo.Invoices.IIssuedInvoiceSource;
using IssuedInvoiceDetail = Anela.Heblo.IssuedInvoices.IssuedInvoiceDetail;

namespace Anela.Heblo.Application.Features.IssuedInvoices;


public class IssuedInvoiceAppService
{
    private readonly IRepository<IssuedInvoice, long> _repository;
    private readonly IIssuedInvoiceSource _issuedInvoiceSource;
    private readonly IIssuedInvoiceClient _issuedInvoiceClient;
    private readonly ICashRegisterOrdersSource _cashRegisterOrdersSource;
    private readonly IBankClient _bankClient;
    private readonly IEnumerable<IIssuedInvoiceImportTransformation> _importTransformations;
    private readonly ILogger<IssuedInvoiceAppService> _logger;

    public IssuedInvoiceAppService(
        IRepository<IssuedInvoice, long> repository,
        IIssuedInvoiceSource issuedInvoiceSource,
        IIssuedInvoiceClient issuedInvoiceClient,
        ICashRegisterOrdersSource cashRegisterOrdersSource,
        IBankClient bankClient,
        IEnumerable<IIssuedInvoiceImportTransformation> importTransformations,
        ILogger<IssuedInvoiceAppService> logger
    )
    {
        _repository = repository;
        _issuedInvoiceSource = issuedInvoiceSource;
        _issuedInvoiceClient = issuedInvoiceClient;
        _cashRegisterOrdersSource = cashRegisterOrdersSource;
        _bankClient = bankClient;
        _importTransformations = importTransformations;
        _logger = logger;
    }


    
    public async Task<List<string>> EnqueueImportInvoiceAsync(ImportInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.InvoiceIds.Any())
        {
            var query = ObjectMapper.Map<ImportInvoiceRequestDto, IssuedInvoiceRequest>(request);
            return await EnqueueImportByDate(request, query);
        }

        var jobIds = new List<string>();
        foreach(var i in request.InvoiceIds)
        {
            jobIds.Add(await _jobManager.EnqueueAsync(new IssuedInvoiceSingleImportArgs(i, request.Currency ?? "CZK")));
        }

        return jobIds;
    }

    public async Task<ImportResultDto> ImportInvoiceAsync(IssuedInvoiceRequest query, CancellationToken cancellationToken = default)
    {
        var batches = await _issuedInvoiceSource.GetAllAsync(query);

        var resultDto = new ImportResultDto()
        {
            RequestId = query.RequestId,
        };
        
        foreach (var batch in batches)
        {
            bool error = false;

            _logger.LogInformation("Importing batch: {BatchId}", batch.BatchId);
            foreach (var f in batch.Invoices)
            {
                var result = await ExecuteImportInvoice(f, cancellationToken);
                // if (!result.IsSuccess && result.ErrorType == ErrorType.InvoicePaired && request.TryUnpairIfNecessary)
                // {
                //     result = await UnpairInvoice(f.Code, cancellationToken);
                //     if(result.IsSuccess)
                //         result = await ImportInvoice(f, cancellationToken); // 2nd try
                // }

                if (!result.IsSuccess)
                {
                    error = true;
                    resultDto.Failed.Add(f.Code);
;                }
                else
                {
                    resultDto.Succeeded.Add(f.Code);
                }
            }

            if (error)
                await _issuedInvoiceSource.FailAsync(batch);
            else
                await _issuedInvoiceSource.CommitAsync(batch);
        }

        return resultDto;
    }
   
    
    public async Task<List<CashRegisterOrderResult>> GetCashRegisterOrdersAsync(CashRegistryRequestDto request, CancellationToken cancellationToken = default)
    {
        return await _cashRegisterOrdersSource.GetAllAsync(request);
    }
    
    private async Task<OperationResult<OperationResultDetail>> UnpairInvoice(string invoiceCode, CancellationToken cancellationToken = default)
    {
        var invoice = await _issuedInvoiceClient.GetAsync(invoiceCode, cancellationToken);

        var result = new OperationResult<OperationResultDetail>(HttpStatusCode.OK);
        foreach (var p in invoice.GetBankPaymentsIds())
        {
            result = await _bankClient.UnPairPayment(p, cancellationToken);
        }

        return result;
    }



    private async Task<OperationResult<OperationResultDetail>> ExecuteImportInvoice(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default)
    {
                _logger.LogInformation("Importing invoice: {InvoiceNumber}", invoiceDetail.Code);

                var invoice = await GetOrCreateAsync(invoiceDetail.Code, () => ObjectMapper.Map<IssuedInvoiceDetail, IssuedInvoice>(invoiceDetail), cancellationToken);

                var flexiInvoice = ObjectMapper.Map<IssuedInvoiceDetail, Rem.FlexiBeeSDK.Model.Invoices.IssuedInvoiceDetailFlexiDto>(invoiceDetail);
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
                        var invoiceError = ObjectMapper.Map<JSType.Error, IssuedInvoiceError>(flexiError);
                        invoice.SyncFailed(flexiInvoice, invoiceError);
                    }
                    else
                    {
                        invoice.SyncFailed(flexiInvoice, "Error not specified");
                    }
                    _logger.LogError("Failed to import invoice: {InvoiceNumber}: {Error}", invoiceDetail.Code, result.ErrorMessage);
                }

                await Repository.UpdateAsync(invoice, true, cancellationToken);
                return result;
    }

    private Task<IssuedInvoiceResponse> EnqueueImportByDate(IssuedInvoiceRequest request)
    {
        var taskIds = new List<string>();
        if (!request.DateFrom.HasValue)
            throw new NullReferenceException($"{nameof(request.DateFrom)} must have value");
        if (!request.DateTo.HasValue)
            throw new NullReferenceException($"{nameof(request.DateTo)} must have value");
        if (request.DateTo < request.DateFrom)
            throw new NullReferenceException($"{nameof(request.DateTo)} must be later than {nameof(request.DateFrom)}");
        if ((request.DateTo.Value - request.DateFrom.Value).TotalDays > 14)
            throw new NullReferenceException($"Interval is too large, add 14 day at most");


        for (var day = request.DateFrom.Value.Date; day < request.DateTo.Value.Date; day = day.AddDays(1))
        {
            // TODO add import task to background task worker
            //tasksId.Add(await _jobManager.EnqueueAsync(new IssuedInvoiceDailyImportArgs(day, query.Currency ?? "CZK")));
        }

        return Task.FromResult(new IssuedInvoiceResponse()
        {
            TaskIds = taskIds,
        });
    }
}

public class IssuedInvoiceResponse : BaseResponse
{
    public List<string> TaskIds { get; set; } = new();
}

public interface IIssuedInvoiceClient
{
    Task<IssuedInvoiceDetailFlexiDto> GetAsync(string code, CancellationToken cancellationToken = default);
        
    Task<OperationResult<OperationResultDetail>> SaveAsync(IssuedInvoiceDetailFlexiDto invoice, CancellationToken cancellationToken = default);
}

public interface ICashRegisterOrdersSource
{
    Task<List<CashRegisterOrderResult>> GetAllAsync(CashRegistryRequestDto query);
}