using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureClient : IManufactureClient
{
    private const string WarehouseDocumentTypeSemiProduct = "VYROBA-POLOTOVAR";
    private const string WarehouseDocumentTypeProduct = "VYROBA-PRODUKT";
    private readonly IErpStockClient _stockClient;
    private readonly IStockItemsMovementClient _stockMovementClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlexiManufactureClient> _logger;

    public FlexiManufactureClient(
        IErpStockClient stockClient,
        IStockItemsMovementClient stockMovementClient,
        TimeProvider timeProvider,
        ILogger<FlexiManufactureClient> logger)
    {
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
        _timeProvider = timeProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting manufacture consumption movement. ManufactureOrderCode: {ManufactureOrderCode}, Type: {ManufactureType}, ItemsCount: {ItemsCount}",
            request.ManufactureOrderCode, request.ManufactureType, request.Items.Count);

        try
        {
            // Validate items
            var validItems = request.Items.Where(w => w.Amount > 0).ToList();
            if (validItems.Count == 0)
            {
                _logger.LogError("No valid items to consume for manufacture order {ManufactureOrderCode}", request.ManufactureOrderCode);
                throw new ManufactureSubmissionFailedException(
                    "No valid items to consume - all amounts are zero or negative",
                    request.ManufactureOrderCode);
            }

            // Read current stock quantities for materials to get unit prices
            var stockItems = await _stockClient.StockToDateAsync(request.Date, FlexiStockClient.MaterialWarehouseId, cancellationToken);

            // Create stock movement request for consumption (materials OUT from inventory → WIP)
            var stockMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
            {
                CreatedBy = request.CreatedBy,
                AccountingDate = request.Date,
                IssueDate = request.Date,
                StockItems = validItems.Select(item =>
                {
                    var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == item.ProductCode);
                    var unitPrice = stockItem?.Price ?? 0;

                    return new StockItemsMovementUpsertRequestItemFlexiDto()
                    {
                        ProductCode = item.ProductCode,
                        ProductName = item.ProductName,
                        Amount = (double)item.Amount,
                        AmountIssued = (double)item.Amount,
                        LotNumber = request.LotNumber,
                        Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                        UnitPrice = (double)unitPrice,
                    };
                }).ToList(),
                Description = request.ManufactureOrderCode,
                DocumentTypeCode = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseDocumentTypeSemiProduct : WarehouseDocumentTypeProduct,
                StockMovementDirection = StockMovementDirection.Out, // Consumption = OUT movement
                Note = request.ManufactureInternalNumber,
                WarehouseId = FlexiStockClient.MaterialWarehouseId.ToString(),
            };

            _logger.LogDebug("Creating consumption stock movement for manufacture order {ManufactureOrderCode}. ItemsCount: {ItemsCount}",
                request.ManufactureOrderCode, stockMovementRequest.StockItems.Count);

            // Create consumption movement
            var stockMovementResult = await _stockMovementClient.SaveAsync(stockMovementRequest, cancellationToken);

            if (!stockMovementResult.IsSuccess)
            {
                var errorMessage = stockMovementResult.GetErrorMessage();
                _logger.LogError("Failed to create consumption movement for manufacture order {ManufactureOrderCode}: {Error}",
                    request.ManufactureOrderCode, errorMessage);
                throw new ConsumptionMovementFailedException(
                    errorMessage,
                    request.ManufactureOrderCode);
            }

            // Retrieve created document reference
            var newDocumentIdString = stockMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(newDocumentIdString))
            {
                _logger.LogError("No document ID returned from consumption movement for manufacture order {ManufactureOrderCode}", request.ManufactureOrderCode);
                throw new ConsumptionMovementFailedException(
                    "No document ID returned from FlexiBee API",
                    request.ManufactureOrderCode);
            }

            if (!int.TryParse(newDocumentIdString, out var newDocumentId))
            {
                _logger.LogError("Failed to parse document ID '{DocumentId}' for manufacture order {ManufactureOrderCode}",
                    newDocumentIdString, request.ManufactureOrderCode);
                throw new ConsumptionMovementFailedException(
                    $"Invalid document ID format returned from FlexiBee: {newDocumentIdString}",
                    request.ManufactureOrderCode);
            }

            var document = await _stockMovementClient.GetAsync(newDocumentId);
            var movementReference = document.FirstOrDefault()?.Document.DocumentCode;

            if (string.IsNullOrEmpty(movementReference))
            {
                _logger.LogError("No movement reference returned for consumption movement {DocumentId}", newDocumentId);
                throw new ConsumptionMovementFailedException(
                    $"No movement reference returned for document ID: {newDocumentId}",
                    request.ManufactureOrderCode);
            }

            _logger.LogInformation("Successfully created consumption movement {MovementReference} for manufacture order {ManufactureOrderCode}",
                movementReference, request.ManufactureOrderCode);

            // Step 2: Create production movement (finished goods IN to inventory)
            _logger.LogDebug("Starting production movement creation for manufacture order {ManufactureOrderCode}", request.ManufactureOrderCode);

            // Determine target warehouse based on manufacture type
            var targetWarehouseId = request.ManufactureType == ErpManufactureType.SemiProduct
                ? FlexiStockClient.SemiProductsWarehouseId
                : FlexiStockClient.ProductsWarehouseId;

            // Read stock for unit price calculation (use cost from consumption for production)
            var productionStockItems = await _stockClient.StockToDateAsync(request.Date, FlexiStockClient.MaterialWarehouseId, cancellationToken);

            // Create production movement request (finished goods IN to inventory)
            var productionMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
            {
                CreatedBy = request.CreatedBy,
                AccountingDate = request.Date,
                IssueDate = request.Date,
                StockItems = validItems.Select(item =>
                {
                    // Calculate unit price from consumed materials
                    var materialCost = stockItems.Where(s => validItems.Any(v => v.ProductCode == s.ProductCode))
                        .Sum(s => s.Price * s.Stock);
                    var totalProducedQuantity = validItems.Sum(v => v.Amount);
                    var unitPrice = totalProducedQuantity > 0 ? materialCost / totalProducedQuantity : 0;

                    return new StockItemsMovementUpsertRequestItemFlexiDto()
                    {
                        ProductCode = item.ProductCode,
                        ProductName = item.ProductName,
                        Amount = (double)item.Amount,
                        AmountIssued = (double)item.Amount,
                        LotNumber = request.LotNumber,
                        Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                        UnitPrice = (double)unitPrice,
                    };
                }).ToList(),
                Description = request.ManufactureOrderCode,
                DocumentTypeCode = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseDocumentTypeSemiProduct : WarehouseDocumentTypeProduct,
                StockMovementDirection = StockMovementDirection.In, // Production = IN movement
                Note = request.ManufactureInternalNumber,
                WarehouseId = targetWarehouseId.ToString(),
            };

            _logger.LogDebug("Creating production stock movement for manufacture order {ManufactureOrderCode}. Target warehouse: {WarehouseId}, ItemsCount: {ItemsCount}",
                request.ManufactureOrderCode, targetWarehouseId, productionMovementRequest.StockItems.Count);

            // Create production movement
            var productionMovementResult = await _stockMovementClient.SaveAsync(productionMovementRequest, cancellationToken);

            if (!productionMovementResult.IsSuccess)
            {
                var errorMessage = productionMovementResult.GetErrorMessage();
                _logger.LogError("Failed to create production movement for manufacture order {ManufactureOrderCode}: {Error}. Consumption movement was created: {ConsumptionMovementReference}",
                    request.ManufactureOrderCode, errorMessage, movementReference);
                throw new ProductionMovementFailedException(
                    errorMessage,
                    request.ManufactureOrderCode,
                    movementReference);
            }

            // Retrieve production document reference
            var productionDocumentIdString = productionMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(productionDocumentIdString))
            {
                _logger.LogError("No document ID returned from production movement for manufacture order {ManufactureOrderCode}. Consumption movement: {ConsumptionMovementReference}",
                    request.ManufactureOrderCode, movementReference);
                throw new ProductionMovementFailedException(
                    "No document ID returned from FlexiBee API",
                    request.ManufactureOrderCode,
                    movementReference);
            }

            if (!int.TryParse(productionDocumentIdString, out var productionDocumentId))
            {
                _logger.LogError("Failed to parse production document ID '{DocumentId}' for manufacture order {ManufactureOrderCode}. Consumption movement: {ConsumptionMovementReference}",
                    productionDocumentIdString, request.ManufactureOrderCode, movementReference);
                throw new ProductionMovementFailedException(
                    $"Invalid document ID format returned from FlexiBee: {productionDocumentIdString}",
                    request.ManufactureOrderCode,
                    movementReference);
            }

            var productionDocument = await _stockMovementClient.GetAsync(productionDocumentId);
            var productionMovementReference = productionDocument.FirstOrDefault()?.Document.DocumentCode;

            if (string.IsNullOrEmpty(productionMovementReference))
            {
                _logger.LogError("No movement reference returned for production movement {DocumentId}. Consumption movement: {ConsumptionMovementReference}",
                    productionDocumentId, movementReference);
                throw new ProductionMovementFailedException(
                    $"No movement reference returned for document ID: {productionDocumentId}",
                    request.ManufactureOrderCode,
                    movementReference);
            }

            _logger.LogInformation("Successfully created both consumption movement {ConsumptionMovementReference} and production movement {ProductionMovementReference} for manufacture order {ManufactureOrderCode}",
                movementReference, productionMovementReference, request.ManufactureOrderCode);

            // Return consumption movement reference (maintaining backward compatibility)
            return movementReference;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit manufacture movements for order {ManufactureOrderCode}: {ErrorMessage}",
                request.ManufactureOrderCode, ex.Message);
            throw;
        }
    }

    public async Task<DiscardResidualSemiProductResponse> DiscardResidualSemiProductAsync(DiscardResidualSemiProductRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting residual semi-product discard process. ManufactureOrderCode: {ManufactureOrderCode}, SemiProductCode: {SemiProductCode}, MaxAutoDiscardQuantity: {MaxAutoDiscardQuantity}",
            request.ManufactureOrderCode, request.ProductCode, request.MaxAutoDiscardQuantity);

        try
        {
            // Step 1: Read current stock quantity for the semi-product
            var stockItems = await _stockClient.StockToDateAsync(request.CompletionDate, FlexiStockClient.SemiProductsWarehouseId, cancellationToken);
            var semiProductStock = stockItems.FirstOrDefault(s => s.ProductCode == request.ProductCode);

            if (semiProductStock == null)
            {
                _logger.LogDebug("No stock found for semi-product {SemiProductCode} in warehouse {WarehouseId}",
                    request.ProductCode, FlexiStockClient.SemiProductsWarehouseId);

                return new DiscardResidualSemiProductResponse
                {
                    Success = true,
                    QuantityFound = 0,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = false,
                    Details = "Nebylo nalezeno žádné zbytkové množství - žádná akce není potřeba"
                };
            }

            var currentQuantity = (double)semiProductStock.Stock;

            _logger.LogDebug("Found {CurrentQuantity} units of semi-product {SemiProductCode} in stock",
                currentQuantity, request.ProductCode);


            if (currentQuantity > request.MaxAutoDiscardQuantity)
            {
                _logger.LogWarning("Residual quantity {CurrentQuantity} exceeds auto-discard limit {MaxAutoDiscardQuantity} for product {SemiProductCode}",
                    currentQuantity, request.MaxAutoDiscardQuantity, request.ProductCode);

                return new DiscardResidualSemiProductResponse
                {
                    Success = false,
                    QuantityFound = currentQuantity,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = true,
                    Details = $"Množství {currentQuantity} překračuje limit pro automatické vyřazení {request.MaxAutoDiscardQuantity} - vyžaduje ruční schválení"
                };
            }


            // Step 3: Create stock movement for discard (stock reduction)
            _logger.LogDebug("Creating stock movement to discard {CurrentQuantity} units of {SemiProductCode}",
                currentQuantity, request.ProductCode);
            string? movementReference;
            try
            {
                var stockMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
                {
                    CreatedBy = request.CompletedBy,
                    AccountingDate = _timeProvider.GetLocalNow().DateTime,
                    IssueDate = _timeProvider.GetLocalNow().DateTime,
                    StockItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>()
                    {
                        new()
                        {
                            ProductCode = request.ProductCode,
                            ProductName = request.ProductName,
                            Amount = currentQuantity,
                            AmountIssued = currentQuantity,
                            LotNumber = null,
                            Expiration = null,
                            UnitPrice = (double)semiProductStock.Price,
                        }
                    },
                    Description = request.ManufactureOrderCode,
                    DocumentTypeCode = WarehouseDocumentTypeSemiProduct,
                    StockMovementDirection = StockMovementDirection.Out,
                    Note = request.ManufactureOrderCode,
                    WarehouseId = FlexiStockClient.SemiProductsWarehouseId.ToString(),
                };

                var stockMovementResult = await _stockMovementClient.SaveAsync(stockMovementRequest, cancellationToken);
                if (!stockMovementResult.IsSuccess)
                {
                    return new DiscardResidualSemiProductResponse
                    {
                        Success = false,
                        QuantityFound = currentQuantity,
                        QuantityDiscarded = 0,
                        RequiresManualApproval = true,
                        ErrorMessage = $"Selhalo vytvoření skladového pohybu: {stockMovementResult.GetErrorMessage()}",
                        Details = "Vytvoření skladového pohybu selhalo - vyžaduje ruční schválení"
                    };
                }
                var newDocumentIdString = stockMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
                var newDocumentId = Int32.Parse(newDocumentIdString);
                var document = await _stockMovementClient.GetAsync(newDocumentId);
                movementReference = document.FirstOrDefault()?.Document.DocumentCode;

                _logger.LogDebug("Successfully created stock movement {MovementReference} for discard", movementReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create stock movement for discard, but continuing with discard process");
                return new DiscardResidualSemiProductResponse
                {
                    Success = false,
                    QuantityFound = currentQuantity,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = true,
                    ErrorMessage = $"Selhalo vytvoření skladového pohybu: {ex.Message}",
                    Details = "Vytvoření skladového pohybu selhalo - vyžaduje ruční schválení"
                };
            }

            _logger.LogInformation("Successfully discarded {CurrentQuantity} units of semi-product {SemiProductCode} for manufacture order {ManufactureOrderCode}",
                currentQuantity, request.ProductCode, request.ManufactureOrderCode);

            return new DiscardResidualSemiProductResponse
            {
                Success = true,
                QuantityFound = currentQuantity,
                QuantityDiscarded = currentQuantity,
                RequiresManualApproval = false,
                StockMovementReference = movementReference,
                Details = $"Úspěšně automaticky vyřazeno {currentQuantity} kusů"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discard residual semi-product for manufacture order {ManufactureOrderCode}: {ErrorMessage}",
                request.ManufactureOrderCode, ex.Message);

            return new DiscardResidualSemiProductResponse
            {
                Success = false,
                QuantityFound = 0,
                QuantityDiscarded = 0,
                RequiresManualApproval = false,
                ErrorMessage = $"Selhalo vyřazení zbytkového polotovaru: {ex.Message}"
            };
        }
    }


}
