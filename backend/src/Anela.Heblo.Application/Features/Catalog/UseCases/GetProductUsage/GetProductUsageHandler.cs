using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageHandler : IRequestHandler<GetProductUsageRequest, GetProductUsageResponse>
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly ICatalogRepository _catalogRepository;

    public GetProductUsageHandler(
        IManufactureRepository manufactureRepository,
        ICatalogRepository catalogRepository)
    {
        _manufactureRepository = manufactureRepository;
        _catalogRepository = catalogRepository;
    }

    public async Task<GetProductUsageResponse> Handle(GetProductUsageRequest request, CancellationToken cancellationToken)
    {
        // Get the current catalog item to access its MMQ
        var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
            x => x.ProductCode == request.ProductCode,
            cancellationToken);

        if (catalogItem == null)
        {
            return new GetProductUsageResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ProductNotFound,
                Params = new Dictionary<string, string> { { "productCode", request.ProductCode } }
            };
        }

        // Get manufacture templates
        var manufactureTemplates = await _manufactureRepository.FindByIngredientAsync(request.ProductCode, cancellationToken);

        // Scale templates based on current item's MMQ
        var scaledTemplates = ScaleTemplatesByMmq(manufactureTemplates, catalogItem.MinimalManufactureQuantity);

        return new GetProductUsageResponse
        {
            ManufactureTemplates = scaledTemplates
        };
    }

    /// <summary>
    /// Scales manufacture templates based on the current item's MinimumManufactureQuantity (MMQ).
    /// Formula: displayed_amount = template_amount * (MMQ / template_base_quantity)
    /// </summary>
    private List<ManufactureTemplate> ScaleTemplatesByMmq(List<ManufactureTemplate> templates, double mmq)
    {
        if (mmq <= 0 || templates.Count == 0)
        {
            return templates; // No scaling if MMQ is not configured or no templates
        }

        var scaledTemplates = new List<ManufactureTemplate>();

        foreach (var template in templates)
        {
            // Use OriginalAmount as primary base, fallback to Amount if OriginalAmount is invalid
            var templateBaseQuantity = template.OriginalAmount > 0 ? template.OriginalAmount : template.Amount;

            if (templateBaseQuantity <= 0)
            {
                scaledTemplates.Add(template); // No scaling if base quantity is invalid
                continue;
            }

            // Calculate scaling factor
            var scalingFactor = mmq / templateBaseQuantity;

            // Create scaled template
            var scaledTemplate = new ManufactureTemplate
            {
                TemplateId = template.TemplateId,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                Amount = template.Amount * scalingFactor, // Scale the amount
                OriginalAmount = template.OriginalAmount, // Preserve original amount
                Ingredients = ScaleIngredients(template.Ingredients, scalingFactor)
            };

            scaledTemplates.Add(scaledTemplate);
        }

        return scaledTemplates;
    }

    /// <summary>
    /// Scales ingredient amounts by the given scaling factor
    /// </summary>
    private List<Ingredient> ScaleIngredients(List<Ingredient> ingredients, double scalingFactor)
    {
        return ingredients.Select(ingredient => new Ingredient
        {
            TemplateId = ingredient.TemplateId,
            ProductCode = ingredient.ProductCode,
            ProductName = ingredient.ProductName,
            Amount = ingredient.Amount * scalingFactor, // Scale the amount
            OriginalAmount = ingredient.OriginalAmount, // Preserve original amount
            Price = ingredient.Price // Price remains unchanged
        }).ToList();
    }
}