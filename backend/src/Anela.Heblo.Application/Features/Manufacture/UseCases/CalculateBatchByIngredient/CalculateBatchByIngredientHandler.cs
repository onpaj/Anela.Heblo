using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;

public class CalculateBatchByIngredientHandler : IRequestHandler<CalculateBatchByIngredientRequest, CalculateBatchByIngredientResponse>
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly ICatalogRepository _catalogRepository;

    public CalculateBatchByIngredientHandler(IManufactureRepository manufactureRepository, ICatalogRepository catalogRepository)
    {
        _manufactureRepository = manufactureRepository;
        _catalogRepository = catalogRepository;
    }

    public async Task<CalculateBatchByIngredientResponse> Handle(CalculateBatchByIngredientRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            var targetIngredient = template.Ingredients.FirstOrDefault(i => i.ProductCode == request.IngredientCode);
            if (targetIngredient == null)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.IngredientNotFoundInTemplate,
                    new Dictionary<string, string> { { "IngredientCode", request.IngredientCode } });
            }

            if (targetIngredient.Amount <= 0)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.InvalidIngredientAmount,
                    new Dictionary<string, string> { { "Amount", targetIngredient.Amount.ToString() } });
            }

            var scaleFactor = request.DesiredIngredientAmount / targetIngredient.Amount;
            var newBatchSize = template.OriginalAmount * scaleFactor;

            // Create ingredients list with stock information
            var ingredientsWithStock = new List<CalculatedIngredientDto>();
            foreach (var ingredient in template.Ingredients)
            {
                var ingredientCatalog = await _catalogRepository.GetByIdAsync(ingredient.ProductCode, cancellationToken);

                ingredientsWithStock.Add(new CalculatedIngredientDto
                {
                    ProductCode = ingredient.ProductCode,
                    ProductName = ingredient.ProductName,
                    OriginalAmount = ingredient.Amount,
                    CalculatedAmount = Math.Round(ingredient.Amount * scaleFactor, 2),
                    Price = ingredient.Price,
                    StockTotal = ingredientCatalog?.Stock?.Total ?? 0
                });
            }

            return new CalculateBatchByIngredientResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                OriginalBatchSize = template.OriginalAmount,
                NewBatchSize = Math.Round(newBatchSize, 2),
                ScaleFactor = scaleFactor,
                ScaledIngredientCode = targetIngredient.ProductCode,
                ScaledIngredientName = targetIngredient.ProductName,
                ScaledIngredientOriginalAmount = targetIngredient.Amount,
                ScaledIngredientNewAmount = request.DesiredIngredientAmount,
                Ingredients = ingredientsWithStock
            };
        }
        catch (Exception ex)
        {
            return new CalculateBatchByIngredientResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}