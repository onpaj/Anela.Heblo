using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageHandler : IRequestHandler<GetProductUsageRequest, GetProductUsageResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ICatalogRepository _catalogRepository;

    public GetProductUsageHandler(
        IManufactureClient manufactureClient,
        ICatalogRepository catalogRepository)
    {
        _manufactureClient = manufactureClient;
        _catalogRepository = catalogRepository;
    }

    public async Task<GetProductUsageResponse> Handle(GetProductUsageRequest request, CancellationToken cancellationToken)
    {
        // Get manufacture templates
        var manufactureTemplates = await _manufactureClient.FindByIngredientAsync(request.ProductCode, cancellationToken);

        // Apply MMQ scaling if configured
        foreach (var template in manufactureTemplates)
        {
            var ingredientProduct = await _catalogRepository.GetByIdAsync(template.ProductCode, cancellationToken);
            // Preserve original amounts for scaling reference
            if (template.OriginalAmount == 0)
            {
                template.OriginalAmount = template.Amount; // Use current Amount as fallback
            }

            // Skip scaling if MMQ is not configured or template base quantity is invalid
            if (ingredientProduct == null || ingredientProduct.MinimalManufactureQuantity <= 0 || template.OriginalAmount <= 0)
            {
                continue; // No scaling applied
            }

            // Calculate scaling factor based on MMQ vs template base quantity
            var scalingFactor = ingredientProduct.MinimalManufactureQuantity / template.BatchSize;

            // Apply scaling to template
            template.Amount = template.OriginalAmount * scalingFactor;
            template.BatchSize = ingredientProduct.MinimalManufactureQuantity;
        }

        return new GetProductUsageResponse
        {
            ManufactureTemplates = manufactureTemplates
        };
    }
}