using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler
    : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureClient _manufactureClient;

    public GetProductCompositionHandler(IManufactureClient manufactureClient)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(
            request.ProductCode,
            cancellationToken);

        if (template is null)
        {
            return new GetProductCompositionResponse { Ingredients = new List<IngredientDto>() };
        }

        // Sort by Flexi BoM order (poradi). Zero means unordered — push those last, then alphabetical.
        var sorted = template.Ingredients
            .OrderBy(i => i.Order == 0 ? int.MaxValue : i.Order)
            .ThenBy(i => i.ProductName)
            .Select((i, index) => new IngredientDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                Amount = i.Amount,
                Unit = "g",
                Order = index + 1,
                PhaseLabel = i.PhaseLabel,
            })
            .ToList();

        return new GetProductCompositionResponse { Ingredients = sorted };
    }
}
