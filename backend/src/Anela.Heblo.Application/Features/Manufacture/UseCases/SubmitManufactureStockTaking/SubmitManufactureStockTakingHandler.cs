using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;

public class SubmitManufactureStockTakingHandler : IRequestHandler<SubmitManufactureStockTakingRequest, SubmitManufactureStockTakingResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<SubmitManufactureStockTakingHandler> _logger;
    private readonly IErpStockDomainService _erpStockDomainService;

    public SubmitManufactureStockTakingHandler(
        ICatalogRepository catalogRepository,
        ILogger<SubmitManufactureStockTakingHandler> logger,
        IErpStockDomainService erpStockDomainService)
    {
        _catalogRepository = catalogRepository;
        _logger = logger;
        _erpStockDomainService = erpStockDomainService;
    }

    public async Task<SubmitManufactureStockTakingResponse> Handle(SubmitManufactureStockTakingRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Submitting manufacture stock taking for product code {ProductCode} with target amount {TargetAmount}",
            request.ProductCode, request.TargetAmount);

        try
        {
            // Get the current product to check if it's a material
            var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("Product with code {ProductCode} not found", request.ProductCode);
                return new SubmitManufactureStockTakingResponse(ErrorCodes.ProductNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            // Verify this is a material (manufacture inventory only handles materials)
            if (product.Type != ProductType.Material)
            {
                _logger.LogWarning("Product {ProductCode} is not a material (Type: {ProductType}). Manufacture stock taking only supports materials.",
                    request.ProductCode, product.Type);
                return new SubmitManufactureStockTakingResponse(ErrorCodes.InvalidOperation,
                    new Dictionary<string, string>
                    {
                        { "ProductCode", request.ProductCode },
                        { "ProductType", product.Type.ToString() },
                        { "Message", "Manufacture stock taking only supports materials" }
                    });
            }

            // Create ERP stock taking request - validation exceptions should propagate
            var erpRequest = new ErpStockTakingRequest
            {
                ProductCode = request.ProductCode,
                RemoveMissingLots = true,
                DryRun = false,
                StockTakingItems = CreateStockTakingItems(request, product)
            };

            // Call ERP domain service to submit stock taking
            var stockTakingRecord = await _erpStockDomainService.SubmitStockTakingAsync(erpRequest);

            // Check if the stock taking operation failed
            if (!string.IsNullOrEmpty(stockTakingRecord.Error))
            {
                _logger.LogWarning("Manufacture stock taking failed for product code {ProductCode}. Error: {Error}",
                    request.ProductCode, stockTakingRecord.Error);

                return new SubmitManufactureStockTakingResponse(ErrorCodes.StockTakingFailed,
                    new Dictionary<string, string>
                    {
                        { "ProductCode", request.ProductCode },
                        { "Error", stockTakingRecord.Error }
                    });
            }

            _logger.LogInformation("Manufacture stock taking submitted successfully for product code {ProductCode}. Record ID: {RecordId}",
                request.ProductCode, stockTakingRecord.Id);

            product.SyncStockTaking(stockTakingRecord);

            // Map domain result to response
            return new SubmitManufactureStockTakingResponse
            {
                Id = stockTakingRecord.Id.ToString(),
                Type = stockTakingRecord.Type.ToString(),
                Code = stockTakingRecord.Code,
                AmountNew = stockTakingRecord.AmountNew,
                AmountOld = stockTakingRecord.AmountOld,
                Date = stockTakingRecord.Date,
                User = stockTakingRecord.User,
                Error = stockTakingRecord.Error
            };
        }
        catch (ArgumentException)
        {
            // Let validation ArgumentExceptions propagate - these are expected for invalid input
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit manufacture stock taking for product code {ProductCode}", request.ProductCode);
            return new SubmitManufactureStockTakingResponse(ErrorCodes.InternalServerError,
                new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
        }
    }

    private List<ErpStockTakingLot> CreateStockTakingItems(SubmitManufactureStockTakingRequest request, CatalogAggregate product)
    {
        // Product with lots (HasLots = true) - must use lot-based stock taking
        if (product.HasLots)
        {
            if (request.Lots == null || request.Lots.Count == 0)
            {
                throw new ArgumentException($"Product {request.ProductCode} has lots but no lots were provided in the request. For lot-based materials, lots are required.");
            }

            _logger.LogInformation("Creating lot-based stock taking items for product {ProductCode} with {LotCount} lots",
                request.ProductCode, request.Lots.Count);

            return request.Lots.Select(lotDto => new ErpStockTakingLot
            {
                LotCode = lotDto.LotCode,
                Expiration = lotDto.Expiration,
                Amount = lotDto.Amount,
                SoftStockTaking = lotDto.SoftStockTaking
            }).ToList();
        }

        // Product without lots (HasLots = false) - use simple stock taking with TargetAmount
        if (!request.TargetAmount.HasValue)
        {
            throw new ArgumentException($"Product {request.ProductCode} does not have lots but no TargetAmount was provided. For simple materials, TargetAmount is required.");
        }

        _logger.LogInformation("Creating simple stock taking item for product {ProductCode} with amount {TargetAmount}",
            request.ProductCode, request.TargetAmount.Value);

        return new List<ErpStockTakingLot>
        {
            new ErpStockTakingLot
            {
                Amount = request.TargetAmount.Value,
                SoftStockTaking = request.SoftStockTaking
            }
        };
    }
}