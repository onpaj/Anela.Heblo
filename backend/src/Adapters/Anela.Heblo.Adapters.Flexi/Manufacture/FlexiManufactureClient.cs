using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.IssuedOrders;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureClient : IManufactureClient
{
    private const string DocumentTypeSemiProduct = "VYR-POLOTOVAR";
    private const string DocumentTypeProduct = "VYR-PRODUKT";
    private const string WarehouseDocumentTypeSemiProduct = "VYROBA-POLOTOVAR";
    private const string WarehouseDocumentTypeProduct = "VYROBA-PRODUKT";
    private const string WarehouseCodeSemiProduct = "POLOTOVARY";
    private const string WarehouseCodeProduct = "ZBOZI";
    
    private readonly IIssuedOrdersClient _ordersClient;
    private readonly IErpStockClient _stockClient;
    private readonly IStockItemsMovementClient _stockMovementClient;
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlexiManufactureClient> _logger;

    public FlexiManufactureClient(
        IIssuedOrdersClient ordersClient,
        IErpStockClient stockClient,
        IStockItemsMovementClient stockMovementClient,
        IBoMClient bomClient,
        IProductSetsClient productSetsClient,
        TimeProvider timeProvider,
        ILogger<FlexiManufactureClient> logger)
    {
        _ordersClient = ordersClient ?? throw new ArgumentNullException(nameof(ordersClient));
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
        _timeProvider = timeProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {

        _logger.LogDebug("Starting manufacture order submission. ManufactureOrderId: {ManufactureOrderId}, Type: {ManufactureType}, ItemsCount: {ItemsCount}",
            request.ManufactureOrderCode, request.ManufactureType, request.Items.Count);

        try
        {
            // Map request to FlexiBee DTO
            var createOrder = new CreateIssuedOrderFlexiDto()
            {
                DepartmentCode = "C",
                OrderInternalNumber = request.ManufactureInternalNumber,
                DocumentType = request.ManufactureType == ErpManufactureType.SemiProduct ? DocumentTypeSemiProduct : DocumentTypeProduct,
                WarehouseDocumentType = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseDocumentTypeSemiProduct : WarehouseDocumentTypeProduct,
                DateCreated = request.Date,
                DateVat = request.Date,
                CreatedBy = request.CreatedBy,
                User = request.CreatedBy,
                Note = request.ManufactureOrderCode,
                Description = request.ManufactureOrderCode,
                Items = request.Items.Where(w => w.Amount > 0)
                    .Select(s => MapToFlexiItem(s, request)).ToList()
            };

            _logger.LogDebug("Mapped request to CreateIssuedOrderFlexiDto. DocumentType: {DocumentType}, ItemsCount: {ItemsCount}",
                createOrder.DocumentType, createOrder.Items?.Count ?? 0);

            // Create the issued order
            var result = await _ordersClient.SaveAsync(createOrder, cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to create manufacture order {ManufactureOrderId}: {Error}", request.ManufactureOrderCode, result.GetErrorMessage());
                throw new InvalidOperationException($"Failed to create issued order: {result.GetErrorMessage()}");
            }

            var firstResult = result.Result.Results.First();
            if (firstResult?.Id == null)
            {
                _logger.LogError("SaveAsync returned result with null Id for manufacture order {ManufactureOrderId}", request.ManufactureOrderCode);
                throw new InvalidOperationException("Failed to create issued order - no ID returned");
            }

            if (!int.TryParse(firstResult.Id.ToString(), out var orderId))
            {
                _logger.LogError("Failed to parse order ID '{OrderId}' for manufacture order {ManufactureOrderId}",
                    firstResult.Id, request.ManufactureOrderCode);
                throw new InvalidOperationException($"Failed to parse order ID: {firstResult.Id}");
            }

            _logger.LogDebug("Successfully created issued order with ID {OrderId} for manufacture order {ManufactureOrderId}",
                orderId, request.ManufactureOrderCode);

            var savedOrder = await _ordersClient.GetAsync(orderId, cancellationToken);

            // Finalize the order
            var finalizeOrder = new FinalizeIssuedOrderFlexiDto(orderId)
            {
                FinalizeStockMovement = new IssuedOrderStockMovementFlexiDto()
                {
                    Items = savedOrder.Items.Select(s => new FinalizeIssuedOrderItemFlexiDto()
                    {
                        Id = s.Id,
                        Amount = s.Amount,
                        LotNumber = request.LotNumber,
                        ExpirationDate = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    }).ToList(),
                    WarehouseDocumentType = createOrder.WarehouseDocumentType,
                    WarehouseCode = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseCodeSemiProduct : WarehouseCodeProduct,
                }
            };

            _logger.LogDebug("Finalizing issued order {OrderId}", orderId);


            var finalizeResult = await _ordersClient.FinalizeAsync(finalizeOrder, cancellationToken);

            if (!finalizeResult.IsSuccess)
            {
                var shortenedMessage = finalizeResult.GetErrorMessage()?.Split("Could not execute JDBC batch update")
                    .First();
                _logger.LogError("FinalizeAsync failed for order {OrderId}: {ErrorMessage}", orderId, shortenedMessage);
                throw new InvalidOperationException($"Failed to finalize issued order {orderId}: {shortenedMessage}");
            }

            _logger.LogDebug("Successfully finalized issued order {OrderId}", orderId);

            return savedOrder.Code;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit manufacture order {ManufactureOrderId}: {ErrorMessage}",
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
    
     public async Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        var bom = await _bomClient.GetAsync(id, cancellationToken);

        var header = bom.SingleOrDefault(s => s.Level == 1) ?? throw new ApplicationException(message: $"No BoM header for product {id} found");
        var ingredients = bom.Where(w => w.Level != 1);

        var template = new ManufactureTemplate()
        {
            TemplateId = header.Id,
            ProductCode = header.IngredientCode.RemoveCodePrefix(),
            ProductName = header.IngredientFullName,
            Amount = header.Amount,
            OriginalAmount = header.Amount,
            Ingredients = ingredients.Select(s =>
            {
                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = s.IngredientCode.RemoveCodePrefix(),
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s)
                };
            }).ToList(),
        };

        if (ingredients.Any(a => a.Ingredient.Any(b => b.ProductTypeId == (int)ProductType.SemiProduct)))
            template.ManufactureType = ManufactureType.MultiPhase;
        else
            template.ManufactureType = ManufactureType.SinglePhase;

        return template;
    }

    private ProductType ResolveProductType(BoMItemFlexiDto boMItemFlexiDto)
    {
        try
        {
            var productTypeId = boMItemFlexiDto.Ingredient?.FirstOrDefault()?.ProductTypeId;

            // Return UNDEFINED if no ProductTypeId is available
            if (!productTypeId.HasValue)
            {
                return ProductType.UNDEFINED;
            }

            // Check if the value is a valid ProductType enum value
            if (Enum.IsDefined(typeof(ProductType), productTypeId.Value))
            {
                return (ProductType)productTypeId.Value;
            }

            // Return UNDEFINED for unknown enum values
            return ProductType.UNDEFINED;
        }
        catch
        {
            // Return UNDEFINED for any exceptions
            return ProductType.UNDEFINED;
        }
    }

    public async Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken)
    {
        var templates = await _bomClient.GetByIngredientAsync(ingredientCode, cancellationToken);

        return templates
                .Select(s => new ManufactureTemplate()
                {
                    ProductCode = s.ParentCode!.RemoveCodePrefix(),
                    ProductName = s.ParentFullName!,
                    Amount = s.Amount,
                    TemplateId = s.Id,
                    BatchSize = s.Parent?.Amount ?? 0,
                })
        .Where(w => w.ProductCode != ingredientCode)
        .ToList();
    }

    public async Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default)
    {
        var setParts = await _productSetsClient.GetAsync(setProductCode, cancellationToken: cancellationToken);

        return setParts
            .Select(s => new ProductPart()
            {
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                Amount = s.Quantity,
            })
            .ToList();
    }


    private static IssuedOrderItemFlexiDto MapToFlexiItem(SubmitManufactureClientItem item,
        SubmitManufactureClientRequest request)
    {
        if (item.Amount <= 0)
        {
            throw new ArgumentException("Item quantity must be greater than zero", nameof(item));
        }

        return new IssuedOrderItemFlexiDto
        {
            ProductCode = item.ProductCode,
            Name = item.ProductName,
            Amount = (double)item.Amount,
            LotNumber = request.LotNumber,
            ExpirationDate = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            WarehouseCode = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseCodeSemiProduct : WarehouseCodeProduct,
        };
    }
}
