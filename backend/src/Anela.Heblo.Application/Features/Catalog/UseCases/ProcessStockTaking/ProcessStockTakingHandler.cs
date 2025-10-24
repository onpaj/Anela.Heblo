using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.ProcessStockTaking;

public class ProcessStockTakingHandler : IRequestHandler<ProcessStockTakingRequest, ProcessStockTakingResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<ProcessStockTakingHandler> _logger;
    private readonly IEshopStockDomainService _eshopStockDomainService;
    private readonly IMapper _mapper;

    public ProcessStockTakingHandler(
        ICatalogRepository catalogRepository,
        ILogger<ProcessStockTakingHandler> logger,
        IEshopStockDomainService eshopStockDomainService,
        IMapper mapper)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
        _eshopStockDomainService = eshopStockDomainService;
        _mapper = mapper;
    }

    [DisplayName("Inventarizace-{0}-{1}")]
    public async Task<ProcessStockTakingResponse> Handle(ProcessStockTakingRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing stock taking for product code {ProductCode} with target amount {TargetAmount}",
            request.ProductCode, request.TargetAmount);

        try
        {
            // Map request to domain model
            var eshopRequest = new EshopStockTakingRequest
            {
                ProductCode = request.ProductCode,
                TargetAmount = request.TargetAmount,
                SoftStockTaking = request.SoftStockTaking
            };

            // Call domain service to submit stock taking
            var stockTakingRecord = await _eshopStockDomainService.SubmitStockTakingAsync(eshopRequest);

            // Check if the stock taking operation failed
            if (!string.IsNullOrEmpty(stockTakingRecord.Error))
            {
                _logger.LogWarning("Stock taking failed for product code {ProductCode}. Error: {Error}",
                    request.ProductCode, stockTakingRecord.Error);

                var errorResult = new StockTakingResultDto
                {
                    Id = stockTakingRecord.Id.ToString(),
                    Type = stockTakingRecord.Type.ToString(),
                    Code = stockTakingRecord.Code,
                    AmountNew = stockTakingRecord.AmountNew,
                    AmountOld = stockTakingRecord.AmountOld,
                    Date = stockTakingRecord.Date,
                    User = stockTakingRecord.User ?? string.Empty,
                    Error = stockTakingRecord.Error ?? string.Empty
                };

                return new ProcessStockTakingResponse { Result = errorResult };
            }

            _logger.LogInformation("Stock taking processed successfully for product code {ProductCode}. Record ID: {RecordId}",
                request.ProductCode, stockTakingRecord.Id);

            var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
            if (product != null)
            {
                // Use the SyncStockTaking method to update stock and add to history
                product.SyncStockTaking(stockTakingRecord);
                // Note: Catalog repository is read-only, changes will be persisted through other mechanisms
            }

            // Map domain result to response
            var result = _mapper.Map<StockTakingResultDto>(stockTakingRecord);
            return new ProcessStockTakingResponse { Result = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process stock taking for product code {ProductCode}", request.ProductCode);
            
            var errorResult = new StockTakingResultDto
            {
                Code = request.ProductCode,
                Error = ex.Message,
                Date = DateTime.UtcNow
            };

            return new ProcessStockTakingResponse { Result = errorResult };
        }
    }
}