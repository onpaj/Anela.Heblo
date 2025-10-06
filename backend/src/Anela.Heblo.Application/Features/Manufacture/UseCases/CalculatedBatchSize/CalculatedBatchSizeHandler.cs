using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculatedBatchSizeHandler : IRequestHandler<CalculatedBatchSizeRequest, CalculatedBatchSizeResponse>
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly ICatalogRepository _catalogRepository;

    public CalculatedBatchSizeHandler(IManufactureRepository manufactureRepository, ICatalogRepository catalogRepository)
    {
        _manufactureRepository = manufactureRepository;
        _catalogRepository = catalogRepository;
    }

    public async Task<CalculatedBatchSizeResponse> Handle(CalculatedBatchSizeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
            var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            if (product == null)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.ProductNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            template.BatchSize = product.MinimalManufactureQuantity;

            if (template.OriginalAmount <= 0)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.InvalidBatchSize,
                    new Dictionary<string, string> { { "BatchSize", template.BatchSize.ToString() } });
            }

            if (request.DesiredBatchSize == null) // Use MMQ instead
                request.DesiredBatchSize = product.MinimalManufactureQuantity;

            double scaleFactor = (request.DesiredBatchSize / template.OriginalAmount).Value;

            // Create ingredients list with stock information
            var ingredientsWithStock = new List<CalculatedIngredientDto>();
            foreach (var ingredient in template.Ingredients)
            {
                var ingredientCatalog = await _catalogRepository.GetByIdAsync(ingredient.ProductCode, cancellationToken);

                // Get the latest stock taking date for this ingredient
                var lastStockTaking = ingredientCatalog?.StockTakingHistory
                    ?.OrderByDescending(st => st.Date)
                    ?.FirstOrDefault()?.Date;

                ingredientsWithStock.Add(new CalculatedIngredientDto
                {
                    ProductCode = ingredient.ProductCode,
                    ProductName = ingredient.ProductName,
                    OriginalAmount = ingredient.Amount,
                    CalculatedAmount = Math.Round(ingredient.Amount * scaleFactor, 2),
                    Price = ingredient.Price,
                    StockTotal = ingredientCatalog?.Stock?.Total ?? 0,
                    LastStockTaking = lastStockTaking
                });
            }

            return new CalculatedBatchSizeResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                OriginalBatchSize = template.OriginalAmount,
                NewBatchSize = request.DesiredBatchSize.Value,
                ScaleFactor = scaleFactor,
                Ingredients = ingredientsWithStock
            };
        }
        catch (Exception ex)
        {
            return new CalculatedBatchSizeResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}